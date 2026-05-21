using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using PureRpc.Abstractions;
using PureRpc.Authentication.JwtBearer;

namespace PureRpc;

/// <summary>
/// JWT Bearer 认证扩展方法 / JWT Bearer authentication extension methods.
/// 提供简洁的 DSL 将 JWT Bearer 认证集成到 PureRpc 服务端 / 
/// Provides a concise DSL for integrating JWT Bearer authentication into PureRpc servers.
/// </summary>
public static class JwtBearerAuthenticationExtensions
{
    /// <summary>
    /// 使用配置委托注册 JWT Bearer 认证 / Registers JWT Bearer authentication with a configuration delegate.
    /// 自动创建 <see cref="ServerAuthorizationInterceptor"/> 并从请求头提取 Bearer token 进行验证 / 
    /// Automatically creates a <see cref="ServerAuthorizationInterceptor"/> that extracts Bearer tokens from request headers and validates them.
    /// </summary>
    /// <param name="builder">服务端构建器 / The server builder.</param>
    /// <param name="configureOptions">JWT Bearer 选项配置委托 / JWT Bearer options configuration delegate.</param>
    /// <returns>服务端构建器（支持链式调用） / The server builder (supports fluent chaining).</returns>
    public static IServerBuilder AddJwtBearerAuthentication(
        this IServerBuilder builder,
        Action<JwtBearerOptions> configureOptions)
    {
        builder.Services.Configure(configureOptions);
        builder.Services.AddSingleton<IRpcServerInterceptor>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<JwtBearerOptions>>().Value;
            var validationParameters = options.TokenValidationParameters
                ?? throw new InvalidOperationException(
                    "JwtBearerOptions.TokenValidationParameters must be configured. " +
                    "Call AddJwtBearerAuthentication(options => { ... }) or use JwtBearerOptions.UseAuthorityAsync().");
            var handler = new JwtSecurityTokenHandler();

            return new ServerAuthorizationInterceptor(async (authHeader, ct) =>
            {
                if (!authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                    return null;
                var token = authHeader["Bearer ".Length..];
                try
                {
                    var result = await handler.ValidateTokenAsync(token, validationParameters);
                    if (result.IsValid && result.ClaimsIdentity != null)
                        return new ClaimsPrincipal(result.ClaimsIdentity);
                }
                catch { }
                return null;
            });
        });
        return builder;
    }

    /// <summary>
    /// 使用预配置的选项实例注册 JWT Bearer 认证 / Registers JWT Bearer authentication with a pre-configured options instance.
    /// </summary>
    /// <param name="builder">服务端构建器 / The server builder.</param>
    /// <param name="options">预配置的 JWT Bearer 选项实例 / Pre-configured JWT Bearer options instance.</param>
    /// <returns>服务端构建器（支持链式调用） / The server builder (supports fluent chaining).</returns>
    public static IServerBuilder AddJwtBearerAuthentication(
        this IServerBuilder builder,
        JwtBearerOptions options)
    {
        builder.Services.AddSingleton(Options.Create(options));
        builder.Services.AddSingleton<IRpcServerInterceptor>(sp =>
        {
            var validationParameters = options.TokenValidationParameters
                ?? throw new InvalidOperationException(
                    "JwtBearerOptions.TokenValidationParameters must be configured.");
            var handler = new JwtSecurityTokenHandler();

            return new ServerAuthorizationInterceptor(async (authHeader, ct) =>
            {
                if (!authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                    return null;
                var token = authHeader["Bearer ".Length..];
                try
                {
                    var result = await handler.ValidateTokenAsync(token, validationParameters);
                    if (result.IsValid && result.ClaimsIdentity != null)
                        return new ClaimsPrincipal(result.ClaimsIdentity);
                }
                catch { }
                return null;
            });
        });
        return builder;
    }
}