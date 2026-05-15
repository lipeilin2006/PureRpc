using System;
using System.Net;

namespace PureRpc.Transport.Tcp
{
    public class TcpClientOptions
    {
        /// <summary>
        /// 远程服务器的地址（支持主机名或 IP）。
        /// </summary>
        public string Host { get; set; } = "127.0.0.1";

        /// <summary>
        /// 远程服务器的端口。
        /// </summary>
        public int Port { get; set; } = 5000;

        /// <summary>
        /// 连接建立的超时时间。
        /// </summary>
        public TimeSpan ConnectTimeout { get; set; } = TimeSpan.FromSeconds(15);

        /// <summary>
        /// 是否启用 TCP NoDelay (禁用 Nagle 算法)。
        /// 对于 RPC 这种小包高频场景，通常设置为 true 以降低延迟。
        /// </summary>
        public bool NoDelay { get; set; } = true;

        /// <summary>
        /// 发送缓冲区大小。
        /// </summary>
        public int SendBufferSize { get; set; } = 64 * 1024; // 64KB

        /// <summary>
        /// 接收缓冲区大小。
        /// </summary>
        public int ReceiveBufferSize { get; set; } = 64 * 1024; // 64KB

        /// <summary>
        /// 解析后的远程端点。
        /// </summary>
        public EndPoint RemoteEndPoint =>
            IPAddress.TryParse(Host, out var ip)
                ? new IPEndPoint(ip, Port)
                : new DnsEndPoint(Host, Port);

        /// <summary>
        /// TLS 目标主机名（SNI）。设置此值将自动开启 TLS。
        /// 通常与服务端证书的 CN 或 SAN 匹配。
        /// </summary>
        public string? TargetHost { get; set; }
    }
}