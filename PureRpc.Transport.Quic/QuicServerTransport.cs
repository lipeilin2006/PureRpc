using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.IO.Pipelines;
using System.Net;
using System.Net.Quic;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.ObjectPool;
using Microsoft.Extensions.Options;
using PureRpc.Abstractions;

namespace PureRpc.Transport.Quic;

internal sealed partial class QuicServerTransport : IServerTransport
{
    private readonly QuicServerOptions _options;
    private readonly ILogger<QuicServerTransport> _logger;
    private readonly ObjectPool<RpcContext> _contextPool;
    private readonly SemaphoreSlim _requestThrottle = new(RpcProtocolConstants.DefaultRequestThrottleLimit, RpcProtocolConstants.DefaultRequestThrottleLimit);

    private readonly ConcurrentDictionary<(long ConnectionId, uint RequestId), CancellationTokenSource> _activeRequests = new();
    private readonly ConcurrentDictionary<long, (PipeWriter Writer, SemaphoreSlim Lock)> _connections = new(Environment.ProcessorCount, 256);

    private QuicListener? _listener;
    private long _nextConnectionId;
    private bool _disposed;

    public QuicServerTransport(IOptions<QuicServerOptions> options, ILogger<QuicServerTransport>? logger = null)
    {
        _options = options.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? NullLogger<QuicServerTransport>.Instance;
        _contextPool = RpcContextPolicy.CreatePool();
    }

    public async Task StartAsync(Func<RpcContext, ReadOnlySequence<byte>, Task> onRequestReceived, CancellationToken ct)
    {
        var cert = _options.ServerCertificate;
        if (cert == null && _options.ServerCertificatePath != null)
        {
            cert = X509CertificateLoader.LoadPkcs12(File.ReadAllBytes(_options.ServerCertificatePath), _options.ServerCertificatePassword ?? "");
        }
        if (cert == null)
            throw new InvalidOperationException("QUIC server requires a TLS certificate.");

        var listenEp = (IPEndPoint)_options.ListenEndPoint;
        var listenerOptions = new QuicListenerOptions
        {
            ListenEndPoint = listenEp,
            ApplicationProtocols = [new SslApplicationProtocol("purerpc")],
            ConnectionOptionsCallback = (_, _, _) =>
            {
                return ValueTask.FromResult(new QuicServerConnectionOptions
                {
                    DefaultCloseErrorCode = 0,
                    DefaultStreamErrorCode = 0,
                    ServerAuthenticationOptions = new SslServerAuthenticationOptions
                    {
                        ServerCertificate = cert,
                        ApplicationProtocols = [new SslApplicationProtocol("purerpc")],
                    },
                    MaxInboundBidirectionalStreams = _options.MaxInboundBidirectionalStreams,
                });
            },
            ListenBacklog = _options.Backlog,
        };

        _listener = await QuicListener.ListenAsync(listenerOptions, ct).ConfigureAwait(false);
        LogListening(_logger, _options.ListenEndPoint);

        while (!ct.IsCancellationRequested)
        {
            try
            {
                var connection = await _listener.AcceptConnectionAsync(ct).ConfigureAwait(false);
                long connId = Interlocked.Increment(ref _nextConnectionId);
                _ = HandleConnectionAsync(connection, connId, onRequestReceived, ct);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex) when (!_disposed)
            {
                LogAcceptError(_logger, ex);
            }
        }
    }

