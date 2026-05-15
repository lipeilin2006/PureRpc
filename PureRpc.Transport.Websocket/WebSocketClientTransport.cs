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

        var frame = BuildFrame(requestId, serviceName, methodName, data, headers);
        await _socket.SendAsync(new ArraySegment<byte>(frame), WebSocketMessageType.Binary, true, ct).ConfigureAwait(false);
    }

    private static byte[] BuildFrame(uint requestId, string serviceName, string methodName,
        ReadOnlySequence<byte> data, IDictionary<string, string>? headers)
    {
        int svcByteCount = Encoding.UTF8.GetByteCount(serviceName);
        int metByteCount = Encoding.UTF8.GetByteCount(methodName);
        int headerCount = headers?.Count ?? 0;
        int headersBlockSize = 0;
        string[]? keys = null, values = null;
        int[]? keySizes = null, valSizes = null;
        if (headerCount > 0)
        {
            keys = new string[headerCount];
            values = new string[headerCount];
            keySizes = new int[headerCount];
            valSizes = new int[headerCount];
            int i = 0;
            foreach (var kv in headers!)
            {
                keys[i] = kv.Key;
                values[i] = kv.Value;
                keySizes[i] = Encoding.UTF8.GetByteCount(kv.Key);
                valSizes[i] = Encoding.UTF8.GetByteCount(kv.Value);
                headersBlockSize += 4 + keySizes[i] + 4 + valSizes[i];
                i++;
            }
        }

        int bodyLen = 1 + 4 + 4 + svcByteCount + 4 + metByteCount + 4 + headersBlockSize + (int)data.Length;
        var buffer = new byte[bodyLen];
        buffer[0] = 1;
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(1, 4), requestId);
        BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(5, 4), svcByteCount);
        int pos = 9;
        Encoding.UTF8.GetBytes(serviceName, buffer.AsSpan(pos, svcByteCount));
        pos += svcByteCount;
        BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(pos, 4), metByteCount);
        pos += 4;
        Encoding.UTF8.GetBytes(methodName, buffer.AsSpan(pos, metByteCount));
        pos += metByteCount;
        BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(pos, 4), headerCount);
        pos += 4;
        for (int i = 0; i < headerCount; i++)
        {
            BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(pos, 4), keySizes![i]);
            pos += 4;
            Encoding.UTF8.GetBytes(keys![i], buffer.AsSpan(pos, keySizes[i]));
            pos += keySizes[i];
            BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(pos, 4), valSizes![i]);
            pos += 4;
            Encoding.UTF8.GetBytes(values![i], buffer.AsSpan(pos, valSizes[i]));
            pos += valSizes[i];
        }
        if (data.Length > 0)
        {
            if (data.IsSingleSegment)
                data.FirstSpan.CopyTo(buffer.AsSpan(pos));
            else
                foreach (var seg in data)
                {
                    seg.Span.CopyTo(buffer.AsSpan(pos));
                    pos += seg.Length;
                }
        }
        return buffer;
    }

    public async ValueTask CancelRequestAsync(uint requestId, CancellationToken ct = default)
    {
        if (_socket?.State != WebSocketState.Open) return;
        var buffer = new byte[5];
        buffer[0] = 8;
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(1, 4), requestId);
        try
        {
            await _socket.SendAsync(new ArraySegment<byte>(buffer), WebSocketMessageType.Binary, true, ct).ConfigureAwait(false);
        }
        catch (Exception ex) { LogCancelError(_logger, ex); }
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
        if (message.Length < 10 || _onResponse == null) return;
        var span = message.AsSpan();
        byte type = span[0];
        uint requestId = BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(1, 4));
        int statusCode = BinaryPrimitives.ReadInt32LittleEndian(span.Slice(5, 4));
        int headerCount = BinaryPrimitives.ReadInt32LittleEndian(span.Slice(9, 4));
        int offset = 13;
        IReadOnlyDictionary<string, string>? headers = null;
        if (headerCount > 0)
        {
            var dict = new Dictionary<string, string>(headerCount);
            for (int i = 0; i < headerCount; i++)
            {
                int keyLen = BinaryPrimitives.ReadInt32LittleEndian(span.Slice(offset, 4));
                offset += 4;
                string key = Encoding.UTF8.GetString(span.Slice(offset, keyLen));
                offset += keyLen;
                int valLen = BinaryPrimitives.ReadInt32LittleEndian(span.Slice(offset, 4));
                offset += 4;
                string val = Encoding.UTF8.GetString(span.Slice(offset, valLen));
                offset += valLen;
                dict[key] = val;
            }
            headers = dict;
        }
        bool isSuccess = type != 3 && statusCode == 200;
        var payload = new ReadOnlySequence<byte>(span.Slice(offset).ToArray());
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
