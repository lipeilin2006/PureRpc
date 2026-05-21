using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using PureRpc.Abstractions;
using PureRpc.Authentication.JwtBearer;

namespace PureRpc;

public static class JwtBearerAuthenticationExtensions
{
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
