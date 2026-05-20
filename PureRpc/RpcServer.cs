using System.Buffers;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PureRpc.Abstractions;

namespace PureRpc;

public sealed partial class RpcServer : IRpcServer
{
    private readonly IServerTransport _transport;
    private readonly ISerializer _serializer;
    private readonly ILogger<RpcServer> _logger;
    private readonly RpcMetrics _metrics;

    private readonly Dictionary<string, IServiceDispatcher> _dispatchers;
    private readonly CancellationTokenSource _serverCts = new();
    private readonly RpcRequestDelegate _pipeline;
    private bool _isDisposed;

    public RpcServer(
        IServerTransport transport,
        ISerializer serializer,
        IEnumerable<IServiceDispatcher> dispatchers,
        ILogger<RpcServer>? logger = null,
        IEnumerable<IRpcServerInterceptor>? interceptors = null,
        RpcMetrics? metrics = null)
    {
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
        _logger = logger ?? NullLogger<RpcServer>.Instance;
        _metrics = metrics ?? new RpcMetrics();

        _dispatchers = dispatchers.ToDictionary(
            d => d.ServiceName,
            d => d,
            StringComparer.OrdinalIgnoreCase);

        // 统一使用日志方法
        foreach (var serviceName in _dispatchers.Keys)
        {
            LogServiceLoaded(_logger, serviceName);
        }

        RpcRequestDelegate terminal = (ctx, payload) =>
        {
            if (_dispatchers.TryGetValue(ctx.ServiceName, out var dispatcher))
            {
                return dispatcher.DispatchAsync(ctx.MethodName, payload, ctx);
            }
            LogServiceNotFound(_logger, ctx.ServiceName);
            ctx.Abort();
            return default;
        };

        var interceptorList = interceptors?.ToList();
        if (interceptorList is { Count: > 0 })
        {
            RpcRequestDelegate pipeline = terminal;
            for (int i = interceptorList.Count - 1; i >= 0; i--)
            {
                var interceptor = interceptorList[i];
                var next = pipeline;
                pipeline = (ctx, payload) => interceptor.InvokeAsync(ctx, payload, next);
            }
            _pipeline = pipeline;
        }
        else
        {
            _pipeline = terminal;
        }
    }

    #region Source Generated Logging
    [LoggerMessage(EventId = 1, Level = LogLevel.Information, Message = "[PureRpc] Starting server. Serializer: {SerializerType}. Services: {ServiceCount}")]
    private static partial void LogStarting(ILogger logger, string serializerType, int serviceCount);

    [LoggerMessage(EventId = 2, Level = LogLevel.Warning, Message = "[PureRpc] Service not found: {ServiceName}")]
    private static partial void LogServiceNotFound(ILogger logger, string serviceName);

    [LoggerMessage(EventId = 3, Level = LogLevel.Information, Message = "[PureRpc] Loaded Service: {ServiceName}")]
    private static partial void LogServiceLoaded(ILogger logger, string serviceName);

    [LoggerMessage(EventId = 4, Level = LogLevel.Error, Message = "[PureRpc] Internal Error [Service:{serviceName}, Method:{methodName}]")]
    private static partial void LogInternalError(ILogger logger, Exception ex, string serviceName, string methodName);
    #endregion

    public async Task StartAsync(CancellationToken ct = default)
    {
        if (_isDisposed) throw new ObjectDisposedException(nameof(RpcServer));

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, _serverCts.Token);

        LogStarting(_logger, _serializer.GetType().Name, _dispatchers.Count);

        await _transport.StartAsync(HandleRequestAsync, linkedCts.Token).ConfigureAwait(false);
    }

    public async Task HandleRequestAsync(RpcContext context, ReadOnlySequence<byte> payload)
    {
        var startTimestamp = Stopwatch.GetTimestamp();
        var tags = new KeyValuePair<string, object?>[]
        {
            new("rpc.service", context.ServiceName),
            new("rpc.method", context.MethodName),
        };

        _metrics.ServerRequests.Add(1, tags);
        _metrics.ServerActiveRequests.Add(1, tags);

        try
        {
            if (_serverCts.IsCancellationRequested) return;

            await _pipeline(context, payload).ConfigureAwait(false);

            await _transport.SendResponseAsync(context, _serverCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            _metrics.ServerErrors.Add(1, tags);
            // C1: 确保 RpcContext 归还池，避免泄漏
            await _transport.SendResponseAsync(context, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _metrics.ServerErrors.Add(1, tags);
            LogInternalError(_logger, ex, context.ServiceName, context.MethodName);

            // 将异常信息写入响应缓冲区，使传输层能回传给客户端
            try
            {
                if (context.ResponseBuffer is System.Buffers.ArrayBufferWriter<byte> writer)
                {
                    var msg = $"RpcError: {ex.GetType().Name}: {ex.Message}";
                    var bytes = System.Text.Encoding.UTF8.GetBytes(msg);
                    writer.Write(bytes);
                }
            }
            catch { /* best-effort */ }

            context.Abort();
            await _transport.SendResponseAsync(context, _serverCts.Token).ConfigureAwait(false);
        }
        finally
        {
            _metrics.ServerRequestDuration.Record(
                Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds, tags);
            _metrics.ServerActiveRequests.Add(-1, tags);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        _serverCts.Cancel();
        await _transport.DisposeAsync().ConfigureAwait(false);
        _serverCts.Dispose();
    }
}
