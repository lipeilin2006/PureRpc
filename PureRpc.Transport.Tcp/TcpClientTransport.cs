using System.Buffers;
using System.Buffers.Binary;
using System.IO.Pipelines;
using System.Net.Security;
using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PureRpc.Abstractions;

namespace PureRpc.Transport.Tcp;

internal sealed partial class TcpClientTransport : IClientTransport
{
    private readonly TcpClientOptions _options;
    private readonly ILogger<TcpClientTransport> _logger;
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private readonly CancellationTokenSource _transportCts = new();

    private Socket? _socket;
    private Stream? _stream;
    private PipeWriter? _writer;
    private Task? _receiveTask;
    private bool _disposed;
    private bool _isConnected;

    public bool IsConnected => _isConnected && (_socket?.Connected ?? false);

    public TcpClientTransport(IOptions<TcpClientOptions> options, ILogger<TcpClientTransport>? logger = null)
    {
        _options = options.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? NullLogger<TcpClientTransport>.Instance;
    }

    #region Source Generated Logging
    [LoggerMessage(EventId = 501, Level = LogLevel.Information, Message = "[TcpClient] Connecting to {Target}")]
    private static partial void LogConnecting(ILogger logger, System.Net.EndPoint target);

    [LoggerMessage(EventId = 502, Level = LogLevel.Error, Message = "[TcpClient] Failed to connect to {Target}.")]
    private static partial void LogConnectError(ILogger logger, Exception ex, System.Net.EndPoint target);

    [LoggerMessage(EventId = 503, Level = LogLevel.Error, Message = "[TcpClient] Receive loop error.")]
    private static partial void LogReceiveError(ILogger logger, Exception ex);

    [LoggerMessage(EventId = 504, Level = LogLevel.Warning, Message = "[TcpClient] Connection lost.")]
    private static partial void LogConnectionLost(ILogger logger);

    [LoggerMessage(EventId = 505, Level = LogLevel.Debug, Message = "[TcpClient] Request {RequestId} cancelled.")]
    private static partial void LogRequestCancelled(ILogger logger, uint requestId);
    #endregion

