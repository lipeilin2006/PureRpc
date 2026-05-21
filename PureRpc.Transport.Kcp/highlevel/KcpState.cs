namespace PureRpc.Transport.Kcp
{
    /// <summary>
    /// KCP 连接状态枚举 / KCP connection state enum.
    /// </summary>
    public enum KcpState
    {
        /// <summary>
        /// 已连接（等待认证握手）/ Connected (waiting for authentication handshake).
        /// </summary>
        Connected,

        /// <summary>
        /// 已认证（握手完成，可以发送数据）/ Authenticated (handshake complete, can send data).
        /// </summary>
        Authenticated,

        /// <summary>
        /// 已断开连接 / Disconnected.
        /// </summary>
        Disconnected
    }
}
