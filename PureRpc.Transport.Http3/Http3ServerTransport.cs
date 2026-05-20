using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.ObjectPool;
using Microsoft.Extensions.Options;
using PureRpc.Abstractions;

namespace PureRpc.Transport.Http3;

internal sealed partial class Http3ServerTransport : IServerTransport
{
    private readonly Http3ServerOptions _options;
    private readonly ILogger<Http3ServerTransport> _logger;
    private readonly ObjectPool<RpcContext> _contextPool;
    private readonly ConcurrentDictionary<uint, CancellationTokenSource> _activeRequests = new();

    private WebApplication? _app;
    private CancellationTokenSource? _serverCts;
    private Task? _serverTask;
    private bool _disposed;
    private Func<RpcContext, ReadOnlySequence<byte>, Task>? _onRequest;

    public Http3ServerTransport(IOptions<Http3ServerOptions> options, ILogger<Http3ServerTransport>? logger = null)
    {
        _options = options.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? NullLogger<Http3ServerTransport>.Instance;
        var provider = new DefaultObjectPoolProvider { MaximumRetained = 1024 };
        _contextPool = provider.Create(new RpcContextPolicy());
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
                listen.Protocols = Microsoft.AspNetCore.Server.Kestrel.Core.HttpProtocols.Http3;
                if (_options.TlsCertPath != null && _options.TlsKeyPath != null)
                {
                    listen.UseHttps(_options.TlsCertPath, _options.TlsKeyPath);
                }
                else
                {
                    // HTTP/3 requires TLS; use a self-signed dev cert as fallback
                    listen.UseHttps();
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

            // Cancel frame
            if (type == 8)
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

            if (type != 1) { context.Response.StatusCode = 400; return; }

            uint requestId = BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(1, 4));
            int svcLen = BinaryPrimitives.ReadInt32LittleEndian(span.Slice(5, 4));
            if (svcLen <= 0 || svcLen > 256) { context.Response.StatusCode = 400; return; }
            int offset = 9;
            string serviceName = Encoding.UTF8.GetString(span.Slice(offset, svcLen));
            offset += svcLen;

            int metLen = BinaryPrimitives.ReadInt32LittleEndian(span.Slice(offset, 4));
            if (metLen <= 0 || metLen > 256) { context.Response.StatusCode = 400; return; }
            offset += 4;
            string methodName = Encoding.UTF8.GetString(span.Slice(offset, metLen));
            offset += metLen;

            int headerCount = BinaryPrimitives.ReadInt32LittleEndian(span.Slice(offset, 4));
            if (headerCount < 0 || headerCount > 64) { context.Response.StatusCode = 400; return; }
            offset += 4;

            var headers = new Dictionary<string, string>();
            for (int i = 0; i < headerCount; i++)
            {
                int keyLen = BinaryPrimitives.ReadInt32LittleEndian(span.Slice(offset, 4));
                if (keyLen <= 0 || keyLen > 256) { context.Response.StatusCode = 400; return; }
                offset += 4;
                string key = Encoding.UTF8.GetString(span.Slice(offset, keyLen));
                offset += keyLen;
                int valLen = BinaryPrimitives.ReadInt32LittleEndian(span.Slice(offset, 4));
                if (valLen <= 0 || valLen > 4096) { context.Response.StatusCode = 400; return; }
                offset += 4;
                string val = Encoding.UTF8.GetString(span.Slice(offset, valLen));
                offset += valLen;
                headers[key] = val;
            }

            var payload = new ReadOnlySequence<byte>(span.Slice(offset).ToArray());
            var ctx = _contextPool.Get();
            ctx.ConnectionId = 0;
            ctx.RequestId = requestId;
            ctx.ServiceName = serviceName;
            ctx.MethodName = methodName;
            foreach (var kv in headers) ctx.Headers[kv.Key] = kv.Value;

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
            int hc = ctx.Headers.Count;
            int hbs = 0;
            string[]? keys = null, values = null;
            int[]? ks = null, vs = null;
            if (hc > 0)
            {
                keys = new string[hc]; values = new string[hc];
                ks = new int[hc]; vs = new int[hc];
                int i = 0;
                foreach (var kv in ctx.Headers)
                {
                    keys[i] = kv.Key; values[i] = kv.Value;
                    ks[i] = Encoding.UTF8.GetByteCount(kv.Key);
                    vs[i] = Encoding.UTF8.GetByteCount(kv.Value);
                    hbs += 4 + ks[i] + 4 + vs[i]; i++;
                }
            }

            int bodyLen = 1 + 4 + 4 + 4 + hbs + data.Length;
            var resp = new byte[bodyLen];
            resp[0] = (byte)(ctx.IsAborted ? 3 : 2);
            BinaryPrimitives.WriteUInt32LittleEndian(resp.AsSpan(1, 4), requestId);
            BinaryPrimitives.WriteInt32LittleEndian(resp.AsSpan(5, 4), ctx.IsAborted ? 500 : 200);
            int pos = 9;
            BinaryPrimitives.WriteInt32LittleEndian(resp.AsSpan(pos, 4), hc); pos += 4;
            for (int i = 0; i < hc; i++)
            {
                BinaryPrimitives.WriteInt32LittleEndian(resp.AsSpan(pos, 4), ks![i]); pos += 4;
                Encoding.UTF8.GetBytes(keys![i], resp.AsSpan(pos, ks[i])); pos += ks[i];
                BinaryPrimitives.WriteInt32LittleEndian(resp.AsSpan(pos, 4), vs![i]); pos += 4;
                Encoding.UTF8.GetBytes(values![i], resp.AsSpan(pos, vs[i])); pos += vs[i];
            }
            if (data.Length > 0) data.Span.CopyTo(resp.AsSpan(pos));

            context.Response.StatusCode = 200;
            context.Response.ContentType = "application/octet-stream";
            await context.Response.Body.WriteAsync(resp).ConfigureAwait(false);
            _contextPool.Return(ctx);
        }
        catch (OperationCanceledException) { context.Response.StatusCode = 499; }
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
    [LoggerMessage(EventId = 1201, Level = LogLevel.Information, Message = "[HTTP3] Server listening on port {Port}")]
    private static partial void LogListening(ILogger logger, int port);
    [LoggerMessage(EventId = 1202, Level = LogLevel.Error, Message = "[HTTP3] Request error")]
    private static partial void LogError(ILogger logger, Exception ex);
    #endregion
}
