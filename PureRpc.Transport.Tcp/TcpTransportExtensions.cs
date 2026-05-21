using Microsoft.Extensions.DependencyInjection;
using PureRpc.Abstractions;
using PureRpc.Transport.Tcp;
using System.Net;

namespace PureRpc;

public static class TcpTransportExtensions
{
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

    public static IServerBuilder WithTls(this IServerBuilder builder, System.Security.Cryptography.X509Certificates.X509Certificate2 certificate)
    {
        builder.Services.PostConfigure<TcpServerOptions>(options =>
        {
            options.ServerCertificate = certificate ?? throw new ArgumentNullException(nameof(certificate));
        });
        return builder;
    }

    public static IClientBuilder WithTls(this IClientBuilder builder, string targetHost)
    {
        builder.Services.PostConfigure<TcpClientOptions>(options =>
        {
            options.TargetHost = targetHost ?? throw new ArgumentNullException(nameof(targetHost));
        });
        return builder;
    }
}
