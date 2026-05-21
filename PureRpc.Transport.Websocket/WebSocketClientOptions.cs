namespace PureRpc.Transport.Websocket;

/// <summary>
/// WebSocket 客户端传输层配置选项 / WebSocket client transport configuration options.
/// 配置 WebSocket 连接的 URL 和超时 / Configures the WebSocket connection URL and timeout.
/// </summary>
public class WebSocketClientOptions
{
    /// <summary>
    /// 获取或设置 WebSocket 服务器 URL / Gets or sets the WebSocket server URL.
    /// 默认值为 "ws://127.0.0.1:5000/rpc" / Defaults to "ws://127.0.0.1:5000/rpc".
    /// </summary>
    public string Url { get; set; } = "ws://127.0.0.1:5000/rpc";

    /// <summary>
    /// 获取或设置连接超时时间（毫秒） / Gets or sets the connection timeout in milliseconds.
    /// 默认值为 15000 毫秒 / Defaults to 15000 milliseconds.
    /// </summary>
    public int ConnectTimeoutMs { get; set; } = 15000;
}