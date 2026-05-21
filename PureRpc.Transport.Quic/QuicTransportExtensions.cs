using PureRpc.Abstractions;
using PureRpc.Transport.Quic;

namespace PureRpc;

/// <summary>
/// QUIC 传输层注册扩展方法 / QUIC transport registration extension methods.
/// 提供简洁的 DSL 来配置 QUIC 传输层 / Provides a concise DSL for configuring QUIC transport.
/// </summary>
public static class QuicTransportExtensions
{
    /// <summary>
    /// 为服务端配置 QUIC 传输层 / Configures QUIC transport for the server.
    /// </summary>
    /// <param name="builder">服务端构建器 / The server builder.</param>
    /// <param name="port">监听端口号 / The listening port number.</param>
    /// <param name="configure">可选的 QUIC 服务端选项配置委托 / Optional QUIC server options configuration delegate.</param>
    /// <returns>服务端构建器（支持链式调用） / The server builder (supports fluent chaining).</returns>
    public static IServerBuilder WithQuicTransport(this IServerBuilder builder, int port,
        Action<QuicServerOptions>? configure = null)
    {
        return TransportRegistration.AddServerTransport<QuicServerOptions, QuicServerTransport>(builder, options =>
        {
            options.ListenEndPoint = new System.Net.IPEndPoint(System.Net.IPAddress.Any, port);
            configure?.Invoke(options);
        });
    }

    /// <summary>
    /// 为客户端配置 QUIC 传输层 / Configures QUIC transport for the client.
    /// </summary>
    /// <param name="builder">客户端构建器 / The client builder.</param>
    /// <param name="host">远程主机名或 IP 地址 / Remote hostname or IP address.</param>
    /// <param name="port">远程端口号 / Remote port number.</param>
    /// <param name="configure">可选的 QUIC 客户端选项配置委托 / Optional QUIC client options configuration delegate.</param>
    /// <returns>客户端构建器（支持链式调用） / The client builder (supports fluent chaining).</returns>
    public static IClientBuilder WithQuicTransport(this IClientBuilder builder, string host, int port,
        Action<QuicClientOptions>? configure = null)
    {
        return TransportRegistration.AddClientTransport<QuicClientOptions, QuicClientTransport>(builder, options =>
        {
            options.Host = host;
            options.Port = port;
            configure?.Invoke(options);
        });
    }
}