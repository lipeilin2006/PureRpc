namespace PureRpc.Transport.Kcp;

/// <summary>
/// KCP 服务端传输层配置选项 / KCP server transport configuration options.
/// 配置 KCP 监听端口和 KCP 参数 / Configures KCP listening port and KCP parameters.
/// </summary>
public class KcpServerOptions
{
    /// <summary>
    /// 获取或设置监听端口号 / Gets or sets the listening port number.
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