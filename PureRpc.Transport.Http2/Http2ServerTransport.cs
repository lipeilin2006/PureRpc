using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.ObjectPool;
using Microsoft.Extensions.Options;
using PureRpc.Abstractions;

namespace PureRpc.Transport.Http2;

internal sealed partial class Http2ServerTransport : IServerTransport
{
    private readonly Http2ServerOptions _options;
    private readonly ILogger<Http2ServerTransport> _logger;
    private readonly ObjectPool<RpcContext> _contextPool;
    private readonly ConcurrentDictionary<uint, CancellationTokenSource> _activeRequests = new();

    private WebApplication? _app;
    private CancellationTokenSource? _serverCts;
    private Task? _serverTask;
    private bool _disposed;
    private Func<RpcContext, ReadOnlySequence<byte>, Task>? _onRequest;

    public Http2ServerTransport(IOptions<Http2ServerOptions> options, ILogger<Http2ServerTransport>? logger = null)
    {
        _options = options.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? NullLogger<Http2ServerTransport>.Instance;
        _contextPool = RpcContextPolicy.CreatePool();
    }

    public Task StartAsync(Func<RpcContext, ReadOnlySequence<byte>, Task> onRequestReceived, CancellationToken ct)
    {
        _onRequest = onRequestReceived ?? throw new ArgumentNullException(nameof(onRequestReceived));
        _serverCts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseKestrel(kestrel =>
        {
            kestrel.Listen(System.Net.IPAddress.Any, _options.Port, listen =>
            {
                listen.Protocols = Microsoft.AspNetCore.Server.Kestrel.Core.HttpProtocols.Http2;
                if (_options.TlsCertPath != null && _options.TlsKeyPath != null)
                {
                    listen.UseHttps(_options.TlsCertPath, _options.TlsKeyPath);
                }
            });
        });

        _app = builder.Build();
        _app.MapPost("/rpc", HandleRequestAsync);

        _serverTask = _app.StartAsync(_serverCts.Token);
        LogListening(_logger, _options.Port);
        return Task.CompletedTask;
    }

    private async Task HandleRequestAsync(HttpContext context)
    {
        try
        {
            var requestBytes = await ReadBodyAsync(context.Request.Body).ConfigureAwait(false);
            if (requestBytes.Length < 5 || _onRequest == null)
            { context.Response.StatusCode = 400; return; }

            var span = requestBytes.AsSpan();
            byte type = span[0];

            if (type == (byte)RpcMessageType.Cancel)
            {
                uint cancelId = BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(1, 4));
                if (_activeRequests.TryRemove(cancelId, out var cts))
                {
                    cts.Cancel();
                    cts.Dispose();
                }
                context.Response.StatusCode = 200;
                return;
            }

            if (type != (byte)RpcMessageType.Request) { context.Response.StatusCode = 400; return; }

            uint requestId = BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(1, 4));
            int svcLen = BinaryPrimitives.ReadInt32LittleEndian(span.Slice(5, 4));
            if (svcLen <= 0 || svcLen > RpcProtocolConstants.MaxServiceNameLength) { context.Response.StatusCode = 400; return; }
            int offset = 9;
            string serviceName = Encoding.UTF8.GetString(span.Slice(offset, svcLen));
            offset += svcLen;

            int metLen = BinaryPrimitives.ReadInt32LittleEndian(span.Slice(offset, 4));
            if (metLen <= 0 || metLen > RpcProtocolConstants.MaxMethodNameLength) { context.Response.StatusCode = 400; return; }
            offset += 4;
            string methodName = Encoding.UTF8.GetString(span.Slice(offset, metLen));
            offset += metLen;

            int headerCount = BinaryPrimitives.ReadInt32LittleEndian(span.Slice(offset, 4));
            offset += 4;

            Dictionary<string, string>? headers;
            if (!RpcFrameCodec.TryParseHeadersSpan(span, ref offset, headerCount, out headers))
            { context.Response.StatusCode = 400; return; }

            var payload = new ReadOnlySequence<byte>(requestBytes, offset, requestBytes.Length - offset);
            var ctx = _contextPool.Get();
            ctx.PopulateRequest(0, requestId, serviceName, methodName, null, headers as IReadOnlyDictionary<string, string>);

            var requestCts = CancellationTokenSource.CreateLinkedTokenSource(
                _serverCts?.Token ?? default, context.RequestAborted);
            _activeRequests[requestId] = requestCts;
            ctx.CancellationToken = requestCts.Token;

            try
            {
                await _onRequest(ctx, payload).ConfigureAwait(false);
            }
            finally
            {
                _activeRequests.TryRemove(requestId, out _);
                requestCts.Dispose();
            }

            var data = ((ArrayBufferWriter<byte>)ctx.ResponseBuffer).WrittenMemory;
            var headerInfo = RpcFrameCodec.PrepareHeaders(ctx.HeadersOrNull);

            int bodyLen = 1 + 4 + 4 + 4 + headerInfo.HeadersBlockSize + data.Length;
            byte[] rented = ArrayPool<byte>.Shared.Rent(bodyLen);
            try
            {
                int written = RpcFrameCodec.WriteResponseSpan(rented, requestId, ctx.IsAborted, in headerInfo, data.Length);
                if (data.Length > 0) data.Span.CopyTo(rented.AsSpan(written));

                context.Response.StatusCode = 200;
                context.Response.ContentType = "application/octet-stream";
                await context.Response.Body.WriteAsync(rented, 0, bodyLen).ConfigureAwait(false);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(rented);
            }
            _contextPool.Return(ctx);
        }
        catch (Exception ex) { LogError(_logger, ex); context.Response.StatusCode = 500; }
    }

    private static async Task<byte[]> ReadBodyAsync(Stream stream)
    {
        using var ms = new MemoryStream();
        await stream.CopyToAsync(ms).ConfigureAwait(false);
        return ms.ToArray();
    }

    public ValueTask SendResponseAsync(RpcContext context, CancellationToken ct) => default;

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        _serverCts?.Cancel();
        if (_app != null) await _app.DisposeAsync().ConfigureAwait(false);
        if (_serverTask != null) try { await _serverTask.ConfigureAwait(false); } catch { }
        _serverCts?.Dispose();
    }

    #region Logging
    [LoggerMessage(EventId = 1101, Level = LogLevel.Information, Message = "[HTTP2] Server listening on port {Port}")]
    private static partial void LogListening(ILogger logger, int port);
    [LoggerMessage(EventId = 1102, Level = LogLevel.Error, Message = "[HTTP2] Request error")]
    private static partial void LogError(ILogger logger, Exception ex);
    #endregion
}
