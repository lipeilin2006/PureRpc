using PureRpc.Abstractions;
using PureRpc.Transport.Quic;

namespace PureRpc;

public static class QuicTransportExtensions
{
    public static IServerBuilder WithQuicTransport(this IServerBuilder builder, int port,
        Action<QuicServerOptions>? configure = null)
    {
        return TransportRegistration.AddServerTransport<QuicServerOptions, QuicServerTransport>(builder, options =>
        {
            options.ListenEndPoint = new System.Net.IPEndPoint(System.Net.IPAddress.Any, port);
            configure?.Invoke(options);
        });
    }

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
