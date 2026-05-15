namespace PureRpc.Transport.Kcp;

public class KcpServerOptions
{
    public ushort Port { get; set; } = 5000;
    public KcpConfig Config { get; set; } = new(
        SendWindowSize: 1024,
        ReceiveWindowSize: 1024,
        Timeout: 60000,
        MaxRetransmits: 100);

    /// <summary>
    /// KCP tick 循环间隔（毫秒）。默认 10ms。
    /// </summary>
    public int TickInterval { get; set; } = 10;
}
