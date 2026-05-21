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
    private readonly ConcurrentDictionary<uint, CancellationTokenSource> _activeRequests = new();

    private readonly SemaphoreSlim _requestThrottle = new(RpcProtocolConstants.DefaultRequestThrottleLimit, RpcProtocolConstants.DefaultRequestThrottleLimit);
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
        _contextPool = RpcContextPolicy.CreatePool();
    }

    public Task StartAsync(Func<RpcContext, ReadOnlySequence<byte>, Task> onRequestReceived, CancellationToken ct)
    {
        _onRequest = onRequestReceived ?? throw new ArgumentNullException(nameof(onRequestReceived));
        _serverCts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        var ep = (IPEndPoint)_options.EndPoint;
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
        if (message.Length < 5 || _onRequest == null) return;
        var span = message.AsSpan();
        byte type = span[0];
        uint requestId = BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(1, 4));

        if (type == (byte)RpcMessageType.Cancel)
        {
            if (_activeRequests.TryRemove(requestId, out var cts))
            {
                cts.Cancel();
                cts.Dispose();
            }
            return;
        }

        if (type != (byte)RpcMessageType.Request || message.Length < 9) return;

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

        var payload = new ReadOnlySequence<byte>(message, offset, message.Length - offset);
        var context = _contextPool.Get();
        context.PopulateRequest(connId, requestId, serviceName, methodName, null, headers as IReadOnlyDictionary<string, string>);

        var requestCts = CancellationTokenSource.CreateLinkedTokenSource(_serverCts?.Token ?? default);
        _activeRequests[requestId] = requestCts;
        context.CancellationToken = requestCts.Token;

        _ = ProcessRequestAsync(socket, context, payload, requestCts);
    }

    private async Task ProcessRequestAsync(WebSocket socket, RpcContext context, ReadOnlySequence<byte> payload, CancellationTokenSource requestCts)
    {
        await _requestThrottle.WaitAsync().ConfigureAwait(false);
        try
        {
            await _onRequest!(context, payload).ConfigureAwait(false);

            var responseData = ((ArrayBufferWriter<byte>)context.ResponseBuffer).WrittenMemory;
            var headerInfo = RpcFrameCodec.PrepareHeaders(context.HeadersOrNull);

            int bodyLen = 1 + 4 + 4 + 4 + headerInfo.HeadersBlockSize + responseData.Length;
            byte[] rented = ArrayPool<byte>.Shared.Rent(bodyLen);
            try
            {
                int written = RpcFrameCodec.WriteResponseSpan(rented, context.RequestId, context.IsAborted, in headerInfo, responseData.Length);
                if (responseData.Length > 0)
                    responseData.Span.CopyTo(rented.AsSpan(written));

                await socket.SendAsync(new ArraySegment<byte>(rented, 0, bodyLen), WebSocketMessageType.Binary, true, CancellationToken.None).ConfigureAwait(false);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(rented);
            }
        }
        catch { }
        finally
        {
            _requestThrottle.Release();
            _contextPool.Return(context);
            _activeRequests.TryRemove(context.RequestId, out _);
            requestCts.Dispose();
        }
    }

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
