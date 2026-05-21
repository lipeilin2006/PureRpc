using System.Buffers;
using System.Collections.Concurrent;
using System.Buffers.Binary;
using System.IO.Pipelines;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.ObjectPool;
using Microsoft.Extensions.Options;
using PureRpc.Abstractions;

namespace PureRpc.Transport.Tcp;

internal sealed partial class TcpServerTransport : IServerTransport
{
    private readonly TcpServerOptions _options;
    private readonly ILogger<TcpServerTransport> _logger;
    private readonly ObjectPool<RpcContext> _contextPool;
    private Socket? _listenSocket;
    private bool _disposed;
    private long _nextConnectionId;
    private int _activeConnectionCount;
    private readonly SemaphoreSlim _requestThrottle = new(RpcProtocolConstants.DefaultRequestThrottleLimit, RpcProtocolConstants.DefaultRequestThrottleLimit);

    private readonly ConcurrentDictionary<long, (PipeWriter Writer, SemaphoreSlim Lock)> _connections =
        new(Environment.ProcessorCount, 256);

    private readonly ConcurrentDictionary<(long ConnectionId, uint RequestId), CancellationTokenSource> _activeRequests =
        new(Environment.ProcessorCount, 1024);

    public TcpServerTransport(IOptions<TcpServerOptions> options, ILogger<TcpServerTransport>? logger = null)
    {
        _options = options.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? NullLogger<TcpServerTransport>.Instance;
        _contextPool = RpcContextPolicy.CreatePool();
    }

    #region Source Generated Logging
    [LoggerMessage(EventId = 101, Level = LogLevel.Information, Message = "[TcpServer] Listening on {EndPoint}")]
    private static partial void LogListening(ILogger logger, System.Net.EndPoint endPoint);

    [LoggerMessage(EventId = 102, Level = LogLevel.Error, Message = "[TcpServer] Error in accept loop.")]
    private static partial void LogAcceptError(ILogger logger, Exception ex);

    [LoggerMessage(EventId = 103, Level = LogLevel.Debug, Message = "[TcpServer] Conn {Id} closed: {Msg}")]
    private static partial void LogConnectionClosed(ILogger logger, long id, string msg);

    [LoggerMessage(EventId = 104, Level = LogLevel.Error, Message = "[TcpServer] Failed to send response for Request {Id}")]
    private static partial void LogSendError(ILogger logger, Exception ex, uint id);
    #endregion

    public async Task StartAsync(Func<RpcContext, ReadOnlySequence<byte>, Task> onRequestReceived, CancellationToken ct)
    {
        if (_listenSocket != null) throw new InvalidOperationException("Server already running.");

        _listenSocket = new Socket(_options.EndPoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
        if (_options.ReuseAddress) _listenSocket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);

        try
        {
            _listenSocket.Bind(_options.EndPoint);
            _listenSocket.Listen(_options.Backlog);

            LogListening(_logger, _options.EndPoint);

            while (!ct.IsCancellationRequested)
            {
                if (Volatile.Read(ref _activeConnectionCount) >= _options.MaxConnections)
                {
                    await Task.Delay(100, ct).ConfigureAwait(false);
                    continue;
                }

                Socket clientSocket = await _listenSocket.AcceptAsync(ct).ConfigureAwait(false);
                clientSocket.NoDelay = true;

                long connId = Interlocked.Increment(ref _nextConnectionId);
                Interlocked.Increment(ref _activeConnectionCount);
                _ = HandleClientAsync(clientSocket, connId, onRequestReceived, ct);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (!_disposed)
        {
            LogAcceptError(_logger, ex);
        }
    }

    private async Task HandleClientAsync(Socket socket, long connId, Func<RpcContext, ReadOnlySequence<byte>, Task> onRequest, CancellationToken ct)
    {
        var remoteEP = socket.RemoteEndPoint;
        var netStream = new NetworkStream(socket, ownsSocket: true);
        Stream stream = netStream;
        if (_options.ServerCertificate != null)
        {
            var sslStream = new SslStream(netStream, leaveInnerStreamOpen: false);
            try
            {
                await sslStream.AuthenticateAsServerAsync(
                    new SslServerAuthenticationOptions { ServerCertificate = _options.ServerCertificate },
                    ct).ConfigureAwait(false);
            }
            catch
            {
                sslStream.Dispose();
                throw;
            }
            stream = sslStream;
        }

        var reader = PipeReader.Create(stream);
        var writer = PipeWriter.Create(stream);
        var writeLock = new SemaphoreSlim(1, 1);

        _connections[connId] = (writer, writeLock);

        try
        {
            while (!ct.IsCancellationRequested)
            {
                ReadResult result = await reader.ReadAsync(ct).ConfigureAwait(false);
                ReadOnlySequence<byte> buffer = result.Buffer;

                while (RpcFrameCodec.TryParseRequest(ref buffer, connId, _activeRequests, out var request, out var payload))
                {
                    var context = _contextPool.Get();
                    context.PopulateRequest(connId, request.RequestId, request.ServiceName, request.MethodName, remoteEP, request.Headers);

                    var requestCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    var key = (connId, request.RequestId);
                    _activeRequests[key] = requestCts;
                    context.CancellationToken = requestCts.Token;

                    _ = ProcessRequestAsync(onRequest, context, payload, requestCts, key);
                }

                reader.AdvanceTo(buffer.Start, buffer.End);
                if (result.IsCompleted) break;
            }
        }
        catch (Exception ex)
        {
            LogConnectionClosed(_logger, connId, ex.Message);
        }
        finally
        {
            Interlocked.Decrement(ref _activeConnectionCount);
            _connections.TryRemove(connId, out _);
            await reader.CompleteAsync().ConfigureAwait(false);
            await writer.CompleteAsync().ConfigureAwait(false);
            writeLock.Dispose();
            stream.Dispose();
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

            int headerOffset = 13;
            headerOffset = RpcFrameCodec.WriteHeaders(span, headerOffset, in headerInfo);

            writer.Advance(frameHeaderSize);
            if (data.Length > 0)
            {
                writer.Write(data.Span);
            }

            await writer.FlushAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            locker.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        _listenSocket?.Close();
        _connections.Clear();
        _requestThrottle.Dispose();
        await Task.CompletedTask;
    }
}
