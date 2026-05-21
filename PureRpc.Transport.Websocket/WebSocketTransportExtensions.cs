using PureRpc.Abstractions;
using PureRpc.Transport.Websocket;

namespace PureRpc;

public static class WebSocketTransportExtensions
{
    public static IServerBuilder WithWebSocketTransport(
        this IServerBuilder builder,
        int port,
        Action<WebSocketServerOptions>? configure = null)
    {
        return TransportRegistration.AddServerTransport<WebSocketServerOptions, WebSocketServerTransport>(builder, options =>
        {
            options.EndPoint = new System.Net.IPEndPoint(System.Net.IPAddress.Any, port);
            configure?.Invoke(options);
        });
    }

    public static IClientBuilder WithWebSocketTransport(
        this IClientBuilder builder,
        string url,
        Action<WebSocketClientOptions>? configure = null)
    {
        return TransportRegistration.AddClientTransport<WebSocketClientOptions, WebSocketClientTransport>(builder, options =>
        {
            options.Url = url;
            configure?.Invoke(options);
        });
    }
}
