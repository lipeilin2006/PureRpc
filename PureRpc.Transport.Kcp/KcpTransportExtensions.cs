using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PureRpc.Abstractions;
using PureRpc.Transport.Kcp;

namespace PureRpc;

public static class KcpTransportExtensions
{
    public static IServerBuilder WithKcpTransport(
        this IServerBuilder builder,
        ushort port,
        Action<KcpServerOptions>? configure = null)
    {
        builder.Services.Configure<KcpServerOptions>(options =>
        {
            options.Port = port;
            configure?.Invoke(options);
        });
        builder.Services.TryAddSingleton<IServerTransport, KcpServerTransport>();
        return builder;
    }

    public static IClientBuilder WithKcpTransport(
        this IClientBuilder builder,
        string host,
        ushort port,
        Action<KcpClientOptions>? configure = null)
    {
        builder.Services.Configure<KcpClientOptions>(options =>
        {
            options.Host = host;
            options.Port = port;
            configure?.Invoke(options);
        });
        builder.Services.TryAddSingleton<IClientTransport, KcpClientTransport>();
        return builder;
    }
}
