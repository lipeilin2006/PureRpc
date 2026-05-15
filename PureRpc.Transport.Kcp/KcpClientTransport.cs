using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PureRpc.Abstractions;

namespace PureRpc.Transport.Kcp;

internal sealed partial class KcpClientTransport : IClientTransport
{
    private const int MaxHeaderCount = 64;
    private const int MaxHeaderKeyLength = 256;
    private const int MaxHeaderValueLength = 4096;

    private readonly KcpClientOptions _options;
    private readonly ILogger<KcpClientTransport> _logger;

    // 所有 KCP 操作仅在 tick 线程执行，通过队列传递数据
    private readonly ConcurrentQueue<byte[]> _sendQueue = new();
    private KcpClient? _client;
    private CancellationTokenSource? _tickCts;
    private Task? _tickTask;
    private bool _disposed;
    private bool _isConnected;
    private TaskCompletionSource? _connectedTcs;
    private Action<uint, ReadOnlySequence<byte>, bool, string?, IReadOnlyDictionary<string, string>?>? _onResponse;

    public bool IsConnected => _isConnected && _client is { connected: true };

    public KcpClientTransport(IOptions<KcpClientOptions> options, ILogger<KcpClientTransport>? logger = null)
    {
        _options = options.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? NullLogger<KcpClientTransport>.Instance;
    }

    public async Task ConnectAsync(
        Action<uint, ReadOnlySequence<byte>, bool, string?, IReadOnlyDictionary<string, string>?> onResponseReceived,
        CancellationToken ct)
    {
        if (_isConnected) return;

        _onResponse = onResponseReceived ?? throw new ArgumentNullException(nameof(onResponseReceived));
        _connectedTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        _client = new KcpClient(
            OnConnected: OnConnected,
            OnData: OnDataReceived,
            OnDisconnected: () => LogDisconnected(_logger),
            OnError: (code, msg) => LogError(_logger, code, msg),
            _options.Config);

        _client.Connect(_options.Host, _options.Port);

        _tickCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _tickTask = RunTickLoopAsync(_tickCts.Token);

        LogConnecting(_logger, $"{_options.Host}:{_options.Port}");

        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);
        try
        {
            await _connectedTcs.Task.WaitAsync(linkedCts.Token).ConfigureAwait(false);
            _isConnected = true;
        }
        catch
        {
            _client?.Disconnect();
            throw new TimeoutException("KCP connection handshake failed.");
        }
    }

    private void OnConnected()
    {
        LogConnected(_logger);
        _connectedTcs?.TrySetResult();
    }

    // 仅供 tick 线程调用
    private void OnDataReceived(ArraySegment<byte> message, KcpChannel channel)
    {
        if (message.Count < 10) return;
        if (_onResponse == null) return;

        var span = message.AsSpan();
        byte type = span[0];
        uint requestId = BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(1, 4));
        int statusCode = BinaryPrimitives.ReadInt32LittleEndian(span.Slice(5, 4));
        int headerCount = BinaryPrimitives.ReadInt32LittleEndian(span.Slice(9, 4));
        if (headerCount < 0 || headerCount > MaxHeaderCount) return;

        int offset = 13;
        IReadOnlyDictionary<string, string>? headers = null;
        if (headerCount > 0)
        {
            var dict = new Dictionary<string, string>(headerCount);
            for (int i = 0; i < headerCount; i++)
            {
                int keyLen = BinaryPrimitives.ReadInt32LittleEndian(span.Slice(offset, 4));
                if (keyLen <= 0 || keyLen > MaxHeaderKeyLength || offset + 4 + keyLen > message.Count) return;
                offset += 4;
                string key = Encoding.UTF8.GetString(span.Slice(offset, keyLen));
                offset += keyLen;
                int valLen = BinaryPrimitives.ReadInt32LittleEndian(span.Slice(offset, 4));
                if (valLen <= 0 || valLen > MaxHeaderValueLength || offset + 4 + valLen > message.Count) return;
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

    // 仅序列化，不入库 KCP（由 tick 线程实际发送）
    public ValueTask SendAsync(
        uint requestId, string serviceName, string methodName,
        ReadOnlySequence<byte> data, CancellationToken ct, IDictionary<string, string>? headers = null)
    {
        if (_client == null || !_client.connected)
            throw new IOException("KCP client is not connected.");

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

        _sendQueue.Enqueue(buffer);
        return default;
    }

    public ValueTask CancelRequestAsync(uint requestId, CancellationToken ct = default)
    {
        if (_client == null || !_client.connected) return default;

        var buffer = new byte[5];
        buffer[0] = 8;
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(1, 4), requestId);
        _sendQueue.Enqueue(buffer);
        return default;
    }

    private async Task RunTickLoopAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                _client?.Tick();
                DrainSendQueue();
                // 有数据时低延迟轮询，空闲时按配置间隔休眠
                try
                {
                    await Task.Delay(_sendQueue.IsEmpty ? _options.TickInterval : 1, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException) { break; }
            }
        }
        catch (Exception ex) when (!_disposed)
        {
            LogTickError(_logger, ex);
        }
        finally
        {
            _isConnected = false;
        }
    }

    // tick 线程独占，无需锁
    private void DrainSendQueue()
    {
        while (_sendQueue.TryDequeue(out var data))
        {
            _client?.Send(new ArraySegment<byte>(data), KcpChannel.Reliable);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        _isConnected = false;

        _tickCts?.Cancel();
        _client?.Disconnect();

        if (_tickTask != null)
        {
            try { await _tickTask.ConfigureAwait(false); } catch { }
        }

        _tickCts?.Dispose();
    }

    #region Logging
    [LoggerMessage(EventId = 601, Level = LogLevel.Information, Message = "[KcpClient] Connecting to {Target}")]
    private static partial void LogConnecting(ILogger logger, string target);

    [LoggerMessage(EventId = 602, Level = LogLevel.Information, Message = "[KcpClient] Connected.")]
    private static partial void LogConnected(ILogger logger);

    [LoggerMessage(EventId = 603, Level = LogLevel.Warning, Message = "[KcpClient] Disconnected.")]
    private static partial void LogDisconnected(ILogger logger);

    [LoggerMessage(EventId = 604, Level = LogLevel.Error, Message = "[KcpClient] Tick loop error.")]
    private static partial void LogTickError(ILogger logger, Exception ex);

    [LoggerMessage(EventId = 605, Level = LogLevel.Error, Message = "[KcpClient] Error: {ErrorCode} - {Message}")]
    private static partial void LogError(ILogger logger, ErrorCode errorCode, string message);
    #endregion
}
