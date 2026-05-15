using System.Net.Http;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;

namespace PureRpc.Transport.Http2;

public class Http2ClientOptions
{
    public string Url { get; set; } = "http://127.0.0.1:5001/rpc";
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);
    /// <summary>自定义服务端证书验证。不设置则接受任意证书（开发环境用，生产环境务必配置）。</summary>
    public Func<HttpRequestMessage, X509Certificate2?, X509Chain?, SslPolicyErrors, bool>? ServerCertificateValidation { get; set; }
}
