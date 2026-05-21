using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace PureRpc.Abstractions;

internal static class TransportRegistration
{
    public static IServerBuilder AddServerTransport<TOptions, TTransport>(
        this IServerBuilder builder, Action<TOptions> configure)
        where TOptions : class, new()
        where TTransport : class, IServerTransport
    {
        builder.Services.Configure(configure);
        builder.Services.TryAddSingleton<IServerTransport, TTransport>();
        return builder;
    }

    public static IClientBuilder AddClientTransport<TOptions, TTransport>(
        this IClientBuilder builder, Action<TOptions> configure)
        where TOptions : class, new()
        where TTransport : class, IClientTransport
    {
        builder.Services.Configure(configure);
        builder.Services.TryAddSingleton<IClientTransport, TTransport>();
        return builder;
    }
}
