using System.Net;
using System.Security.Cryptography.X509Certificates;

namespace PureRpc.Transport.Quic;

/// <summary>
/// QUIC 服务端传输层配置选项 / QUIC server transport configuration options.
/// 配置 QUIC 监听端点、TLS 证书和流并发数等 / 
/// Configures QUIC listening endpoint, TLS certificate, and stream concurrency.
/// </summary>
public class QuicServerOptions
{
    /// <summary>
    /// 获取或设置监听端点 / Gets or sets the listening endpoint.
    /// 默认监听所有网卡的 5035 端口 / Defaults to listening on all interfaces at port 5035.
    /// </summary>
    public EndPoint ListenEndPoint { get; set; } = new IPEndPoint(IPAddress.Any, 5035);

    /// <summary>
    /// 获取或设置 TLS 服务端证书 / Gets or sets the TLS server certificate.
    /// QUIC 强制要求 TLS，此证书为必需项 / QUIC mandates TLS; this certificate is required.
    /// </summary>
    public X509Certificate2? ServerCertificate { get; set; }

    /// <summary>
    /// 获取或设置 TLS 证书文件路径 / Gets or sets the TLS certificate file path.
    /// 也可使用 <see cref="ServerCertificate"/> 直接提供证书 / 
    /// Alternatively, provide the certificate directly via <see cref="ServerCertificate"/>.
    /// </summary>
    public string? ServerCertificatePath { get; set; }

    /// <summary>
    /// 获取或设置 TLS 证书密码 / Gets or sets the TLS certificate password.
    /// </summary>
    public string? ServerCertificatePassword { get; set; }

    /// <summary>
    /// 获取或设置最大入站双向流数量 / Gets or sets the maximum number of inbound bidirectional streams.
    /// 默认值为 10000 / Defaults to 10000.
    /// </summary>
    public int MaxInboundBidirectionalStreams { get; set; } = 10000;

    /// <summary>
    /// 获取或设置等待连接队列的最大长度 / Gets or sets the maximum length of the pending connections queue.
    /// 默认值为 512 / Defaults to 512.
    /// </summary>
    public int Backlog { get; set; } = 512;
}