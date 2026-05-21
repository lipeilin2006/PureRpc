namespace PureRpc.Transport.Kcp
{
    /// <summary>
    /// KCP 通道类型枚举 / KCP channel type enum.
    /// 定义消息的传输通道类型 / Defines the transmission channel type for messages.
    /// </summary>
    public enum KcpChannel : byte
    {
        /// <summary>
        /// 可靠通道（KCP 保证顺序和重传）/ Reliable channel (KCP guarantees ordering and retransmission).
        /// </summary>
        Reliable   = 1,

        /// <summary>
        /// 不可靠通道（UDP 级别，不保证送达）/ Unreliable channel (UDP-level, no delivery guarantee).
        /// </summary>
        Unreliable = 2
    }
}
