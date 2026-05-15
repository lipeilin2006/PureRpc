using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
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
        builder.Services.Configure<WebSocketServerOptions>(options =>
        {
            options.EndPoint = new System.Net.IPEndPoint(System.Net.IPAddress.Any, port);
            configure?.Invoke(options);
        });
        builder.Services.TryAddSingleton<IServerTransport, WebSocketServerTransport>();
        return builder;
    }

    public static IClientBuilder WithWebSocketTransport(
        this IClientBuilder builder,
        string url,
        Action<WebSocketClientOptions>? configure = null)
    {
        builder.Services.Configure<WebSocketClientOptions>(options =>
        {
            options.Url = url;
            configure?.Invoke(options);
        });
        builder.Services.TryAddSingleton<IClientTransport, WebSocketClientTransport>();
        return builder;
    }
}