    public async Task ConnectAsync(Action<uint, ReadOnlySequence<byte>, bool, string?, IReadOnlyDictionary<string, string>?> onResponseReceived, CancellationToken ct)
    {
        if (_isConnected) return;

        var target = _options.RemoteEndPoint;
        LogConnecting(_logger, target);

        _socket = new Socket(target.AddressFamily, SocketType.Stream, ProtocolType.Tcp)
        {
            NoDelay = _options.NoDelay,
            SendBufferSize = _options.SendBufferSize,
            ReceiveBufferSize = _options.ReceiveBufferSize
        };

        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(_options.ConnectTimeout);

            await _socket.ConnectAsync(target, timeoutCts.Token).ConfigureAwait(false);

            _isConnected = true;

            var netStream = new NetworkStream(_socket, ownsSocket: true);
            Stream stream = netStream;
            if (_options.TargetHost != null)
            {
                var sslStream = new SslStream(netStream, leaveInnerStreamOpen: false);
                try
                {
                    await sslStream.AuthenticateAsClientAsync(
                        new SslClientAuthenticationOptions { TargetHost = _options.TargetHost },
                        ct).ConfigureAwait(false);
                }
                catch
                {
                    sslStream.Dispose();
                    throw;
                }
                stream = sslStream;
            }

            _stream = stream;

            var reader = PipeReader.Create(stream);
            _writer = PipeWriter.Create(stream);

            _receiveTask = RunReceiveLoopAsync(reader, onResponseReceived);
        }
        catch (Exception ex)
        {
            _isConnected = false;
            _socket?.Dispose();
            LogConnectError(_logger, ex, target);
            throw;
        }
    }

    public async ValueTask SendAsync(uint requestId, string serviceName, string methodName, ReadOnlySequence<byte> data, CancellationToken ct, IDictionary<string, string>? headers = null)
    {
        if (!IsConnected || _writer == null)
            throw new IOException("Transport is not connected.");

        int svcByteCount = Encoding.UTF8.GetByteCount(serviceName);
        int metByteCount = Encoding.UTF8.GetByteCount(methodName);
        var headerInfo = RpcFrameCodec.PrepareHeaders(headers as IReadOnlyDictionary<string, string>);

        int bodyLen = 1 + 4 + 4 + svcByteCount + 4 + metByteCount + 4 + headerInfo.HeadersBlockSize + (int)data.Length;
        int headerTotal = 4 + 1 + 4 + 4 + svcByteCount + 4 + metByteCount + 4 + headerInfo.HeadersBlockSize;

        await _sendLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var span = _writer.GetSpan(headerTotal);

            BinaryPrimitives.WriteInt32LittleEndian(span[..4], bodyLen);
            int written = RpcFrameCodec.WriteRequestSpan(span.Slice(4), requestId, serviceName, methodName, in headerInfo, svcByteCount, metByteCount);

            _writer.Advance(headerTotal);

            if (data.Length > 0)
            {
                if (data.IsSingleSegment)
                {
                    _writer.Write(data.FirstSpan);
                }
                else
                {
                    foreach (var segment in data)
                    {
                        _writer.Write(segment.Span);
                    }
                }
            }

            var result = await _writer.FlushAsync(ct).ConfigureAwait(false);
            if (result.IsCompleted) throw new IOException("Connection closed during flush.");
        }
        finally
        {
            _sendLock.Release();
        }
    }

    private async Task RunReceiveLoopAsync(PipeReader reader, Action<uint, ReadOnlySequence<byte>, bool, string?, IReadOnlyDictionary<string, string>?> onResponseReceived)
    {
        try
        {
            while (!_transportCts.Token.IsCancellationRequested)
            {
                ReadResult result = await reader.ReadAsync(_transportCts.Token).ConfigureAwait(false);
                ReadOnlySequence<byte> buffer = result.Buffer;

                while (TryParseResponse(ref buffer, out var requestId, out var type, out var statusCode, out var headers, out var payload))
                {
                    bool isSuccess = type != RpcMessageType.Error && statusCode == 200;
                    string? errorMsg = isSuccess ? null : RpcFrameCodec.DecodeUtf8(payload);

                    onResponseReceived(requestId, payload, isSuccess, errorMsg, headers);
                }

                reader.AdvanceTo(buffer.Start, buffer.End);
                if (result.IsCompleted) break;
            }
        }
        catch (Exception ex) when (!_disposed)
        {
            LogReceiveError(_logger, ex);
        }
        finally
        {
            _isConnected = false;
            await reader.CompleteAsync().ConfigureAwait(false);
            LogConnectionLost(_logger);
        }
    }

    private bool TryParseResponse(ref ReadOnlySequence<byte> buffer, out uint requestId, out RpcMessageType type, out int statusCode, out IReadOnlyDictionary<string, string>? headers, out ReadOnlySequence<byte> payload)
    {
        requestId = 0;
        type = default;
        statusCode = 0;
        headers = null;
        payload = default;

        if (buffer.Length < 17) return false;

        Span<byte> headSpan = stackalloc byte[17];
        buffer.Slice(0, 17).CopyTo(headSpan);

        int totalLen = BinaryPrimitives.ReadInt32LittleEndian(headSpan[..4]);
        if (totalLen < 13 || buffer.Length < totalLen + 4) return false;

        type = (RpcMessageType)headSpan[4];
        requestId = BinaryPrimitives.ReadUInt32LittleEndian(headSpan.Slice(5, 4));
        statusCode = BinaryPrimitives.ReadInt32LittleEndian(headSpan.Slice(9, 4));
        int headerCount = BinaryPrimitives.ReadInt32LittleEndian(headSpan.Slice(13, 4));
        if (headerCount < 0 || headerCount > RpcProtocolConstants.MaxHeaderCount) return false;

        var remaining = buffer.Slice(17, totalLen - 13);
        if (headerCount > 0)
        {
            var seqReader = new SequenceReader<byte>(remaining);
            if (!RpcFrameCodec.TryParseHeadersSequence(ref seqReader, headerCount, out var dict)) return false;
            headers = dict;
            payload = remaining.Slice(seqReader.Consumed);
        }
        else
        {
            payload = remaining;
        }

        buffer = buffer.Slice(totalLen + 4);
        return true;
    }

    public async ValueTask CancelRequestAsync(uint requestId, CancellationToken ct = default)
    {
        if (!IsConnected || _writer == null) return;

        await _sendLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            LogRequestCancelled(_logger, requestId);
            var span = _writer.GetSpan(9);
            BinaryPrimitives.WriteInt32LittleEndian(span[..4], 5);
            RpcFrameCodec.WriteCancelSpan(span.Slice(5), requestId);
            _writer.Advance(9);
            await _writer.FlushAsync().ConfigureAwait(false);
        }
        finally { _sendLock.Release(); }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        _isConnected = false;

        _transportCts.Cancel();

        if (_writer != null) await _writer.CompleteAsync().ConfigureAwait(false);
        _stream?.Dispose();
        _socket?.Dispose();

        if (_receiveTask != null)
        {
            try { await _receiveTask.ConfigureAwait(false); } catch { /* Ignore */ }
        }

        _sendLock.Dispose();
        _transportCts.Dispose();
    }
}
