using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.ObjectPool;
using Microsoft.Extensions.Options;
using PureRpc.Abstractions;

namespace PureRpc.Transport.Kcp;

internal sealed partial class KcpServerTransport : IServerTransport
{
    private readonly KcpServerOptions _options;
    private readonly ILogger<KcpServerTransport> _logger;
    private readonly ObjectPool<RpcContext> _contextPool;

    private KcpServer? _server;
    private CancellationTokenSource? _tickCts;
    private Task? _tickTask;
    private bool _disposed;

    private Func<RpcContext, ReadOnlySequence<byte>, Task>? _onRequest;
    private readonly ConcurrentQueue<(int ConnectionId, byte[] Data, int Length)> _sendQueue = new();
    private readonly ConcurrentDictionary<(long ConnectionId, uint RequestId), CancellationTokenSource> _activeRequests = new(Environment.ProcessorCount, 1024);
    private readonly SemaphoreSlim _tickSignal = new(0);

    public KcpServerTransport(IOptions<KcpServerOptions> options, ILogger<KcpServerTransport>? logger = null)
    {
        _options = options.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? NullLogger<KcpServerTransport>.Instance;
        _contextPool = RpcContextPolicy.CreatePool();
    }

    public Task StartAsync(Func<RpcContext, ReadOnlySequence<byte>, Task> onRequestReceived, CancellationToken ct)
    {
        _onRequest = onRequestReceived ?? throw new ArgumentNullException(nameof(onRequestReceived));

        _server = new KcpServer(
            OnConnected: (connId) => LogConnected(_logger, connId),
            OnData: OnDataReceived,
            OnDisconnected: (connId) => LogDisconnected(_logger, connId),
            OnError: (connId, code, msg) => LogError(_logger, connId, code, msg),
            _options.Config);

        _server.Start(_options.Port);

        _tickCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _tickTask = RunTickLoopAsync(_tickCts.Token);

        LogListening(_logger, _options.Port);
        return Task.CompletedTask;
    }

    public ValueTask SendResponseAsync(RpcContext context, CancellationToken ct)
    {
        if (_server == null) return default;

        var data = ((ArrayBufferWriter<byte>)context.ResponseBuffer).WrittenMemory;
        var headerInfo = RpcFrameCodec.PrepareHeaders(context.HeadersOrNull);

        int bodyLen = 1 + 4 + 4 + 4 + headerInfo.HeadersBlockSize + data.Length;
        byte[] rented = ArrayPool<byte>.Shared.Rent(bodyLen);

        int written = RpcFrameCodec.WriteResponseSpan(rented, context.RequestId, context.IsAborted, in headerInfo, data.Length);

        if (data.Length > 0)
        {
            data.Span.CopyTo(rented.AsSpan(written));
        }

        int connectionId = (int)context.ConnectionId;
        _sendQueue.Enqueue((connectionId, rented, bodyLen));
        _tickSignal.Release();
        _contextPool.Return(context);
        return default;
    }

    private void OnDataReceived(int connectionId, ArraySegment<byte> message, KcpChannel channel)
    {
        try
        {
            if (message.Count < 9) return;
            if (_onRequest == null) return;

            var span = message.AsSpan();
            byte type = span[0];
            uint requestId = BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(1, 4));

            if (type == (byte)RpcMessageType.Cancel)
            {
                var key = ((long)connectionId, requestId);
                if (_activeRequests.TryRemove(key, out var cts))
                {
                    cts.Cancel();
                    cts.Dispose();
                }
                return;
            }

            if (type != (byte)RpcMessageType.Request) return;

            int svcLen = BinaryPrimitives.ReadInt32LittleEndian(span.Slice(5, 4));
            if (svcLen <= 0 || svcLen > RpcProtocolConstants.MaxServiceNameLength) return;
            int offset = 9;
            string serviceName = Encoding.UTF8.GetString(span.Slice(offset, svcLen));
            offset += svcLen;

            int metLen = BinaryPrimitives.ReadInt32LittleEndian(span.Slice(offset, 4));
            if (metLen <= 0 || metLen > RpcProtocolConstants.MaxMethodNameLength) return;
            offset += 4;
            string methodName = Encoding.UTF8.GetString(span.Slice(offset, metLen));
            offset += metLen;

            int headerCount = BinaryPrimitives.ReadInt32LittleEndian(span.Slice(offset, 4));
            offset += 4;
            Dictionary<string, string>? headers;
            if (!RpcFrameCodec.TryParseHeadersSpan(span, ref offset, headerCount, out headers)) return;

            var payload = new ReadOnlySequence<byte>(message.Array!, message.Offset + offset, message.Count - offset);
            var context = _contextPool.Get();
            context.PopulateRequest(connectionId, requestId, serviceName, methodName, null, headers as IReadOnlyDictionary<string, string>);

            var requestCts = CancellationTokenSource.CreateLinkedTokenSource(_tickCts?.Token ?? CancellationToken.None);
            var key2 = ((long)connectionId, requestId);
            _activeRequests[key2] = requestCts;
            context.CancellationToken = requestCts.Token;

            _ = ProcessRequestAsync(_onRequest, context, payload, requestCts, key2);
        }
        catch (Exception ex)
        {
            LogDispatchError(_logger, ex, connectionId);
        }
    }

    private async Task ProcessRequestAsync(
        Func<RpcContext, ReadOnlySequence<byte>, Task> handler, RpcContext context,
        ReadOnlySequence<byte> payload, CancellationTokenSource requestCts,
        (long ConnectionId, uint RequestId) key)
    {
        try
        {
            await handler(context, payload).ConfigureAwait(false);
        }
        finally
        {
            if (_activeRequests.TryRemove(key, out var removed))
                removed.Dispose();
        }
    }

    private async Task RunTickLoopAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                _server?.TickIncoming();
                while (_sendQueue.TryDequeue(out var item))
                {
                    _server?.Send(item.ConnectionId, new ArraySegment<byte>(item.Data, 0, item.Length), KcpChannel.Reliable);
                    ArrayPool<byte>.Shared.Return(item.Data);
                }
                _server?.TickOutgoing();
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
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        _tickCts?.Cancel();
        _server?.Stop();

        if (_tickTask != null)
        {
            try { await _tickTask.ConfigureAwait(false); } catch { }
        }

        _tickCts?.Dispose();
    }

    #region Logging
    [LoggerMessage(EventId = 701, Level = LogLevel.Information, Message = "[KcpServer] Listening on port {Port}")]
    private static partial void LogListening(ILogger logger, ushort port);

    [LoggerMessage(EventId = 702, Level = LogLevel.Information, Message = "[KcpServer] Connection {ConnId} connected.")]
    private static partial void LogConnected(ILogger logger, int connId);

    [LoggerMessage(EventId = 703, Level = LogLevel.Information, Message = "[KcpServer] Connection {ConnId} disconnected.")]
    private static partial void LogDisconnected(ILogger logger, int connId);

    [LoggerMessage(EventId = 704, Level = LogLevel.Error, Message = "[KcpServer] Tick loop error.")]
    private static partial void LogTickError(ILogger logger, Exception ex);

    [LoggerMessage(EventId = 705, Level = LogLevel.Error, Message = "[KcpServer] Connection {ConnId} error: {ErrorCode} - {Message}")]
    private static partial void LogError(ILogger logger, int connId, ErrorCode errorCode, string message);

    [LoggerMessage(EventId = 706, Level = LogLevel.Error, Message = "[KcpServer] Dispatch error for connection {ConnId}.")]
    private static partial void LogDispatchError(ILogger logger, Exception ex, int connId);
    #endregion
}
