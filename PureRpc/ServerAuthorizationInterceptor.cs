using System;
using System.Buffers;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using PureRpc.Abstractions;

namespace PureRpc;

/// <summary>
/// 服务端认证拦截器，在请求进入 Dispatcher 前从 Header 提取 token、验证、设置 context.User / 
/// Server authentication interceptor that extracts tokens from the Authorization header before the request reaches the Dispatcher,
/// validates them, and sets context.User.
/// 配合 <see cref="AuthorizationHandlerBase"/> 使用时，会自动跳过 ResolvePrincipalAsync，
/// 直接使用本拦截器设置的用户主体进行角色/Policy 检查 / 
/// When used with <see cref="AuthorizationHandlerBase"/>, it automatically skips ResolvePrincipalAsync
/// and uses the user principal set by this interceptor for role/policy checks.
/// </summary>
public sealed class ServerAuthorizationInterceptor : IRpcServerInterceptor
{
    private readonly Func<string, CancellationToken, ValueTask<ClaimsPrincipal?>> _tokenValidator;

    /// <summary>
    /// 初始化 ServerAuthorizationInterceptor 实例 / Initializes a ServerAuthorizationInterceptor instance.
    /// </summary>
    /// <param name="tokenValidator">Token 验证委托，接收 Authorization 头部值和取消令牌，返回用户主体或 null / 
    /// Token validation delegate, receives the Authorization header value and cancellation token, returns the user principal or null.</param>
    /// <exception cref="ArgumentNullException">当 <paramref name="tokenValidator"/> 为 null 时抛出 / 
    /// Thrown when <paramref name="tokenValidator"/> is null.</exception>
    public ServerAuthorizationInterceptor(Func<string, CancellationToken, ValueTask<ClaimsPrincipal?>> tokenValidator)
    {
        _tokenValidator = tokenValidator ?? throw new ArgumentNullException(nameof(tokenValidator));
    }

    /// <summary>
    /// 执行拦截逻辑：从请求头部提取并验证 token，然后调用管道中的下一个委托 / 
    /// Executes interceptor logic: extracts and validates the token from the request header, then invokes the next delegate in the pipeline.
    /// </summary>
    /// <param name="context">当前 RPC 请求上下文 / The current RPC request context.</param>
    /// <param name="payload">请求的原始字节序列 / The raw byte sequence of the request payload.</param>
    /// <param name="next">管道中的下一个处理委托 / The next processing delegate in the pipeline.</param>
    /// <returns>表示异步处理的 ValueTask / A ValueTask representing the asynchronous processing.</returns>
    public async ValueTask InvokeAsync(RpcContext context, ReadOnlySequence<byte> payload, RpcRequestDelegate next)
    {
        if (context.Headers.TryGetValue("Authorization", out var authHeader))
        {
            var principal = await _tokenValidator(authHeader, context.CancellationToken).ConfigureAwait(false);
            if (principal != null)
                context.User = principal;
        }

        await next(context, payload).ConfigureAwait(false);
    }
}