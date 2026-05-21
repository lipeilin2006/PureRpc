using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;

namespace PureRpc.Abstractions;

/// <summary>
/// 一个具体的 <see cref="AuthorizationHandlerBase"/>，通过委托方法解析用户主体 / 
/// A concrete <see cref="AuthorizationHandlerBase"/> that resolves the user principal via a delegate.
/// 适用于快速注册内联认证逻辑，无需定义子类 / 
/// Suitable for quickly registering inline authentication logic without defining a subclass.
/// </summary>
public sealed class DelegatingAuthorizationHandler : AuthorizationHandlerBase
{
    /// <summary>
    /// 用户主体解析委托 / The user principal resolver delegate.
    /// </summary>
    private readonly Func<RpcContext, CancellationToken, ValueTask<ClaimsPrincipal?>> _resolver;

    /// <summary>
    /// 使用异步委托初始化 / Initializes with an asynchronous delegate.
    /// </summary>
    /// <param name="resolver">异步用户主体解析委托 / The asynchronous user principal resolver delegate.</param>
    /// <exception cref="ArgumentNullException">当 <paramref name="resolver"/> 为 null 时抛出 / 
    /// Thrown when <paramref name="resolver"/> is null.</exception>
    public DelegatingAuthorizationHandler(Func<RpcContext, CancellationToken, ValueTask<ClaimsPrincipal?>> resolver)
    {
        _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
    }

    /// <summary>
    /// 使用同步委托初始化 / Initializes with a synchronous delegate.
    /// 内部将同步委托包装为异步 / Internally wraps the synchronous delegate as asynchronous.
    /// </summary>
    /// <param name="resolver">同步用户主体解析委托 / The synchronous user principal resolver delegate.</param>
    public DelegatingAuthorizationHandler(Func<RpcContext, ClaimsPrincipal?> resolver)
        : this((ctx, _) => new ValueTask<ClaimsPrincipal?>(resolver(ctx))) { }

    /// <summary>
    /// 通过委托解析当前请求的用户主体 / Resolves the user principal for the current request via the delegate.
    /// </summary>
    /// <param name="context">当前 RPC 请求上下文 / The current RPC request context.</param>
    /// <param name="ct">取消令牌 / Cancellation token.</param>
    /// <returns>解析出的用户主体 / The resolved user principal.</returns>
    protected override ValueTask<ClaimsPrincipal?> ResolvePrincipalAsync(RpcContext context, CancellationToken ct)
        => _resolver(context, ct);
}
