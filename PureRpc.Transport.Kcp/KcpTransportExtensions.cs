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
        return TransportRegistration.AddServerTransport<KcpServerOptions, KcpServerTransport>(builder, options =>
        {
            options.Port = port;
            configure?.Invoke(options);
        });
    }

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
