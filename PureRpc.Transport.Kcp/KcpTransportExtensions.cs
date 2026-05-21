using PureRpc.Abstractions;
using PureRpc.Transport.Kcp;

namespace PureRpc;

/// <summary>
/// KCP 传输层注册扩展方法 / KCP transport registration extension methods.
/// 提供简洁的 DSL 来配置 KCP 传输层 / Provides a concise DSL for configuring KCP transport.
/// </summary>
public static class KcpTransportExtensions
{
    /// <summary>
    /// 为服务端配置 KCP 传输层 / Configures KCP transport for the server.
    /// </summary>
    /// <param name="builder">服务端构建器 / The server builder.</param>
    /// <param name="port">监听端口号 / The listening port number.</param>
    /// <param name="configure">可选的 KCP 服务端选项配置委托 / Optional KCP server options configuration delegate.</param>
    /// <returns>服务端构建器（支持链式调用） / The server builder (supports fluent chaining).</returns>
    public static IServerBuilder WithKcpTransport(
        this IServerBuilder builder,
        ushort port,
        Action<KcpServerOptions>? configure = null)
    {
        return TransportRegistration.AddServerTransport<KcpServerOptions, KcpServerTransport>(builder, options =>
        {
            options.Port = port;
            configure?.Invoke(options);
        });
    }

    /// <summary>
    /// 为客户端配置 KCP 传输层 / Configures KCP transport for the client.
    /// </summary>
    /// <param name="builder">客户端构建器 / The client builder.</param>
    /// <param name="host">远程主机名或 IP 地址 / Remote hostname or IP address.</param>
    /// <param name="port">远程端口号 / Remote port number.</param>
    /// <param name="configure">可选的 KCP 客户端选项配置委托 / Optional KCP client options configuration delegate.</param>
    /// <returns>客户端构建器（支持链式调用） / The client builder (supports fluent chaining).</returns>
    public static IClientBuilder WithKcpTransport(
        this IClientBuilder builder,
        string host,
        ushort port,
        Action<KcpClientOptions>? configure = null)
    {
        return TransportRegistration.AddClientTransport<KcpClientOptions, KcpClientTransport>(builder, options =>
        {
            options.Host = host;
            options.Port = port;
            configure?.Invoke(options);
        });
    }
}