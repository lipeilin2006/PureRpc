using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;

namespace PureRpc.Abstractions;

/// <summary>
/// 一个具体的 <see cref="AuthorizationHandlerBase"/>，通过委托方法解析用户主体。
/// 适用于快速注册内联认证逻辑，无需定义子类。
/// </summary>
public sealed class DelegatingAuthorizationHandler : AuthorizationHandlerBase
{
    private readonly Func<RpcContext, CancellationToken, ValueTask<ClaimsPrincipal?>> _resolver;

    public DelegatingAuthorizationHandler(Func<RpcContext, CancellationToken, ValueTask<ClaimsPrincipal?>> resolver)
    {
        _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
    }

    public DelegatingAuthorizationHandler(Func<RpcContext, ClaimsPrincipal?> resolver)
        : this((ctx, _) => new ValueTask<ClaimsPrincipal?>(resolver(ctx))) { }

    protected override ValueTask<ClaimsPrincipal?> ResolvePrincipalAsync(RpcContext context, CancellationToken ct)
        => _resolver(context, ct);
}
