using PureRpc.Abstractions;
using PureRpc.Transport.Websocket;

namespace PureRpc;

/// <summary>
/// WebSocket 传输层注册扩展方法 / WebSocket transport registration extension methods.
/// 提供简洁的 DSL 来配置 WebSocket 传输层 / Provides a concise DSL for configuring WebSocket transport.
/// </summary>
public static class WebSocketTransportExtensions
{
    /// <summary>
    /// 为服务端配置 WebSocket 传输层 / Configures WebSocket transport for the server.
    /// </summary>
    /// <param name="builder">服务端构建器 / The server builder.</param>
    /// <param name="port">监听端口号 / The listening port number.</param>
    /// <param name="configure">可选的 WebSocket 服务端选项配置委托 / Optional WebSocket server options configuration delegate.</param>
    /// <returns>服务端构建器（支持链式调用） / The server builder (supports fluent chaining).</returns>
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

    /// <summary>
    /// 为客户端配置 WebSocket 传输层 / Configures WebSocket transport for the client.
    /// </summary>
    /// <param name="builder">客户端构建器 / The client builder.</param>
    /// <param name="url">WebSocket 服务器 URL / The WebSocket server URL.</param>
    /// <param name="configure">可选的 WebSocket 客户端选项配置委托 / Optional WebSocket client options configuration delegate.</param>
    /// <returns>客户端构建器（支持链式调用） / The client builder (supports fluent chaining).</returns>
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