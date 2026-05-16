using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.ObjectPool;
using Microsoft.Extensions.Options;
using PureRpc.Abstractions;

namespace PureRpc.Transport.Websocket;

internal sealed partial class WebSocketServerTransport : IServerTransport
{
    private readonly WebSocketServerOptions _options;
    private readonly ILogger<WebSocketServerTransport> _logger;
    private readonly ObjectPool<RpcContext> _contextPool;

    private readonly SemaphoreSlim _requestThrottle = new(512, 512);
    private HttpListener? _listener;
    private CancellationTokenSource? _serverCts;
    private Task? _acceptTask;
    private bool _disposed;
    private long _nextConnId;

    private Func<RpcContext, ReadOnlySequence<byte>, Task>? _onRequest;

    public WebSocketServerTransport(IOptions<WebSocketServerOptions> options, ILogger<WebSocketServerTransport>? logger = null)
    {
        _options = options.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? NullLogger<WebSocketServerTransport>.Instance;
        var provider = new DefaultObjectPoolProvider { MaximumRetained = 1024 };
        _contextPool = provider.Create(new RpcContextPolicy());
    }

    public Task StartAsync(Func<RpcContext, ReadOnlySequence<byte>, Task> onRequestReceived, CancellationToken ct)
    {
        _onRequest = onRequestReceived ?? throw new ArgumentNullException(nameof(onRequestReceived));
        _serverCts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        var ep = (IPEndPoint)_options.EndPoint;
        // HttpListener 在 Linux 上不支持 IPAddress.Any (0.0.0.0) 作为前缀，
        // 需要使用 "+" (所有接口) 或 "*" (通配符)
        var addrStr = ep.Address.Equals(IPAddress.Any) ? "+" : ep.Address.ToString();
        _listener = new HttpListener();
        _listener.Prefixes.Add($"http://{addrStr}:{ep.Port}{_options.Path}/");
        _listener.Start();
        LogListening(_logger, $"{ep.Address}:{ep.Port}{_options.Path}");

        _acceptTask = AcceptLoopAsync(_serverCts.Token);
        return Task.CompletedTask;
    }

    private async Task AcceptLoopAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var ctx = await _listener!.GetContextAsync().WaitAsync(ct).ConfigureAwait(false);
                if (ctx.Request.IsWebSocketRequest)
                {
                    var wsCtx = await ctx.AcceptWebSocketAsync(null).ConfigureAwait(false);
                    _ = HandleClientAsync(wsCtx.WebSocket, ct);
                }
                else
                {
                    ctx.Response.StatusCode = 400;
                    ctx.Response.Close();
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (!_disposed)
        {
            LogAcceptError(_logger, ex);
        }
    }

