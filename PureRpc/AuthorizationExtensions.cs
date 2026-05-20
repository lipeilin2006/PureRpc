using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using PureRpc;
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

/// <summary>
/// 注册命名授权策略。子类可通过构造注入 <see cref="AuthorizationOptions"/> 并在
/// <see cref="AuthorizationHandlerBase.CheckPolicyAsync"/> 中按名称解析策略要求。
/// </summary>
public static IServerBuilder AddAuthorization(this IServerBuilder builder,
    Action<AuthorizationOptions> configure)
{
    var options = new AuthorizationOptions();
    configure(options);
    builder.Services.AddSingleton(options);
    return builder;
}

/// <summary>
/// 注册客户端认证拦截器，自动为出站请求注入 Authorization header。
/// </summary>
public static IClientBuilder AddClientAuthorization(this IClientBuilder builder,
    Func<string> tokenFactory, string scheme = "Bearer")
{
    builder.Services.AddSingleton<IRpcClientInterceptor>(
        _ => new ClientAuthorizationInterceptor(tokenFactory, scheme));
    return builder;
}

/// <summary>
/// 注册服务端认证拦截器，自动从请求 header 提取 token 并验证。
/// </summary>
public static IServerBuilder AddServerAuthorization(this IServerBuilder builder,
    Func<string, CancellationToken, ValueTask<ClaimsPrincipal?>> tokenValidator)
{
    builder.Services.AddSingleton<IRpcServerInterceptor>(
        _ => new ServerAuthorizationInterceptor(tokenValidator));
    return builder;
}
}
