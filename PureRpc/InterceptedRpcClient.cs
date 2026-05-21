using System.Buffers;
using PureRpc.Abstractions;

namespace PureRpc;

/// <summary>
/// 带拦截器管道的 RPC 客户端装饰器 / RPC client decorator with interceptor pipeline.
/// 将 <see cref="IRpcClientInterceptor"/> 链式组织为洋葱模型管道 / 
/// Organizes <see cref="IRpcClientInterceptor"/> instances into an onion-model pipeline.
/// 所有 RPC 调用都先经过拦截器管道再到达底层客户端 / 
/// All RPC calls pass through the interceptor pipeline before reaching the underlying client.
/// </summary>
internal sealed class InterceptedRpcClient : IRpcClient
{
    private readonly IRpcClient _inner;
    private readonly RpcCallDelegate _pipeline;

    /// <summary>
    /// 获取客户端是否可用（已连接且未释放） / 
    /// Gets whether the client is available (connected and not disposed).
    /// </summary>
    public bool IsAvailable => _inner.IsAvailable;

    /// <summary>
    /// 初始化 InterceptedRpcClient 实例 / Initializes an InterceptedRpcClient instance.
    /// 构建拦截器管道：从后向前依次包装 / Constructs interceptor pipeline: wraps from back to front.
    /// </summary>
    /// <param name="inner">底层 RPC 客户端实例 / The underlying RPC client instance.</param>
    /// <param name="interceptors">客户端拦截器集合（可为 null） / The client interceptor collection (may be null).</param>
    /// <exception cref="ArgumentNullException">当 <paramref name="inner"/> 为 null 时抛出 / Thrown when <paramref name="inner"/> is null.</exception>
    public InterceptedRpcClient(IRpcClient inner, IEnumerable<IRpcClientInterceptor>? interceptors)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));

        var interceptorList = interceptors?.ToList();
        if (interceptorList is { Count: > 0 })
        {
            RpcCallDelegate pipeline = (svc, mtd, payload, ct, h) => inner.CallAsync(svc, mtd, payload, ct, h);
            for (int i = interceptorList.Count - 1; i >= 0; i--)
            {
                var interceptor = interceptorList[i];
                var next = pipeline;
                pipeline = (svc, mtd, payload, ct, h) => interceptor.InvokeAsync(svc, mtd, payload, ct, h, next);
            }
            _pipeline = pipeline;
        }
        else
        {
            _pipeline = (svc, mtd, payload, ct, h) => inner.CallAsync(svc, mtd, payload, ct, h);
        }
    }

    /// <summary>
    /// 启动客户端并建立底层连接 / Starts the client and establishes the underlying connection.
    /// </summary>
    /// <param name="ct">连接过程的取消令牌 / Cancellation token for the connection process.</param>
    /// <returns>表示启动过程的 Task / A Task representing the startup process.</returns>
    public Task StartAsync(CancellationToken ct = default) => _inner.StartAsync(ct);

    /// <summary>
    /// 通过拦截器管道执行异步 RPC 调用 / Performs an asynchronous RPC call through the interceptor pipeline.
    /// </summary>
    /// <param name="serviceName">目标服务名称 / Target service name.</param>
    /// <param name="methodName">目标方法名称 / Target method name.</param>
    /// <param name="requestPayload">已序列化的请求参数负载 / The serialized request parameter payload.</param>
    /// <param name="ct">RPC 调用的取消令牌 / Cancellation token for the RPC call.</param>
    /// <param name="headers">可选的请求头部 / Optional request headers.</param>
    /// <returns>包含原始响应数据的 ValueTask / A ValueTask containing the raw response data.</returns>
    public ValueTask<ReadOnlySequence<byte>> CallAsync(
        string serviceName, string methodName, ReadOnlySequence<byte> requestPayload,
        CancellationToken ct, IDictionary<string, string>? headers = null)
        => _pipeline(serviceName, methodName, requestPayload, ct, headers);

    /// <summary>
    /// 设置默认请求头部 / Sets a default request header.
    /// </summary>
    /// <param name="key">头部键名 / The header key.</param>
    /// <param name="value">头部值 / The header value.</param>
    public void SetDefaultHeader(string key, string value) => _inner.SetDefaultHeader(key, value);

    /// <summary>
    /// 移除指定的默认请求头部 / Removes the specified default request header.
    /// </summary>
    /// <param name="key">要移除的头部键名 / The header key to remove.</param>
    /// <returns>如果头部存在并被移除返回 true / True if the header existed and was removed.</returns>
    public bool RemoveDefaultHeader(string key) => _inner.RemoveDefaultHeader(key);

    /// <summary>
    /// 清除所有默认请求头部 / Clears all default request headers.
    /// </summary>
    public void ClearDefaultHeaders() => _inner.ClearDefaultHeaders();

    /// <summary>
    /// 异步释放客户端资源 / Asynchronously disposes client resources.
    /// </summary>
    public ValueTask DisposeAsync() => _inner.DisposeAsync();
}