    private async Task HandleClientAsync(WebSocket socket, CancellationToken ct)
    {
        var connId = Interlocked.Increment(ref _nextConnId);
        var recvBuffer = new byte[8192];
        var messageBuffer = new MemoryStream();
        try
        {
            while (!ct.IsCancellationRequested && socket.State == WebSocketState.Open)
            {
                WebSocketReceiveResult result;
                messageBuffer.SetLength(0);
                do
                {
                    result = await socket.ReceiveAsync(new ArraySegment<byte>(recvBuffer), ct).ConfigureAwait(false);
                    messageBuffer.Write(recvBuffer, 0, result.Count);
                }
                while (!result.EndOfMessage);

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None);
                    break;
                }

                ProcessRequest(socket, messageBuffer.ToArray(), connId);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            LogConnectionError(_logger, ex);
        }
    }

    private void ProcessRequest(WebSocket socket, byte[] message, long connId)
    {
        if (message.Length < 9 || _onRequest == null) return;
        var span = message.AsSpan();
        byte type = span[0];
        uint requestId = BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(1, 4));

        if (type == 8) return;

        int svcLen = BinaryPrimitives.ReadInt32LittleEndian(span.Slice(5, 4));
        if (svcLen <= 0 || svcLen > 256) return;
        int offset = 9;
        string serviceName = Encoding.UTF8.GetString(span.Slice(offset, svcLen));
        offset += svcLen;
        int metLen = BinaryPrimitives.ReadInt32LittleEndian(span.Slice(offset, 4));
        if (metLen <= 0 || metLen > 256) return;
        offset += 4;
        string methodName = Encoding.UTF8.GetString(span.Slice(offset, metLen));
        offset += metLen;

        int headerCount = BinaryPrimitives.ReadInt32LittleEndian(span.Slice(offset, 4));
        if (headerCount < 0 || headerCount > 64) return;
        offset += 4;
        var headers = new Dictionary<string, string>();
        for (int i = 0; i < headerCount; i++)
        {
            int keyLen = BinaryPrimitives.ReadInt32LittleEndian(span.Slice(offset, 4));
            if (keyLen <= 0 || keyLen > 256) return;
            offset += 4;
            string key = Encoding.UTF8.GetString(span.Slice(offset, keyLen));
            offset += keyLen;
            int valLen = BinaryPrimitives.ReadInt32LittleEndian(span.Slice(offset, 4));
            if (valLen <= 0 || valLen > 4096) return;
            offset += 4;
            string val = Encoding.UTF8.GetString(span.Slice(offset, valLen));
            offset += valLen;
            headers[key] = val;
        }

        var payload = new ReadOnlySequence<byte>(span.Slice(offset).ToArray());
        var context = _contextPool.Get();
        context.ConnectionId = connId;
        context.RequestId = requestId;
        context.ServiceName = serviceName;
        context.MethodName = methodName;
        foreach (var kv in headers)
            context.Headers[kv.Key] = kv.Value;

        _ = ProcessRequestAsync(socket, context, payload);
    }

    private async Task ProcessRequestAsync(WebSocket socket, RpcContext context, ReadOnlySequence<byte> payload)
    {
        await _requestThrottle.WaitAsync().ConfigureAwait(false);
        try
        {
            await _onRequest!(context, payload).ConfigureAwait(false);

            var responseData = ((ArrayBufferWriter<byte>)context.ResponseBuffer).WrittenMemory;
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

            int bodyLen = 1 + 4 + 4 + 4 + headersBlockSize + responseData.Length;
            var buffer = new byte[bodyLen];
            buffer[0] = (byte)(context.IsAborted ? 3 : 2);
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
            if (responseData.Length > 0)
                responseData.Span.CopyTo(buffer.AsSpan(pos));

            await socket.SendAsync(new ArraySegment<byte>(buffer), WebSocketMessageType.Binary, true, CancellationToken.None).ConfigureAwait(false);
        }
        catch { }
        finally
        {
            _requestThrottle.Release();
            _contextPool.Return(context);
        }
    }

    // WebSocket 传输的响应在 ProcessRequestAsync 中内联发送，
    // 此处不需要额外操作（RpcServer.HandleRequestAsync 调用此方法后返回，
    // ProcessRequestAsync 在 _onRequest 返回后继续发送响应体）
    public ValueTask SendResponseAsync(RpcContext context, CancellationToken ct) => default;

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        _serverCts?.Cancel();
        _listener?.Stop();
        _listener?.Close();
        if (_acceptTask != null)
        {
            try { await _acceptTask.ConfigureAwait(false); } catch { }
        }
        _serverCts?.Dispose();
    }

    #region Logging
    [LoggerMessage(EventId = 901, Level = LogLevel.Information, Message = "[WSServer] Listening on {EndPoint}")]
    private static partial void LogListening(ILogger logger, string endPoint);

    [LoggerMessage(EventId = 902, Level = LogLevel.Error, Message = "[WSServer] Accept loop error.")]
    private static partial void LogAcceptError(ILogger logger, Exception ex);

    [LoggerMessage(EventId = 903, Level = LogLevel.Error, Message = "[WSServer] Connection error.")]
    private static partial void LogConnectionError(ILogger logger, Exception ex);
    #endregion
}
