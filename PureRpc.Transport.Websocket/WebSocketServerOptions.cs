using System.Net;

namespace PureRpc.Transport.Websocket;

public class WebSocketServerOptions
{
    public EndPoint EndPoint { get; set; } = new IPEndPoint(IPAddress.Any, 5000);
    public string Path { get; set; } = "/rpc";
}
