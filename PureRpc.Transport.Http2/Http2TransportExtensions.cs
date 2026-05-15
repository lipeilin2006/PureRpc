using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PureRpc.Abstractions;
using PureRpc.Transport.Http2;

namespace PureRpc;

public static class Http2TransportExtensions
{
    public static IServerBuilder WithHttp2Transport(this IServerBuilder builder, int port,
        Action<Http2ServerOptions>? configure = null)
    {
        builder.Services.Configure<Http2ServerOptions>(options =>
        {
            options.Port = port;
            configure?.Invoke(options);
        });
        builder.Services.TryAddSingleton<IServerTransport, Http2ServerTransport>();
        return builder;
    }

    public static IClientBuilder WithHttp2Transport(this IClientBuilder builder, string url,
        Action<Http2ClientOptions>? configure = null)
    {
        builder.Services.Configure<Http2ClientOptions>(options =>
        {
            options.Url = url;
            configure?.Invoke(options);
        });
        builder.Services.TryAddSingleton<IClientTransport, Http2ClientTransport>();
        return builder;
    }
}
