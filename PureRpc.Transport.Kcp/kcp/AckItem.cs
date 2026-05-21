namespace PureRpc.Transport.Kcp
{
    /// <summary>
    /// KCP 确认项结构体，用于记录已接收消息的序列号和时间戳 / 
    /// KCP acknowledgment item struct, recording the serial number and timestamp of received messages.
    /// </summary>
    internal struct AckItem
    {
        /// <summary>
        /// 消息序列号 / Message serial number.
        /// </summary>
        internal uint serialNumber;

        /// <summary>
        /// 消息时间戳 / Message timestamp.
        /// </summary>
        internal uint timestamp;
    }
}
