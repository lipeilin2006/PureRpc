using Microsoft.IdentityModel.Tokens;

namespace PureRpc.Authentication.JwtBearer;

/// <summary>
/// JWT Bearer 认证配置项。
/// 可通过 <see cref="TokenValidationParameters"/> 直接指定验证参数，
/// 也可通过 <see cref="Authority"/> + <see cref="Audience"/> + <see cref="UseAuthorityAsync"/> 自动从 OIDC 元数据获取签名密钥。
/// </summary>
public sealed class JwtBearerOptions
{
    /// <summary>
    /// 自定义 Token 验证参数。设置后 <see cref="Authority"/> 不会自动生效。
    /// </summary>
    public TokenValidationParameters? TokenValidationParameters { get; set; }

    /// <summary>
    /// OIDC 颁发机构地址（如 https://login.microsoftonline.com/{tenant}/v2.0）。
    /// 仅在未设置 <see cref="TokenValidationParameters"/> 时使用。
    /// </summary>
    public string? Authority { get; set; }

    /// <summary>
    /// 预期的 Audience（受众），与 <see cref="Authority"/> 配合使用。
    /// </summary>
    public string? Audience { get; set; }

    /// <summary>
    /// 自动从 <see cref="Authority"/> 的 OIDC 元数据端点获取签名密钥并填充 <see cref="TokenValidationParameters"/>。
    /// 调用后将 <see cref="TokenValidationParameters"/> 设置为包含正确签名密钥的实例。
    /// </summary>
    public async Task UseAuthorityAsync()
    {
        if (string.IsNullOrEmpty(Authority))
            throw new InvalidOperationException("Authority must be set before calling UseAuthorityAsync.");

        var configManager = new Microsoft.IdentityModel.Protocols.ConfigurationManager<Microsoft.IdentityModel.Protocols.OpenIdConnect.OpenIdConnectConfiguration>(
            Authority.TrimEnd('/') + "/.well-known/openid-configuration",
            new Microsoft.IdentityModel.Protocols.OpenIdConnect.OpenIdConnectConfigurationRetriever());
        var config = await configManager.GetConfigurationAsync();

        TokenValidationParameters = new TokenValidationParameters
        {
            ValidIssuer = Authority,
            ValidAudience = Audience,
            IssuerSigningKeys = config.SigningKeys,
        };
    }
}
