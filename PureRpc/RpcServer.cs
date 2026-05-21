using System.Buffers;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PureRpc.Abstractions;

namespace PureRpc;

/// <summary>
/// PureRpc 服务端核心实现 / Core PureRpc server implementation.
/// 管理服务分发器注册、拦截器管道构建和请求处理生命周期 / 
/// Manages service dispatcher registration, interceptor pipeline construction, and request processing lifecycle.
/// </summary>
public sealed partial class RpcServer : IRpcServer
{
    /// <summary>
    /// 传输层实例 / The transport instance.
    /// </summary>
    private readonly IServerTransport _transport;

    /// <summary>
    /// 序列化器实例 / The serializer instance.
    /// </summary>
    private readonly ISerializer _serializer;

    /// <summary>
    /// 日志记录器 / The logger instance.
    /// </summary>
    private readonly ILogger<RpcServer> _logger;

    /// <summary>
    /// RPC 指标仪表 / The RPC metrics instrument.
    /// </summary>
    private readonly RpcMetrics _metrics;

    /// <summary>
    /// 服务分发器字典，键为服务名（忽略大小写） / 
    /// Service dispatcher dictionary, keyed by service name (case-insensitive).
    /// </summary>
    private readonly Dictionary<string, IServiceDispatcher> _dispatchers;

    /// <summary>
    /// 服务器生命周期取消令牌源 / Server lifecycle cancellation token source.
    /// </summary>
    private readonly CancellationTokenSource _serverCts = new();

    /// <summary>
    /// 请求处理管道（拦截器链 + 终端处理器） / 
    /// Request processing pipeline (interceptor chain + terminal handler).
    /// </summary>
    private readonly RpcRequestDelegate _pipeline;

    /// <summary>
    /// 是否已释放 / Whether the server has been disposed.
    /// </summary>
    private bool _isDisposed;

    /// <summary>
    /// 初始化 RpcServer 实例 / Initializes a new RpcServer instance.
    /// 构建 interceptor 管道：从后向前依次包装，形成洋葱模型 / 
    /// Constructs interceptor pipeline: wraps from back to front, forming an onion model.
    /// </summary>
    /// <param name="transport">服务端传输层实例 / The server transport instance.</param>
    /// <param name="serializer">序列化器实例 / The serializer instance.</param>
    /// <param name="dispatchers">服务分发器集合 / The collection of service dispatchers.</param>
    /// <param name="logger">日志记录器（可选） / The logger (optional).</param>
    /// <param name="interceptors">服务端拦截器集合（可选） / The server interceptor collection (optional).</param>
    /// <param name="metrics">RPC 指标仪表（可选） / The RPC metrics instrument (optional).</param>
    /// <exception cref="ArgumentNullException">当 transport 或 serializer 为 null 时抛出 / 
    /// Thrown when transport or serializer is null.</exception>
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
    /// <summary>
    /// 日志：服务器正在启动 / Log: server is starting.
    /// </summary>
    [LoggerMessage(EventId = 1, Level = LogLevel.Information, Message = "[PureRpc] Starting server. Serializer: {SerializerType}. Services: {ServiceCount}")]
    private static partial void LogStarting(ILogger logger, string serializerType, int serviceCount);

    /// <summary>
    /// 日志：服务未找到 / Log: service not found.
    /// </summary>
    [LoggerMessage(EventId = 2, Level = LogLevel.Warning, Message = "[PureRpc] Service not found: {ServiceName}")]
    private static partial void LogServiceNotFound(ILogger logger, string serviceName);

    /// <summary>
    /// 日志：服务已加载 / Log: service loaded.
    /// </summary>
    [LoggerMessage(EventId = 3, Level = LogLevel.Information, Message = "[PureRpc] Loaded Service: {ServiceName}")]
    private static partial void LogServiceLoaded(ILogger logger, string serviceName);

    /// <summary>
    /// 日志：服务内部错误 / Log: internal server error.
    /// </summary>
    [LoggerMessage(EventId = 4, Level = LogLevel.Error, Message = "[PureRpc] Internal Error [Service:{serviceName}, Method:{methodName}]")]
    private static partial void LogInternalError(ILogger logger, Exception ex, string serviceName, string methodName);
    #endregion

    /// <summary>
    /// 启动服务器并开始监听 / Starts the server and begins listening.
    /// </summary>
    /// <param name="ct">控制服务器生命周期的取消令牌 / Cancellation token controlling the server lifecycle.</param>
    /// <returns>表示启动过程的 Task / A Task representing the startup process.</returns>
    /// <exception cref="ObjectDisposedException">服务器已释放 / The server has been disposed.</exception>
    public async Task StartAsync(CancellationToken ct = default)
    {
        if (_isDisposed) throw new ObjectDisposedException(nameof(RpcServer));

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, _serverCts.Token);

        LogStarting(_logger, _serializer.GetType().Name, _dispatchers.Count);

        await _transport.StartAsync(HandleRequestAsync, linkedCts.Token).ConfigureAwait(false);
    }

    /// <summary>
    /// 处理传入的 RPC 请求 / Handles an incoming RPC request.
    /// 执行拦截器管道后发送响应，异常时中止并发送错误响应 / 
    /// Executes the interceptor pipeline then sends response; on exception, aborts and sends error response.
    /// </summary>
    /// <param name="context">当前 RPC 请求上下文 / The current RPC request context.</param>
    /// <param name="payload">请求的原始字节序列 / The raw byte sequence of the request payload.</param>
    /// <returns>表示处理过程的 Task / A Task representing the handling process.</returns>
    public async Task HandleRequestAsync(RpcContext context, ReadOnlySequence<byte> payload)
    {
        var startTimestamp = Stopwatch.GetTimestamp();
        var tags = new TagList { { "rpc.service", context.ServiceName }, { "rpc.method", context.MethodName } };

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
            await _transport.SendResponseAsync(context, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _metrics.ServerErrors.Add(1, tags);
            LogInternalError(_logger, ex, context.ServiceName, context.MethodName);

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

    /// <summary>
    /// 异步释放服务器资源 / Asynchronously disposes server resources.
    /// 取消所有正在处理的请求并释放传输层 / 
    /// Cancels all in-progress requests and disposes the transport.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        _serverCts.Cancel();
        await _transport.DisposeAsync().ConfigureAwait(false);
        _serverCts.Dispose();
    }
}
