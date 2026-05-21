using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PureRpc.Abstractions;

namespace PureRpc.Transport.Websocket;

internal sealed partial class WebSocketClientTransport : IClientTransport
{
    private readonly WebSocketClientOptions _options;
    private readonly ILogger<WebSocketClientTransport> _logger;

    private ClientWebSocket? _socket;
    private Task? _receiveTask;
    private CancellationTokenSource? _receiveCts;
    private bool _disposed;

    private Action<uint, ReadOnlySequence<byte>, bool, string?, IReadOnlyDictionary<string, string>?>? _onResponse;

    public bool IsConnected => _socket?.State == WebSocketState.Open;

    public WebSocketClientTransport(IOptions<WebSocketClientOptions> options, ILogger<WebSocketClientTransport>? logger = null)
    {
        _options = options.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? NullLogger<WebSocketClientTransport>.Instance;
    }

    public async Task ConnectAsync(
        Action<uint, ReadOnlySequence<byte>, bool, string?, IReadOnlyDictionary<string, string>?> onResponseReceived,
        CancellationToken ct)
    {
        _onResponse = onResponseReceived ?? throw new ArgumentNullException(nameof(onResponseReceived));

        _socket = new ClientWebSocket();
        _receiveCts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        using var timeoutCts = new CancellationTokenSource(_options.ConnectTimeoutMs);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

        await _socket.ConnectAsync(new Uri(_options.Url), linkedCts.Token).ConfigureAwait(false);
        LogConnected(_logger, _options.Url);

        _receiveTask = ReceiveLoopAsync(_receiveCts.Token);
    }

    public async ValueTask SendAsync(
        uint requestId, string serviceName, string methodName,
        ReadOnlySequence<byte> data, CancellationToken ct, IDictionary<string, string>? headers = null)
    {
        if (_socket?.State != WebSocketState.Open)
            throw new IOException("WebSocket is not connected.");

        int svcByteCount = Encoding.UTF8.GetByteCount(serviceName);
        int metByteCount = Encoding.UTF8.GetByteCount(methodName);
        var headerInfo = RpcFrameCodec.PrepareHeaders(headers as IReadOnlyDictionary<string, string>);

        int bodyLen = 1 + 4 + 4 + svcByteCount + 4 + metByteCount + 4 + headerInfo.HeadersBlockSize + (int)data.Length;
        byte[] rented = ArrayPool<byte>.Shared.Rent(bodyLen);
        try
        {
            int written = RpcFrameCodec.WriteRequestSpan(rented, requestId, serviceName, methodName, in headerInfo, svcByteCount, metByteCount);
            if (data.Length > 0)
            {
                if (data.IsSingleSegment)
                    data.FirstSpan.CopyTo(rented.AsSpan(written));
                else
                    foreach (var seg in data)
                    {
                        seg.Span.CopyTo(rented.AsSpan(written));
                        written += seg.Length;
                    }
            }

            await _socket.SendAsync(new ArraySegment<byte>(rented, 0, bodyLen), WebSocketMessageType.Binary, true, ct).ConfigureAwait(false);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented);
        }
    }

    public async ValueTask CancelRequestAsync(uint requestId, CancellationToken ct = default)
    {
        if (_socket?.State != WebSocketState.Open) return;
        byte[] rented = ArrayPool<byte>.Shared.Rent(5);
        try
        {
            RpcFrameCodec.WriteCancelSpan(rented, requestId);
            await _socket.SendAsync(new ArraySegment<byte>(rented, 0, 5), WebSocketMessageType.Binary, true, ct).ConfigureAwait(false);
        }
        catch (Exception ex) { LogCancelError(_logger, ex); }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented);
        }
    }

    private async Task ReceiveLoopAsync(CancellationToken ct)
    {
        var recvBuffer = new byte[8192];
        var messageBuffer = new MemoryStream();
        try
        {
            while (!ct.IsCancellationRequested)
            {
                WebSocketReceiveResult result;
                messageBuffer.SetLength(0);
                do
                {
                    result = await _socket!.ReceiveAsync(new ArraySegment<byte>(recvBuffer), ct).ConfigureAwait(false);
                    messageBuffer.Write(recvBuffer, 0, result.Count);
                }
                while (!result.EndOfMessage);

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    await _socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None);
                    break;
                }

                ProcessResponse(messageBuffer.ToArray());
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (!_disposed)
        {
            LogReceiveError(_logger, ex);
        }
    }

    private void ProcessResponse(byte[] message)
    {
        if (message.Length < 13 || _onResponse == null) return;
        var span = message.AsSpan();
        byte type = span[0];
        uint requestId = BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(1, 4));
        int statusCode = BinaryPrimitives.ReadInt32LittleEndian(span.Slice(5, 4));
        int headerCount = BinaryPrimitives.ReadInt32LittleEndian(span.Slice(9, 4));
        int offset = 13;
        IReadOnlyDictionary<string, string>? headers = null;
        if (headerCount > 0)
        {
            if (!RpcFrameCodec.TryParseHeadersSpan(span, ref offset, headerCount, out var dict)) return;
            headers = dict;
        }
        bool isSuccess = type != (byte)RpcMessageType.Error && statusCode == 200;
        var payload = new ReadOnlySequence<byte>(message, offset, message.Length - offset);
        _onResponse(requestId, payload, isSuccess, isSuccess ? null : Encoding.UTF8.GetString(span.Slice(offset)), headers);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        _receiveCts?.Cancel();
        if (_socket != null)
        {
            try { await _socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None); } catch { }
            _socket.Dispose();
        }
        if (_receiveTask != null)
        {
            try { await _receiveTask.ConfigureAwait(false); } catch { }
        }
        _receiveCts?.Dispose();
    }

    #region Logging
    [LoggerMessage(EventId = 801, Level = LogLevel.Information, Message = "[WSClient] Connected to {Url}")]
    private static partial void LogConnected(ILogger logger, string url);

    [LoggerMessage(EventId = 802, Level = LogLevel.Error, Message = "[WSClient] Receive loop error.")]
    private static partial void LogReceiveError(ILogger logger, Exception ex);

    [LoggerMessage(EventId = 803, Level = LogLevel.Warning, Message = "[WSClient] Cancel send error.")]
    private static partial void LogCancelError(ILogger logger, Exception ex);
    #endregion
}
