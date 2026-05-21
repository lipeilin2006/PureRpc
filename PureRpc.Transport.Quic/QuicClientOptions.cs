using System;
using System.Net;
using System.Net.Security;
using PureRpc.Abstractions;

namespace PureRpc.Transport.Quic;

/// <summary>
/// QUIC 客户端传输层配置选项 / QUIC client transport configuration options.
/// 配置 QUIC 连接的主机、端口、超时和证书验证等 / 
/// Configures host, port, timeout, and certificate validation for QUIC connections.
/// </summary>
public class QuicClientOptions
{
    /// <summary>
    /// 获取或设置远程主机名或 IP 地址 / Gets or sets the remote hostname or IP address.
    /// 默认值为 "127.0.0.1" / Defaults to "127.0.0.1".
    /// </summary>
    public string Host { get; set; } = "127.0.0.1";

    /// <summary>
    /// 获取或设置远程端口号 / Gets or sets the remote port number.
    /// 默认值为 5035 / Defaults to 5035.
    /// </summary>
    public int Port { get; set; } = 5035;

    /// <summary>
    /// 获取或设置连接超时时间 / Gets or sets the connection timeout.
    /// 默认值为 15 秒 / Defaults to 15 seconds.
    /// </summary>
    public TimeSpan ConnectTimeout { get; set; } = TimeSpan.FromSeconds(15);

    /// <summary>
    /// 获取或设置远程证书验证回调 / Gets or sets the remote certificate validation callback.
    /// 不设置时默认信任所有证书 / When not set, all certificates are trusted by default.
    /// </summary>
    public RemoteCertificateValidationCallback? CertificateValidationCallback { get; set; }

    /// <summary>
    /// 根据 Host 和 Port 解析远程端点 / Resolves the remote endpoint from Host and Port.
    /// </summary>
    public EndPoint RemoteEndPoint => EndPointHelper.ResolveEndPoint(Host, Port);
}