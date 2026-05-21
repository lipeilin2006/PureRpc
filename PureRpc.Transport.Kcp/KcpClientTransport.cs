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
    private readonly KcpClientOptions _options;
    private readonly ILogger<KcpClientTransport> _logger;

    private readonly ConcurrentQueue<(byte[] Data, int Length)> _sendQueue = new();
    private KcpClient? _client;
    private CancellationTokenSource? _tickCts;
    private Task? _tickTask;
    private bool _disposed;
    private bool _isConnected;
    private TaskCompletionSource? _connectedTcs;
    private Action<uint, ReadOnlySequence<byte>, bool, string?, IReadOnlyDictionary<string, string>?>? _onResponse;
    private readonly SemaphoreSlim _tickSignal = new(0);

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

    private void OnDataReceived(ArraySegment<byte> message, KcpChannel channel)
    {
        if (message.Count < 10) return;
        if (_onResponse == null) return;

        var span = message.AsSpan();
        byte type = span[0];
        uint requestId = BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(1, 4));
        int statusCode = BinaryPrimitives.ReadInt32LittleEndian(span.Slice(5, 4));
        int headerCount = BinaryPrimitives.ReadInt32LittleEndian(span.Slice(9, 4));
        if (headerCount < 0 || headerCount > RpcProtocolConstants.MaxHeaderCount) return;

        int offset = 13;
        IReadOnlyDictionary<string, string>? headers = null;
        if (headerCount > 0)
        {
            if (!RpcFrameCodec.TryParseHeadersSpan(span, ref offset, headerCount, out var dict)) return;
            headers = dict;
        }

        bool isSuccess = type != (byte)RpcMessageType.Error && statusCode == 200;
        var payload = new ReadOnlySequence<byte>(message.Array!, message.Offset + offset, message.Count - offset);
        _onResponse(requestId, payload, isSuccess, isSuccess ? null : Encoding.UTF8.GetString(span.Slice(offset)), headers);
    }

    public ValueTask SendAsync(
        uint requestId, string serviceName, string methodName,
        ReadOnlySequence<byte> data, CancellationToken ct, IDictionary<string, string>? headers = null)
    {
        if (_client == null || !_client.connected)
            throw new IOException("KCP client is not connected.");

        int svcByteCount = Encoding.UTF8.GetByteCount(serviceName);
        int metByteCount = Encoding.UTF8.GetByteCount(methodName);
        var headerInfo = RpcFrameCodec.PrepareHeaders(headers as IReadOnlyDictionary<string, string>);

        int bodyLen = 1 + 4 + 4 + svcByteCount + 4 + metByteCount + 4 + headerInfo.HeadersBlockSize + (int)data.Length;
        byte[] rented = ArrayPool<byte>.Shared.Rent(bodyLen);

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

        _sendQueue.Enqueue((rented, bodyLen));
        _tickSignal.Release();
        return default;
    }

    public ValueTask CancelRequestAsync(uint requestId, CancellationToken ct = default)
    {
        if (_client == null || !_client.connected) return default;

        byte[] rented = ArrayPool<byte>.Shared.Rent(5);
        RpcFrameCodec.WriteCancelSpan(rented, requestId);
        _sendQueue.Enqueue((rented, 5));
        _tickSignal.Release();
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
                try
                {
                    await _tickSignal.WaitAsync(_options.TickInterval, ct).ConfigureAwait(false);
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

    private void DrainSendQueue()
    {
        while (_sendQueue.TryDequeue(out var item))
        {
            _client?.Send(new ArraySegment<byte>(item.Data, 0, item.Length), KcpChannel.Reliable);
            ArrayPool<byte>.Shared.Return(item.Data);
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
