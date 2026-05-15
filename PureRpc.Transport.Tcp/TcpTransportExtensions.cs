using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PureRpc.Abstractions;
using PureRpc.Transport.Tcp;
using System.Net;

namespace PureRpc;

public static class TcpTransportExtensions
{
    /// <summary>
    /// 为服务端配置 TCP 传输层。
    /// </summary>
    public static IServerBuilder WithTcpTransport(
        this IServerBuilder builder,
        int port,
        Action<TcpServerOptions>? configure = null)
    {
        // 使用标准的 Options 配置模式，支持 IOptions<TcpServerOptions> 注入
        builder.Services.Configure<TcpServerOptions>(options =>
        {
            options.EndPoint = new IPEndPoint(IPAddress.Any, port);
            configure?.Invoke(options);
        });

        // 使用 TryAdd 避免在复杂项目中重复注册多个传输层冲突
        builder.Services.TryAddSingleton<IServerTransport, TcpServerTransport>();

        return builder;
    }

    /// <summary>
    /// 为客户端配置 TCP 传输层。
    /// </summary>
    public static IClientBuilder WithTcpTransport(
        this IClientBuilder builder,
        string host,
        int port,
        Action<TcpClientOptions>? configure = null)
    {
        // 映射配置到 IOptions<TcpClientOptions>
        builder.Services.Configure<TcpClientOptions>(options =>
        {
            options.Host = host;
            options.Port = port;
            configure?.Invoke(options);
        });

        // 注册 IClientTransport 实现
        builder.Services.TryAddSingleton<IClientTransport, TcpClientTransport>();

        return builder;
    }

    /// <summary>
    /// 为服务端启用 TLS，指定服务端证书。
    /// </summary>
    public static IServerBuilder WithTls(this IServerBuilder builder, System.Security.Cryptography.X509Certificates.X509Certificate2 certificate)
    {
        builder.Services.PostConfigure<TcpServerOptions>(options =>
        {
            options.ServerCertificate = certificate ?? throw new ArgumentNullException(nameof(certificate));
        });
        return builder;
    }

    /// <summary>
    /// 为客户端启用 TLS，指定目标主机名（SNI）。
    /// </summary>
    public static IClientBuilder WithTls(this IClientBuilder builder, string targetHost)
    {
        builder.Services.PostConfigure<TcpClientOptions>(options =>
        {
            options.TargetHost = targetHost ?? throw new ArgumentNullException(nameof(targetHost));
        });
        return builder;
    }
}