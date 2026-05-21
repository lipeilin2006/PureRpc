using System.Buffers;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.ObjectPool;
using PureRpc.Abstractions;

namespace PureRpc;

/// <summary>
/// PureRpc 客户端核心实现 / Core PureRpc client implementation.
/// 管理待处理请求、序列化/反序列化、传输层交互和拦截器管道 / 
/// Manages pending requests, serialization/deserialization, transport interaction, and interceptor pipeline.
/// </summary>
internal sealed partial class RpcClient : IRpcClient
{
    /// <summary>
    /// 待处理请求字典的初始容量 / Initial capacity for the pending requests dictionary.
    /// </summary>
    private const int InitialPendingRequestCapacity = 1024;

    /// <summary>
    /// 客户端传输层实例 / The client transport instance.
    /// </summary>
    private readonly IClientTransport _transport;

    /// <summary>
    /// 日志记录器 / The logger instance.
    /// </summary>
    private readonly ILogger<RpcClient> _logger;

    /// <summary>
    /// RPC 指标仪表 / The RPC metrics instrument.
    /// </summary>
    private readonly RpcMetrics _metrics;

    /// <summary>
    /// 待处理请求的并发字典，键为 RequestId / 
    /// Concurrent dictionary of pending requests, keyed by RequestId.
    /// </summary>
    private readonly ConcurrentDictionary<uint, PendingRequest> _pendingRequests =
        new(Environment.ProcessorCount, InitialPendingRequestCapacity);

    /// <summary>
    /// 下一个请求 ID（递增） / The next request ID (incrementing).
    /// </summary>
    private int _nextRequestId = 0;

    /// <summary>
    /// 连接是否已启动的标志（0=未启动，1=已启动） / 
    /// Flag indicating whether the connection has been started (0=not started, 1=started).
    /// </summary>
    private int _connectionStarted = 0;

    /// <summary>
    /// 是否已释放 / Whether the client has been disposed.
    /// </summary>
    private bool _isDisposed;

    /// <summary>
    /// 默认请求头部字典 / Default request headers dictionary.
    /// </summary>
    private readonly Dictionary<string, string> _defaultHeaders = new();

    /// <summary>
    /// 对象池提供者 / Object pool provider.
    /// </summary>
    private readonly DefaultObjectPoolProvider _poolProvider = new() { MaximumRetained = 1024 };

    /// <summary>
    /// PendingRequest 对象池 / Object pool for PendingRequest instances.
    /// </summary>
    private readonly ObjectPool<PendingRequest> _pendingPool;

    /// <summary>
    /// 取消待处理请求的回调，注册到 CancellationToken / 
    /// Callback for canceling pending requests, registered with CancellationToken.
    /// 尝试发送 Cancel 帧到服务端并本地取消请求 / 
    /// Attempts to send a Cancel frame to the server and cancels the request locally.
    /// </summary>
    private static readonly Action<object?, CancellationToken> CancelPendingRequestCallback = static (state, token) =>
    {
        if (state is not CancelRegistrationState s) return;
        if (s.Client._pendingRequests.TryRemove(s.RequestId, out var pending))
        {
            pending.SetCanceled(token);
            var client = s.Client;
            var reqId = s.RequestId;
            var logger = client._logger;
            _ = Task.Run(async () =>
            {
                try
                {
                    using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                    await client._transport.CancelRequestAsync(reqId).AsTask().WaitAsync(timeoutCts.Token);
                }
                catch (Exception ex)
                {
                    RpcClient.LogCancelError(logger, ex);
                }
            });
        }
    };

    /// <summary>
    /// 获取客户端是否可用（已连接且未释放） / 
    /// Gets whether the client is available (connected and not disposed).
    /// </summary>
    public bool IsAvailable => _transport.IsConnected && !_isDisposed;

