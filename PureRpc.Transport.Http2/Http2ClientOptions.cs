using System.Net.Http;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;

namespace PureRpc.Transport.Http2;

/// <summary>
/// HTTP/2 客户端传输层配置选项 / HTTP/2 client transport configuration options.
/// 配置 HTTP/2 连接的 URL、超时和证书验证等 / 
/// Configures URL, timeout, and certificate validation for HTTP/2 connections.
/// </summary>
public class Http2ClientOptions
{
    /// <summary>
    /// 获取或设置 HTTP/2 服务器 URL / Gets or sets the HTTP/2 server URL.
    /// 默认值为 "http://127.0.0.1:5001/rpc" / Defaults to "http://127.0.0.1:5001/rpc".
    /// </summary>
    public string Url { get; set; } = "http://127.0.0.1:5001/rpc";

    /// <summary>
    /// 获取或设置请求超时时间 / Gets or sets the request timeout.
    /// 默认值为 30 秒 / Defaults to 30 seconds.
    /// </summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// 自定义服务端证书验证回调 / Custom server certificate validation callback.
    /// 不设置则接受任意证书（开发环境用，生产环境务必配置） / 
    /// When not set, accepts any certificate (for development; must be configured in production).
    /// </summary>
    public Func<HttpRequestMessage, X509Certificate2?, X509Chain?, SslPolicyErrors, bool>? ServerCertificateValidation { get; set; }
}