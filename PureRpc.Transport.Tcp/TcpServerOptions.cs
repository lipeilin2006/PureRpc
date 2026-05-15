using System;
using System.Net;

namespace PureRpc.Transport.Tcp
{
    /// <summary>
    /// 配置 PureRpc TCP 服务端的传输层参数。
    /// </summary>
    public class TcpServerOptions
    {
        /// <summary>
        /// 服务端监听的网络端点。默认监听所有网卡的 5000 端口。
        /// </summary>
        public EndPoint EndPoint { get; set; } = new IPEndPoint(IPAddress.Any, 5000);

        /// <summary>
        /// 等待连接队列的最大长度（对应 Socket 的 backlog）。
        /// </summary>
        public int Backlog { get; set; } = 1000;

        /// <summary>
        /// 是否启用 TCP NoDelay (禁用 Nagle 算法)。
        /// RPC 场景下建议开启以降低指令延迟。
        /// </summary>
        public bool NoDelay { get; set; } = true;

        /// <summary>
        /// 每个连接的接收缓冲区大小。
        /// </summary>
        public int ReceiveBufferSize { get; set; } = 64 * 1024; // 64KB

        /// <summary>
        /// 每个连接的发送缓冲区大小。
        /// </summary>
        public int SendBufferSize { get; set; } = 64 * 1024; // 64KB

        /// <summary>
        /// 允许的最大并发连接数。超出时将拒绝新连接。
        /// </summary>
        public int MaxConnections { get; set; } = 10000;

        /// <summary>
        /// 获取或设置是否在监听端口上启用地址复用。
        /// </summary>
        public bool ReuseAddress { get; set; } = true;

        /// <summary>
        /// 启用 TLS 时使用的服务端证书。设置此值将自动开启 TLS。
        /// </summary>
        public System.Security.Cryptography.X509Certificates.X509Certificate2? ServerCertificate { get; set; }
    }
}