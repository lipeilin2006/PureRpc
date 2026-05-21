namespace PureRpc.Transport.Kcp
{
    /// <summary>
    /// KCP 错误类型 / KCP error types.
    /// </summary>
    public enum ErrorCode : byte
    {
        /// <summary>
        /// DNS 解析失败 / DNS resolution failed.
        /// </summary>
        DnsResolve,

        /// <summary>
        /// Ping 超时或死连接 / Ping timeout or dead link.
        /// </summary>
        Timeout,

        /// <summary>
        /// 拥塞，消息超过传输层/网络处理能力 / Congestion, more messages than transport/network can handle.
        /// </summary>
        Congestion,

        /// <summary>
        /// 接收到无效数据包（可能是攻击） / Received invalid packet (possibly intentional attack).
        /// </summary>
        InvalidReceive,

        /// <summary>
        /// 用户尝试发送无效数据 / User tried to send invalid data.
        /// </summary>
        InvalidSend,

        /// <summary>
        /// 连接被主动关闭或意外丢失 / Connection closed voluntarily or lost involuntarily.
        /// </summary>
        ConnectionClosed,

        /// <summary>
        /// 意外错误/异常，需要修复 / Unexpected error/exception, requires fix.
        /// </summary>
        Unexpected
    }
}
