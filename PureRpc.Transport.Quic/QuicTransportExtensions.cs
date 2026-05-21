using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PureRpc.Abstractions;
using PureRpc.Transport.Quic;

namespace PureRpc;

public static class QuicTransportExtensions
{
    public static IServerBuilder WithQuicTransport(this IServerBuilder builder, int port,
        Action<QuicServerOptions>? configure = null)
    {
        builder.Services.Configure<QuicServerOptions>(options =>
        {
            options.ListenEndPoint = new System.Net.IPEndPoint(System.Net.IPAddress.Any, port);
            configure?.Invoke(options);
        });
        builder.Services.TryAddSingleton<IServerTransport, QuicServerTransport>();
        return builder;
    }

    public static IClientBuilder WithQuicTransport(this IClientBuilder builder, string host, int port,
        Action<QuicClientOptions>? configure = null)
    {
        builder.Services.Configure<QuicClientOptions>(options =>
        {
            options.Host = host;
            options.Port = port;
            configure?.Invoke(options);
        });
        builder.Services.TryAddSingleton<IClientTransport, QuicClientTransport>();
        return builder;
    }
}
