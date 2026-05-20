using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PureRpc.Abstractions;
using PureRpc.Transport.Http3;

namespace PureRpc;

public static class Http3TransportExtensions
{
    public static IServerBuilder WithHttp3Transport(this IServerBuilder builder, int port,
        Action<Http3ServerOptions>? configure = null)
    {
        builder.Services.Configure<Http3ServerOptions>(options =>
        {
            options.Port = port;
            configure?.Invoke(options);
        });
        builder.Services.TryAddSingleton<IServerTransport, Http3ServerTransport>();
        return builder;
    }

    public static IClientBuilder WithHttp3Transport(this IClientBuilder builder, string url,
        Action<Http3ClientOptions>? configure = null)
    {
        builder.Services.Configure<Http3ClientOptions>(options =>
        {
            options.Url = url;
            configure?.Invoke(options);
        });
        builder.Services.TryAddSingleton<IClientTransport, Http3ClientTransport>();
        return builder;
    }
}
