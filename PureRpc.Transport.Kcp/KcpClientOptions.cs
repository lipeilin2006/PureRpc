namespace PureRpc.Transport.Kcp;

/// <summary>
/// KCP 客户端传输层配置选项 / KCP client transport configuration options.
/// 配置 KCP 连接的主机、端口和 KCP 参数 / 
/// Configures host, port, and KCP parameters for KCP connections.
/// </summary>
public class KcpClientOptions
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
    public ushort Port { get; set; } = 5000;

    /// <summary>
    /// 获取或设置 KCP 配置参数 / Gets or sets the KCP configuration parameters.
    /// </summary>
    public KcpConfig Config { get; set; } = new(
        SendWindowSize: 1024,
        ReceiveWindowSize: 1024,
        Timeout: 60000,
        MaxRetransmits: 100);

    /// <summary>
    /// KCP tick 循环间隔（毫秒）。默认 10ms / 
    /// KCP tick loop interval in milliseconds. Defaults to 10ms.
    /// </summary>
    public int TickInterval { get; set; } = 10;
}