using System;
using System.Buffers;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using PureRpc.Abstractions;

namespace PureRpc;

/// <summary>
/// 服务端认证拦截器，在请求进入 Dispatcher 前从 Header 提取 token、验证、设置 context.User。
/// 配合 <see cref="AuthorizationHandlerBase"/> 使用时，会自动跳过 ResolvePrincipalAsync，
/// 直接使用本拦截器设置的用户主体进行角色/Policy 检查。
/// </summary>
public sealed class ServerAuthorizationInterceptor : IRpcServerInterceptor
{
    private readonly Func<string, CancellationToken, ValueTask<ClaimsPrincipal?>> _tokenValidator;

    public ServerAuthorizationInterceptor(Func<string, CancellationToken, ValueTask<ClaimsPrincipal?>> tokenValidator)
    {
        _tokenValidator = tokenValidator ?? throw new ArgumentNullException(nameof(tokenValidator));
    }

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