    private async Task HandleConnectionAsync(QuicConnection connection, long connId,
        Func<RpcContext, ReadOnlySequence<byte>, Task> onRequest, CancellationToken ct)
    {
        PipeReader? reader = null;
        PipeWriter? writer = null;
        var writeLock = new SemaphoreSlim(1, 1);

        try
        {
            var stream = await connection.AcceptInboundStreamAsync(ct).ConfigureAwait(false);
            var remoteEP = connection.RemoteEndPoint;

            reader = PipeReader.Create(stream);
            writer = PipeWriter.Create(stream);
            _connections[connId] = (writer, writeLock);

            while (!ct.IsCancellationRequested)
            {
                ReadResult result = await reader.ReadAsync(ct).ConfigureAwait(false);
                var buffer = result.Buffer;

                while (TryParseRequest(ref buffer, connId, out var header, out var payload))
                {
                    var context = _contextPool.Get();
                    context.PopulateRequest(connId, header.RequestId, header.ServiceName, header.MethodName, remoteEP, header.Headers);

                    var requestCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    var key = (connId, header.RequestId);
                    _activeRequests[key] = requestCts;
                    context.CancellationToken = requestCts.Token;

                    _ = ProcessRequestAsync(onRequest, context, payload, requestCts, key);
                }

                reader.AdvanceTo(buffer.Start, buffer.End);
                if (result.IsCompleted) break;
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (!_disposed)
        {
            LogConnectionError(_logger, connId, ex.Message);
        }
        finally
        {
            _connections.TryRemove(connId, out _);
            if (reader != null) await reader.CompleteAsync().ConfigureAwait(false);
            if (writer != null) await writer.CompleteAsync().ConfigureAwait(false);
            writeLock.Dispose();
            await connection.DisposeAsync().ConfigureAwait(false);
        }
    }

    private async Task ProcessRequestAsync(
        Func<RpcContext, ReadOnlySequence<byte>, Task> handler, RpcContext context,
        ReadOnlySequence<byte> payload, CancellationTokenSource requestCts,
        (long ConnectionId, uint RequestId) key)
    {
        await _requestThrottle.WaitAsync().ConfigureAwait(false);
        try
        {
            await handler(context, payload).ConfigureAwait(false);
        }
        finally
        {
            _requestThrottle.Release();
            if (_activeRequests.TryRemove(key, out var removed))
                removed.Dispose();
        }
    }

    public async ValueTask SendResponseAsync(RpcContext context, CancellationToken ct)
    {
        try
        {
            if (_connections.TryGetValue(context.ConnectionId, out var conn))
            {
                await SendInternalAsync(context, conn.Writer, conn.Lock, ct).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            LogSendError(_logger, ex, context.RequestId);
        }
        finally
        {
            _contextPool.Return(context);
        }
    }

    private async ValueTask SendInternalAsync(RpcContext ctx, PipeWriter writer, SemaphoreSlim locker, CancellationToken ct)
    {
        var data = ((ArrayBufferWriter<byte>)ctx.ResponseBuffer).WrittenMemory;
        var headerInfo = RpcFrameCodec.PrepareHeaders(ctx.HeadersOrNull);

        int bodyLen = 1 + 4 + 4 + 4 + headerInfo.HeadersBlockSize + data.Length;
        int frameHeaderSize = 4 + 1 + 4 + 4 + 4 + headerInfo.HeadersBlockSize;

        await locker.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var span = writer.GetSpan(frameHeaderSize);
            BinaryPrimitives.WriteInt32LittleEndian(span[..4], bodyLen);
            span[4] = (byte)(ctx.IsAborted ? RpcMessageType.Error : RpcMessageType.Response);
            BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(5, 4), ctx.RequestId);
            BinaryPrimitives.WriteInt32LittleEndian(span.Slice(9, 4), ctx.IsAborted ? 500 : 200);

            int pos = 13;
            pos = RpcFrameCodec.WriteHeaders(span, pos, in headerInfo);

            writer.Advance(frameHeaderSize);
            if (data.Length > 0) writer.Write(data.Span);
            await writer.FlushAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            locker.Release();
        }
    }

    private bool TryParseRequest(ref ReadOnlySequence<byte> buffer, long connectionId,
        out (uint RequestId, string ServiceName, string MethodName, IReadOnlyDictionary<string, string>? Headers) header,
        out ReadOnlySequence<byte> payload)
    {
        header = default; payload = default;
        if (buffer.Length < 9) return false;

        Span<byte> headSpan = stackalloc byte[9];
        buffer.Slice(0, 9).CopyTo(headSpan);
        int totalLen = BinaryPrimitives.ReadInt32LittleEndian(headSpan[..4]);

        if (totalLen < 5 || totalLen > RpcProtocolConstants.MaxFrameSize) return false;
        if (buffer.Length < totalLen + 4U) return false;

        byte type = headSpan[4];
        uint reqId = BinaryPrimitives.ReadUInt32LittleEndian(headSpan.Slice(5, 4));

        if (type == (byte)RpcMessageType.Cancel)
        {
            var key = (connectionId, reqId);
            if (_activeRequests.TryRemove(key, out var cts))
            {
                cts.Cancel();
                cts.Dispose();
            }
            buffer = buffer.Slice(totalLen + 4);
            return false;
        }

        if (type != (byte)RpcMessageType.Request) { buffer = buffer.Slice(totalLen + 4); return false; }

        if (totalLen < 13) return false;

        var reader = new SequenceReader<byte>(buffer.Slice(9, totalLen - 5));

        if (!RpcFrameCodec.TryReadString(ref reader, out var svc, RpcProtocolConstants.MaxServiceNameLength)) return false;
        if (!RpcFrameCodec.TryReadString(ref reader, out var met, RpcProtocolConstants.MaxMethodNameLength)) return false;

        if (!reader.TryReadLittleEndian(out int headerCount)) return false;
        if (!RpcFrameCodec.TryParseHeadersSequence(ref reader, headerCount, out var headers)) return false;

        header = (reqId, svc, met, headers);
        payload = reader.UnreadSequence;
        buffer = buffer.Slice(totalLen + 4);
        return true;
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        _connections.Clear();
        _activeRequests.Clear();
        _requestThrottle.Dispose();
        if (_listener != null) await _listener.DisposeAsync().ConfigureAwait(false);
    }

    #region Logging
    [LoggerMessage(EventId = 1301, Level = LogLevel.Information, Message = "[QUIC] Server listening on {EndPoint}")]
    private static partial void LogListening(ILogger logger, System.Net.EndPoint endPoint);
    [LoggerMessage(EventId = 1302, Level = LogLevel.Error, Message = "[QUIC] Accept error")]
    private static partial void LogAcceptError(ILogger logger, Exception ex);
    [LoggerMessage(EventId = 1303, Level = LogLevel.Error, Message = "[QUIC] Connection {Id} error: {Msg}")]
    private static partial void LogConnectionError(ILogger logger, long id, string msg);
    [LoggerMessage(EventId = 1304, Level = LogLevel.Error, Message = "[QUIC] Request error")]
    private static partial void LogRequestError(ILogger logger, Exception ex);
    [LoggerMessage(EventId = 1305, Level = LogLevel.Error, Message = "[QUIC] Send error for Request {Id}")]
    private static partial void LogSendError(ILogger logger, Exception ex, uint id);
    #endregion
}
