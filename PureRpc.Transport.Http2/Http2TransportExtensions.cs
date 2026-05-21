using PureRpc.Abstractions;
using PureRpc.Transport.Http2;

namespace PureRpc;

/// <summary>
/// HTTP/2 传输层注册扩展方法 / HTTP/2 transport registration extension methods.
/// 提供简洁的 DSL 来配置 HTTP/2 传输层 / Provides a concise DSL for configuring HTTP/2 transport.
/// </summary>
public static class Http2TransportExtensions
{
    /// <summary>
    /// 为服务端配置 HTTP/2 传输层 / Configures HTTP/2 transport for the server.
    /// </summary>
    /// <param name="builder">服务端构建器 / The server builder.</param>
    /// <param name="port">监听端口号 / The listening port number.</param>
    /// <param name="configure">可选的 HTTP/2 服务端选项配置委托 / Optional HTTP/2 server options configuration delegate.</param>
    /// <returns>服务端构建器（支持链式调用） / The server builder (supports fluent chaining).</returns>
    public static IServerBuilder WithHttp2Transport(this IServerBuilder builder, int port,
        Action<Http2ServerOptions>? configure = null)
    {
        return TransportRegistration.AddServerTransport<Http2ServerOptions, Http2ServerTransport>(builder, options =>
        {
            options.Port = port;
            configure?.Invoke(options);
        });
    }

    /// <summary>
    /// 为客户端配置 HTTP/2 传输层 / Configures HTTP/2 transport for the client.
    /// </summary>
    /// <param name="builder">客户端构建器 / The client builder.</param>
    /// <param name="url">HTTP/2 服务器 URL / The HTTP/2 server URL.</param>
    /// <param name="configure">可选的 HTTP/2 客户端选项配置委托 / Optional HTTP/2 client options configuration delegate.</param>
    /// <returns>客户端构建器（支持链式调用） / The client builder (supports fluent chaining).</returns>
    public static IClientBuilder WithHttp2Transport(this IClientBuilder builder, string url,
        Action<Http2ClientOptions>? configure = null)
    {
        return TransportRegistration.AddClientTransport<Http2ClientOptions, Http2ClientTransport>(builder, options =>
        {
            options.Url = url;
            configure?.Invoke(options);
        });
    }
}