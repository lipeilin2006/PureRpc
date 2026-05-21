namespace PureRpc;

/// <summary>
/// 授权配置选项 / Authorization configuration options.
/// 用于配置默认授权策略名称 / Used to configure the default authorization policy name.
/// </summary>
public sealed class AuthorizationOptions
{
    /// <summary>
    /// 获取或设置默认授权策略名称 / Gets or sets the default authorization policy name.
    /// </summary>
    public string? DefaultPolicy { get; set; }
}