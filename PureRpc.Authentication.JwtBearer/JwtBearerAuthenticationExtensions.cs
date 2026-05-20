using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using PureRpc.Abstractions;
using PureRpc.Authentication.JwtBearer;

namespace PureRpc;

/// <summary>
/// JWT Bearer 认证扩展方法，用于在服务端注册 JWT 验证拦截器。
/// </summary>
public static class JwtBearerAuthenticationExtensions
{
    /// <summary>
    /// 注册 JWT Bearer 认证拦截器。拦截器会从请求 Authorization header 提取并验证 Bearer token，
    /// 验证通过后设置 <see cref="RpcContext.User"/>。
    /// </summary>
    /// <param name="builder">服务端构建器</param>
    /// <param name="configureOptions">配置 JwtBearerOptions 的委托</param>
    public static IServerBuilder AddJwtBearerAuthentication(
        this IServerBuilder builder,
        Action<JwtBearerOptions> configureOptions)
    {
        builder.Services.Configure(configureOptions);
        builder.Services.AddSingleton<IRpcServerInterceptor, JwtBearerInterceptor>();
        return builder;
    }

    /// <summary>
    /// 注册 JWT Bearer 认证拦截器（通过已配置的 <see cref="JwtBearerOptions"/> 实例）。
    /// </summary>
    public static IServerBuilder AddJwtBearerAuthentication(
        this IServerBuilder builder,
        JwtBearerOptions options)
    {
        builder.Services.AddSingleton(Options.Create(options));
        builder.Services.AddSingleton<IRpcServerInterceptor, JwtBearerInterceptor>();
        return builder;
    }
}
