using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using PureRpc.Abstractions;
using PureRpc.Transport.Tcp;
using System.Net;

namespace PureRpc;

/// <summary>
/// TCP 传输层注册扩展方法 / TCP transport registration extension methods.
/// 提供简洁的 DSL 来配置 TCP 传输层 / Provides a concise DSL for configuring TCP transport.
/// </summary>
public static class TcpTransportExtensions
{
    /// <summary>
    /// 为服务端配置 TCP 传输层 / Configures TCP transport for the server.
    /// </summary>
    /// <param name="builder">服务端构建器 / The server builder.</param>
    /// <param name="port">监听端口号 / The listening port number.</param>
    /// <param name="configure">可选的 TCP 服务端选项配置委托 / Optional TCP server options configuration delegate.</param>
    /// <returns>服务端构建器（支持链式调用） / The server builder (supports fluent chaining).</returns>
    public static IServerBuilder WithTcpTransport(
        this IServerBuilder builder,
        int port,
        Action<TcpServerOptions>? configure = null)
    {
        return TransportRegistration.AddServerTransport<TcpServerOptions, TcpServerTransport>(builder, options =>
        {
            options.EndPoint = new IPEndPoint(IPAddress.Any, port);
            configure?.Invoke(options);
        });
    }

    /// <summary>
    /// 为客户端配置 TCP 传输层 / Configures TCP transport for the client.
    /// </summary>
    /// <param name="builder">客户端构建器 / The client builder.</param>
    /// <param name="host">远程主机名或 IP 地址 / Remote hostname or IP address.</param>
    /// <param name="port">远程端口号 / Remote port number.</param>
    /// <param name="configure">可选的 TCP 客户端选项配置委托 / Optional TCP client options configuration delegate.</param>
    /// <returns>客户端构建器（支持链式调用） / The client builder (supports fluent chaining).</returns>
    public static IClientBuilder WithTcpTransport(
        this IClientBuilder builder,
        string host,
        int port,
        Action<TcpClientOptions>? configure = null)
    {
        return TransportRegistration.AddClientTransport<TcpClientOptions, TcpClientTransport>(builder, options =>
        {
            options.Host = host;
            options.Port = port;
            configure?.Invoke(options);
        });
    }

    /// <summary>
    /// 为服务端启用 TLS / Enables TLS for the server.
    /// 设置后将使用指定证书进行 TLS 加密 / When set, TLS encryption will use the specified certificate.
    /// </summary>
    /// <param name="builder">服务端构建器 / The server builder.</param>
    /// <param name="certificate">服务端 TLS 证书 / The server TLS certificate.</param>
    /// <returns>服务端构建器（支持链式调用） / The server builder (supports fluent chaining).</returns>
    /// <exception cref="ArgumentNullException">当 <paramref name="certificate"/> 为 null 时抛出 / Thrown when <paramref name="certificate"/> is null.</exception>
    public static IServerBuilder WithTls(this IServerBuilder builder, System.Security.Cryptography.X509Certificates.X509Certificate2 certificate)
    {
        builder.Services.PostConfigure<TcpServerOptions>(options =>
        {
            options.ServerCertificate = certificate ?? throw new ArgumentNullException(nameof(certificate));
        });
        return builder;
    }

    /// <summary>
    /// 为客户端启用 TLS / Enables TLS for the client.
    /// 设置后将使用指定目标主机名进行 TLS 验证 / When set, TLS validation will use the specified target host name.
    /// </summary>
    /// <param name="builder">客户端构建器 / The client builder.</param>
    /// <param name="targetHost">TLS 目标主机名 / The TLS target host name.</param>
    /// <returns>客户端构建器（支持链式调用） / The client builder (supports fluent chaining).</returns>
    /// <exception cref="ArgumentNullException">当 <paramref name="targetHost"/> 为 null 时抛出 / Thrown when <paramref name="targetHost"/> is null.</exception>
    public static IClientBuilder WithTls(this IClientBuilder builder, string targetHost)
    {
        builder.Services.PostConfigure<TcpClientOptions>(options =>
        {
            options.TargetHost = targetHost ?? throw new ArgumentNullException(nameof(targetHost));
        });
        return builder;
    }
}