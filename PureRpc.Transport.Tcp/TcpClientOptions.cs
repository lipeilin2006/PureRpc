using System;
using System.Net;
using PureRpc.Abstractions;

namespace PureRpc.Transport.Tcp
{
    /// <summary>
    /// TCP 客户端传输层配置选项 / TCP client transport configuration options.
    /// 配置 TCP 连接的主机、端口、超时和缓冲区大小等 / 
    /// Configures host, port, timeout, and buffer sizes for TCP connections.
    /// </summary>
    public class TcpClientOptions
    {
        /// <summary>
        /// 获取或设置远程主机名或 IP 地址 / Gets or sets the remote hostname or IP address.
        /// 默认值为 "127.0.0.1" / Defaults to "127.0.0.1".
        /// </summary>
        public string Host { get; set; } = "127.0.0.1";

        /// <summary>
        /// 获取或设置远程端口号 / Gets or sets the remote port number.
        /// 默认值为 5000 / Defaults to 5000.
        /// </summary>
        public int Port { get; set; } = 5000;

        /// <summary>
        /// 获取或设置连接超时时间 / Gets or sets the connection timeout.
        /// 默认值为 15 秒 / Defaults to 15 seconds.
        /// </summary>
        public TimeSpan ConnectTimeout { get; set; } = TimeSpan.FromSeconds(15);

        /// <summary>
        /// 获取或设置是否启用 TCP NoDelay（禁用 Nagle 算法） / 
        /// Gets or sets whether to enable TCP NoDelay (disable Nagle's algorithm).
        /// RPC 场景下建议开启以降低延迟 / Recommended to enable in RPC scenarios to reduce latency.
        /// 默认值为 true / Defaults to true.
        /// </summary>
        public bool NoDelay { get; set; } = true;

        /// <summary>
        /// 获取或设置发送缓冲区大小（字节） / Gets or sets the send buffer size in bytes.
        /// 默认值为 64KB / Defaults to 64KB.
        /// </summary>
        public int SendBufferSize { get; set; } = 64 * 1024;

        /// <summary>
        /// 获取或设置接收缓冲区大小（字节） / Gets or sets the receive buffer size in bytes.
        /// 默认值为 64KB / Defaults to 64KB.
        /// </summary>
        public int ReceiveBufferSize { get; set; } = 64 * 1024;

        /// <summary>
        /// 根据 Host 和 Port 解析远程端点 / Resolves the remote endpoint from Host and Port.
        /// 如果 Host 是合法 IP 地址返回 IPEndPoint，否则返回 DnsEndPoint / 
        /// Returns IPEndPoint if Host is a valid IP address; otherwise returns DnsEndPoint.
        /// </summary>
        public EndPoint RemoteEndPoint => EndPointHelper.ResolveEndPoint(Host, Port);

        /// <summary>
        /// 获取或设置 TLS 目标主机名 / Gets or sets the TLS target host name.
        /// 设置后将启用 TLS/SSL 连接 / When set, TLS/SSL connection will be enabled.
        /// 默认值为 null（不启用 TLS）/ Defaults to null (TLS not enabled).
        /// </summary>
        public string? TargetHost { get; set; }
    }
}