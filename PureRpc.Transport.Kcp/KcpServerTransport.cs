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
    private const int MaxServiceNameLength = 256;
    private const int MaxMethodNameLength = 256;
    private const int MaxHeaderCount = 64;
    private const int MaxHeaderKeyLength = 256;
    private const int MaxHeaderValueLength = 4096;

    private readonly KcpServerOptions _options;
    private readonly ILogger<KcpServerTransport> _logger;
    private readonly ObjectPool<RpcContext> _contextPool;


    private KcpServer? _server;
    private CancellationTokenSource? _tickCts;
    private Task? _tickTask;
    private bool _disposed;

    private Func<RpcContext, ReadOnlySequence<byte>, Task>? _onRequest;
    private readonly ConcurrentQueue<(int ConnectionId, byte[] Data)> _sendQueue = new();
    private readonly ConcurrentDictionary<(long ConnectionId, uint RequestId), CancellationTokenSource> _activeRequests = new(Environment.ProcessorCount, 1024);

    public KcpServerTransport(IOptions<KcpServerOptions> options, ILogger<KcpServerTransport>? logger = null)
    {
        _options = options.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? NullLogger<KcpServerTransport>.Instance;
        var provider = new DefaultObjectPoolProvider { MaximumRetained = 1024 };
        _contextPool = provider.Create(new RpcContextPolicy());
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

        int headerCount = context.Headers.Count;
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
            foreach (var kv in context.Headers)
            {
                keys[i] = kv.Key;
                values[i] = kv.Value;
                keySizes[i] = Encoding.UTF8.GetByteCount(kv.Key);
                valSizes[i] = Encoding.UTF8.GetByteCount(kv.Value);
                headersBlockSize += 4 + keySizes[i] + 4 + valSizes[i];
                i++;
            }
        }

        int bodyLen = 1 + 4 + 4 + 4 + headersBlockSize + data.Length;
        var buffer = new byte[bodyLen];

        buffer[0] = (byte)(context.IsAborted ? 3 : 2); // Response=2, Error=3
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(1, 4), context.RequestId);
        BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(5, 4), context.IsAborted ? 500 : 200);

        int pos = 9;
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
            data.Span.CopyTo(buffer.AsSpan(pos));
        }

        int connectionId = (int)context.ConnectionId;
        _sendQueue.Enqueue((connectionId, buffer));
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

            // Cancel 帧：取消正在处理的请求，然后跳过
            if (type == 8)
            {
                var key = (connectionId, requestId);
                if (_activeRequests.TryRemove(key, out var cts))
                {
                    cts.Cancel();
                    cts.Dispose();
                }
                return;
            }

            if (type != 1) return; // only handle Request

            int svcLen = BinaryPrimitives.ReadInt32LittleEndian(span.Slice(5, 4));
            if (svcLen <= 0 || svcLen > MaxServiceNameLength) return;
            int offset = 9;
            string serviceName = Encoding.UTF8.GetString(span.Slice(offset, svcLen));
            offset += svcLen;

            int metLen = BinaryPrimitives.ReadInt32LittleEndian(span.Slice(offset, 4));
            if (metLen <= 0 || metLen > MaxMethodNameLength) return;
            offset += 4;
            string methodName = Encoding.UTF8.GetString(span.Slice(offset, metLen));
            offset += metLen;

            int headerCount = BinaryPrimitives.ReadInt32LittleEndian(span.Slice(offset, 4));
            if (headerCount < 0 || headerCount > MaxHeaderCount) return;
            offset += 4;
            var headers = new Dictionary<string, string>();
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
                headers[key] = val;
            }

            var payload = new ReadOnlySequence<byte>(span.Slice(offset).ToArray());
            var context = _contextPool.Get();
            context.ConnectionId = connectionId;
            context.RequestId = requestId;
            context.ServiceName = serviceName;
            context.MethodName = methodName;
            foreach (var kv in headers)
            {
                context.Headers[kv.Key] = kv.Value;
            }

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
                // 本 tick 已完成的请求响应在此发送（入 KCP 发送缓冲）
                while (_sendQueue.TryDequeue(out var item))
                {
                    _server?.Send(item.ConnectionId, new ArraySegment<byte>(item.Data), KcpChannel.Reliable);
                }
                _server?.TickOutgoing();
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
