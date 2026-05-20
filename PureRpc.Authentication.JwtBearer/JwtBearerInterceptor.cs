using System.Buffers;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using PureRpc.Abstractions;

namespace PureRpc.Authentication.JwtBearer;

/// <summary>
/// 服务端拦截器：从 Authorization header 提取 Bearer token，
/// 验证 JWT 后设置 <see cref="RpcContext.User"/>。
/// 后续的 <see cref="IAuthorizationHandler.AuthorizeAsync"/> 可直接检查该用户主体。
/// </summary>
public sealed class JwtBearerInterceptor : IRpcServerInterceptor
{
    private readonly TokenValidationParameters _validationParameters;
    private readonly JwtSecurityTokenHandler _handler = new();

    public JwtBearerInterceptor(IOptions<JwtBearerOptions> options)
    {
        var jwtOptions = options.Value;
        _validationParameters = jwtOptions.TokenValidationParameters
            ?? throw new InvalidOperationException(
                "JwtBearerOptions.TokenValidationParameters must be configured. " +
                "Call AddJwtBearerAuthentication(options => { ... }) or use JwtBearerOptions.UseAuthorityAsync().");
    }

    public async ValueTask InvokeAsync(RpcContext context, ReadOnlySequence<byte> payload, RpcRequestDelegate next)
    {
        if (context.Headers.TryGetValue("Authorization", out var authHeader) &&
            authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            var token = authHeader["Bearer ".Length..];

            try
            {
                var result = await _handler.ValidateTokenAsync(token, _validationParameters);
                if (result.IsValid && result.ClaimsIdentity != null)
                {
                    context.User = new ClaimsPrincipal(result.ClaimsIdentity);
                }
            }
            catch
            {
                // token 无效，context.User 保持 null，
                // 后续 AuthorizationHandler 会抛出 UnauthorizedAccessException
            }
        }

        await next(context, payload);
    }
}
