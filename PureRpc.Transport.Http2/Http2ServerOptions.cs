namespace PureRpc.Transport.Http2;

/// <summary>
/// HTTP/2 服务端传输层配置选项 / HTTP/2 server transport configuration options.
/// 配置 HTTP/2 监听端口和 TLS 证书 / Configures HTTP/2 listening port and TLS certificates.
/// </summary>
public class Http2ServerOptions
{
    /// <summary>
    /// 获取或设置监听端口号 / Gets or sets the listening port number.
    /// 默认值为 5001 / Defaults to 5001.
    /// </summary>
    public int Port { get; set; } = 5001;

    /// <summary>
    /// 获取或设置 TLS 证书文件路径 / Gets or sets the TLS certificate file path.
    /// 使用 TLS 时必须配置此属性或 <see cref="TlsKeyPath"/> / 
    /// Must be configured with <see cref="TlsKeyPath"/> when using TLS.
    /// </summary>
    public string? TlsCertPath { get; set; }

    /// <summary>
    /// 获取或设置 TLS 密钥文件路径 / Gets or sets the TLS key file path.
    /// 使用 TLS 时必须配置此属性或 <see cref="TlsCertPath"/> / 
    /// Must be configured with <see cref="TlsCertPath"/> when using TLS.
    /// </summary>
    public string? TlsKeyPath { get; set; }
}