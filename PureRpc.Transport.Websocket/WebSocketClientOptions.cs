namespace PureRpc.Transport.Websocket;

public class WebSocketClientOptions
{
    public string Url { get; set; } = "ws://127.0.0.1:5000/rpc";
    public int ConnectTimeoutMs { get; set; } = 15000;
}
