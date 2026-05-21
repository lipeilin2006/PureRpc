using System;
using System.Buffers;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using PureRpc.Abstractions;

namespace PureRpc;

/// <summary>
/// 客户端认证拦截器，自动在出站请求中注入 Authorization header / 
/// Client authentication interceptor that automatically injects the Authorization header in outbound requests.
/// 通过委托函数动态获取 token，支持 Bearer / 自定义 scheme / 
/// Dynamically obtains tokens via a delegate function, supporting Bearer / custom schemes.
/// </summary>
public sealed class ClientAuthorizationInterceptor : IRpcClientInterceptor
{
    private readonly Func<string> _tokenFactory;
    private readonly string _scheme;

    /// <summary>
    /// 初始化 ClientAuthorizationInterceptor 实例 / Initializes a ClientAuthorizationInterceptor instance.
    /// </summary>
    /// <param name="tokenFactory">Token 生成委托，每次调用返回当前 token / Token factory delegate, returns the current token on each invocation.</param>
    /// <param name="scheme">认证方案名称，默认为 "Bearer" / Authentication scheme name, defaults to "Bearer".</param>
    /// <exception cref="ArgumentNullException">当 <paramref name="tokenFactory"/> 为 null 时抛出 / Thrown when <paramref name="tokenFactory"/> is null.</exception>
    public ClientAuthorizationInterceptor(Func<string> tokenFactory, string scheme = "Bearer")
    {
        _tokenFactory = tokenFactory ?? throw new ArgumentNullException(nameof(tokenFactory));
        _scheme = scheme;
    }

    /// <summary>
    /// 执行拦截逻辑：注入 Authorization 头部后调用管道中的下一个委托 / 
    /// Executes interceptor logic: injects the Authorization header then invokes the next delegate in the pipeline.
    /// </summary>
    /// <param name="serviceName">目标服务名称 / The target service name.</param>
    /// <param name="methodName">目标方法名称 / The target method name.</param>
    /// <param name="requestPayload">已序列化的请求参数负载 / The serialized request parameter payload.</param>
    /// <param name="ct">取消令牌 / Cancellation token.</param>
    /// <param name="headers">可选的请求头部 / Optional request headers.</param>
    /// <param name="next">管道中的下一个调用委托 / The next call delegate in the pipeline.</param>
    /// <returns>包含原始响应数据的 ValueTask / A ValueTask containing the raw response data.</returns>
    public ValueTask<ReadOnlySequence<byte>> InvokeAsync(
        string serviceName, string methodName, ReadOnlySequence<byte> requestPayload,
        CancellationToken ct, IDictionary<string, string>? headers, RpcCallDelegate next)
    {
        headers ??= new Dictionary<string, string>();
        headers["Authorization"] = $"{_scheme} {_tokenFactory()}";
        return next(serviceName, methodName, requestPayload, ct, headers);
    }
}