    /// <summary>
    /// 初始化 RpcClient 实例 / Initializes a new RpcClient instance.
    /// </summary>
    /// <param name="transport">客户端传输层实例 / The client transport instance.</param>
    /// <param name="logger">日志记录器（可选） / The logger (optional).</param>
    /// <param name="metrics">RPC 指标仪表（可选） / The RPC metrics instrument (optional).</param>
    /// <exception cref="ArgumentNullException">当 <paramref name="transport"/> 为 null 时抛出 / 
    /// Thrown when <paramref name="transport"/> is null.</exception>
    public RpcClient(IClientTransport transport, ILogger<RpcClient>? logger = null, RpcMetrics? metrics = null)
    {
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        _logger = logger ?? NullLogger<RpcClient>.Instance;
        _metrics = metrics ?? new RpcMetrics();
        _pendingPool = _poolProvider.Create(new PendingRequestPolicy());
    }

    /// <summary>
    /// PendingRequest 对象池策略 / Object pool policy for PendingRequest.
    /// </summary>
    private sealed class PendingRequestPolicy : IPooledObjectPolicy<PendingRequest>
    {
        /// <summary>
        /// 创建新的 PendingRequest 实例 / Creates a new PendingRequest instance.
        /// </summary>
        public PendingRequest Create() => new();

        /// <summary>
        /// 归还 PendingRequest 时重置状态 / Resets the PendingRequest when returning to the pool.
        /// </summary>
        public bool Return(PendingRequest obj)
        {
            obj.Reset();
            return true;
        }
    }

    #region Source Generated Logging
    /// <summary>
    /// 日志：客户端正在启动传输层连接 / Log: client is starting transport connection.
    /// </summary>
    [LoggerMessage(EventId = 201, Level = LogLevel.Information, Message = "[PureRpcClient] Starting transport connection...")]
    private static partial void LogStarting(ILogger logger);

    /// <summary>
    /// 日志：传输层连接成功 / Log: transport connected successfully.
    /// </summary>
    [LoggerMessage(EventId = 202, Level = LogLevel.Information, Message = "[PureRpcClient] Transport connected successfully.")]
    private static partial void LogConnected(ILogger logger);

    /// <summary>
    /// 日志：StartAsync 已被调用过 / Log: StartAsync was already called.
    /// </summary>
    [LoggerMessage(EventId = 203, Level = LogLevel.Warning, Message = "[PureRpcClient] StartAsync was already called.")]
    private static partial void LogAlreadyStarted(ILogger logger);

    /// <summary>
    /// 日志：传输层启动失败 / Log: transport failed to start.
    /// </summary>
    [LoggerMessage(EventId = 204, Level = LogLevel.Error, Message = "[PureRpcClient] Failed to start transport.")]
    private static partial void LogStartError(ILogger logger, Exception ex);

    /// <summary>
    /// 日志：正在调用 RPC 方法 / Log: invoking RPC method.
    /// </summary>
    [LoggerMessage(EventId = 205, Level = LogLevel.Debug, Message = "[PureRpcClient] Invoking {serviceName}.{methodName} (RequestId: {requestId})")]
    private static partial void LogInvoking(ILogger logger, string serviceName, string methodName, uint requestId);

    /// <summary>
    /// 日志：请求执行出错 / Log: request execution error.
    /// </summary>
    [LoggerMessage(EventId = 206, Level = LogLevel.Error, Message = "[PureRpcClient] Execution error for Request {requestId}.")]
    private static partial void LogExecutionError(ILogger logger, Exception ex, uint requestId);

    /// <summary>
    /// 日志：Cancel 帧发送失败 / Log: cancel frame send failed.
    /// </summary>
    [LoggerMessage(EventId = 207, Level = LogLevel.Warning, Message = "[PureRpcClient] Cancel frame send failed.")]
    internal static partial void LogCancelError(ILogger logger, Exception ex);
    #endregion

