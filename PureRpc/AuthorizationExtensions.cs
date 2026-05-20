using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using PureRpc.Abstractions;

namespace Microsoft.Extensions.DependencyInjection;

public static class AuthorizationExtensions
{
    /// <summary>
    /// 注册一个自定义的 <see cref="IAuthorizationHandler"/> 实现。
    /// </summary>
    public static IServerBuilder AddAuthorization<T>(this IServerBuilder builder)
        where T : class, IAuthorizationHandler
    {
        builder.Services.AddSingleton<IAuthorizationHandler, T>();
        return builder;
    }

    /// <summary>
    /// 通过委托方法注册内联的角色认证逻辑。
    /// 委托接收 <see cref="RpcContext"/> 和 <see cref="CancellationToken"/>，返回用户主体或 null。
    /// </summary>
    public static IServerBuilder AddAuthorization(this IServerBuilder builder,
        Func<RpcContext, CancellationToken, ValueTask<ClaimsPrincipal?>> resolver)
    {
        builder.Services.AddSingleton<IAuthorizationHandler>(
            _ => new DelegatingAuthorizationHandler(resolver));
        return builder;
    }

    /// <summary>
    /// 通过委托方法注册内联的角色认证逻辑（同步版本）。
    /// </summary>
    public static IServerBuilder AddAuthorization(this IServerBuilder builder,
        Func<RpcContext, ClaimsPrincipal?> resolver)
    {
        builder.Services.AddSingleton<IAuthorizationHandler>(
            _ => new DelegatingAuthorizationHandler(resolver));
        return builder;
    }
}
