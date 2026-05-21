using System.Net;

namespace PureRpc.Transport.Websocket;

/// <summary>
/// WebSocket 服务端传输层配置选项 / WebSocket server transport configuration options.
/// 配置 WebSocket 监听端点和路径 / Configures the WebSocket listening endpoint and path.
/// </summary>
public class WebSocketServerOptions
{
    /// <summary>
    /// 获取或设置监听端点 / Gets or sets the listening endpoint.
    /// 默认监听所有网卡的 5000 端口 / Defaults to listening on all interfaces at port 5000.
    /// </summary>
    public EndPoint EndPoint { get; set; } = new IPEndPoint(IPAddress.Any, 5000);

    /// <summary>
    /// 获取或设置 WebSocket 路径 / Gets or sets the WebSocket path.
    /// 默认值为 "/rpc" / Defaults to "/rpc".
    /// </summary>
    public string Path { get; set; } = "/rpc";
}