    /// <summary>
    /// 启动客户端并建立传输层连接 / Starts the client and establishes the transport connection.
    /// 使用原子操作防止重复启动 / Uses atomic operation to prevent duplicate starts.
    /// </summary>
    /// <param name="ct">连接过程的取消令牌 / Cancellation token for the connection process.</param>
    /// <returns>表示启动过程的 Task / A Task representing the startup process.</returns>
    public async Task StartAsync(CancellationToken ct = default)
    {
        if (Interlocked.CompareExchange(ref _connectionStarted, 1, 0) != 0)
        {
            LogAlreadyStarted(_logger);
            return;
        }

        try
        {
            LogStarting(_logger);
            await _transport.ConnectAsync(OnResponseReceived, ct).ConfigureAwait(false);
            LogConnected(_logger);
        }
        catch (Exception ex)
        {
            Interlocked.Exchange(ref _connectionStarted, 0);
            LogStartError(_logger, ex);
            throw;
        }
    }

    /// <summary>
    /// 执行异步 RPC 调用 / Performs an asynchronous RPC call.
    /// 合并默认头部与调用头部，发送请求并等待响应 / 
    /// Merges default headers with call headers, sends request, and awaits response.
    /// 默认超时 30 秒 / Default timeout is 30 seconds.
    /// </summary>
    /// <param name="serviceName">目标服务名称 / Target service name.</param>
    /// <param name="methodName">目标方法名称 / Target method name.</param>
    /// <param name="requestPayload">已序列化的请求参数负载 / The serialized request parameter payload.</param>
    /// <param name="ct">RPC 调用的取消令牌 / Cancellation token for the RPC call.</param>
    /// <param name="headers">可选的请求头部 / Optional request headers.</param>
    /// <returns>包含原始响应数据的 ValueTask / A ValueTask containing the raw response data.</returns>
    /// <exception cref="ObjectDisposedException">客户端已释放 / The client has been disposed.</exception>
    /// <exception cref="IOException">客户端不可用 / The client is not available.</exception>
    /// <exception cref="RpcException">服务端返回错误 / The server returned an error.</exception>
    public async ValueTask<ReadOnlySequence<byte>> CallAsync(
        string serviceName,
        string methodName,
        ReadOnlySequence<byte> requestPayload,
        CancellationToken ct,
        IDictionary<string, string>? headers = null)
    {
        if (_isDisposed) throw new ObjectDisposedException(nameof(RpcClient));

        if (!IsAvailable)
        {
            throw new IOException("RPC Client is not available (not connected or disposed).");
        }

        var startTimestamp = Stopwatch.GetTimestamp();
        var tags = new TagList { { "rpc.service", serviceName }, { "rpc.method", methodName } };

        _metrics.ClientRequests.Add(1, tags);

        uint requestId = (uint)Interlocked.Increment(ref _nextRequestId);

        var pending = _pendingPool.Get();
        _pendingRequests[requestId] = pending;

        CancellationTokenSource? timeoutCts = null;
        CancellationTokenSource? linkedCts = null;
        CancellationToken effectiveCt;
        CancellationTokenRegistration registration = default;
        try
        {
            if (ct.CanBeCanceled)
            {
                timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);
                effectiveCt = linkedCts.Token;
            }
            else
            {
                timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                effectiveCt = timeoutCts.Token;
            }

            registration = effectiveCt.UnsafeRegister(CancelPendingRequestCallback, new CancelRegistrationState { Client = this, RequestId = requestId });

            LogInvoking(_logger, serviceName, methodName, requestId);

            var mergedHeaders = MergeHeaders(headers);

            await _transport.SendAsync(requestId, serviceName, methodName, requestPayload, effectiveCt, mergedHeaders).ConfigureAwait(false);

            return await pending.AsValueTask().ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _metrics.ClientErrors.Add(1, tags);
            LogExecutionError(_logger, ex, requestId);
            throw;
        }
        finally
        {
            _metrics.ClientRequestDuration.Record(
                Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds, tags);
            registration.Dispose();
            linkedCts?.Dispose();
            timeoutCts?.Dispose();
            _pendingRequests.TryRemove(requestId, out _);
            _pendingPool.Return(pending);
        }
    }

    /// <summary>
    /// 合并调用头部与默认头部，调用头部优先 / 
    /// Merges call headers with default headers; call headers take precedence.
    /// </summary>
    /// <param name="callHeaders">调用时传入的头部 / Headers passed at call time.</param>
    /// <returns>合并后的头部字典 / The merged headers dictionary.</returns>
    private IDictionary<string, string>? MergeHeaders(IDictionary<string, string>? callHeaders)
    {
        if (_defaultHeaders.Count == 0) return callHeaders;
        if (callHeaders == null) return new Dictionary<string, string>(_defaultHeaders);

        var merged = new Dictionary<string, string>(callHeaders);
        foreach (var kv in _defaultHeaders)
        {
            if (!merged.ContainsKey(kv.Key))
                merged[kv.Key] = kv.Value;
        }
        return merged;
    }

    /// <summary>
    /// 传输层响应回调，当接收到完整响应帧时调用 / 
    /// Transport response callback, called when a complete response frame is received.
    /// 根据成功/失败状态完成或异常终止对应的 PendingRequest / 
    /// Completes or faults the corresponding PendingRequest based on success/failure status.
    /// </summary>
    /// <param name="requestId">请求标识符 / Request identifier.</param>
    /// <param name="payload">响应负载 / Response payload.</param>
    /// <param name="success">是否成功 / Whether the call succeeded.</param>
    /// <param name="error">错误消息（失败时） / Error message (on failure).</param>
    /// <param name="headers">响应头部 / Response headers.</param>
    internal void OnResponseReceived(uint requestId, ReadOnlySequence<byte> payload, bool success, string? error, IReadOnlyDictionary<string, string>? headers = null)
    {
        if (_pendingRequests.TryRemove(requestId, out var pending))
        {
            if (success)
            {
                pending.SetResult(new ReadOnlySequence<byte>(payload.ToArray()));
            }
            else
            {
                pending.SetException(new RpcException(error ?? "Unknown remote error."));
            }
        }
    }

    /// <summary>
    /// 设置默认请求头部，每次 CallAsync 调用将自动携带 / 
    /// Sets a default request header that will be automatically included in every CallAsync invocation.
    /// </summary>
    /// <param name="key">头部键名 / The header key.</param>
    /// <param name="value">头部值 / The header value.</param>
    /// <exception cref="ArgumentNullException">当 <paramref name="value"/> 为 null 时抛出 / 
    /// Thrown when <paramref name="value"/> is null.</exception>
    public void SetDefaultHeader(string key, string value)
    {
        _defaultHeaders[key] = value ?? throw new ArgumentNullException(nameof(value));
    }

    /// <summary>
    /// 移除指定的默认请求头部 / Removes the specified default request header.
    /// </summary>
    /// <param name="key">要移除的头部键名 / The header key to remove.</param>
    /// <returns>如果头部存在并被移除返回 true / true if the header existed and was removed.</returns>
    public bool RemoveDefaultHeader(string key) => _defaultHeaders.Remove(key);

    /// <summary>
    /// 清除所有默认请求头部 / Clears all default request headers.
    /// </summary>
    public void ClearDefaultHeaders() => _defaultHeaders.Clear();

    /// <summary>
    /// 异步释放客户端资源 / Asynchronously disposes client resources.
    /// 释放传输层并异常终止所有待处理请求 / 
    /// Disposes the transport and faults all pending requests.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        await _transport.DisposeAsync().ConfigureAwait(false);

        var exception = new IOException("Client has been disposed.");
        foreach (var pending in _pendingRequests.Values)
        {
            pending.SetException(exception);
        }
        _pendingRequests.Clear();
    }

    /// <summary>
    /// 取消注册状态，用于 CancellationToken 回调 / 
    /// Cancel registration state, used for CancellationToken callback.
    /// </summary>
    private sealed class CancelRegistrationState
    {
        /// <summary>
        /// 客户端实例引用 / Reference to the client instance.
        /// </summary>
        public RpcClient Client { get; set; } = null!;

        /// <summary>
        /// 请求标识符 / Request identifier.
        /// </summary>
        public uint RequestId { get; set; }
    }
}
