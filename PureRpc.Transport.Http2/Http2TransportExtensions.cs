using PureRpc.Abstractions;
using PureRpc.Transport.Http2;

namespace PureRpc;

public static class Http2TransportExtensions
{
    public static IServerBuilder WithHttp2Transport(this IServerBuilder builder, int port,
        Action<Http2ServerOptions>? configure = null)
    {
        return TransportRegistration.AddServerTransport<Http2ServerOptions, Http2ServerTransport>(builder, options =>
        {
            options.Port = port;
            configure?.Invoke(options);
        });
    }

    public static IClientBuilder WithHttp2Transport(this IClientBuilder builder, string url,
        Action<Http2ClientOptions>? configure = null)
    {
        return TransportRegistration.AddClientTransport<Http2ClientOptions, Http2ClientTransport>(builder, options =>
        {
            options.Url = url;
            configure?.Invoke(options);
        });
    }
}
