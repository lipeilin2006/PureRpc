using PureRpc.Abstractions;

namespace Microsoft.Extensions.DependencyInjection;

public static class InterceptorExtensions
{
    public static IServerBuilder AddServerInterceptor<T>(this IServerBuilder builder)
        where T : class, IRpcServerInterceptor
    {
        builder.Services.AddSingleton<IRpcServerInterceptor, T>();
        return builder;
    }

    public static IClientBuilder AddClientInterceptor<T>(this IClientBuilder builder)
        where T : class, IRpcClientInterceptor
    {
        builder.Services.AddSingleton<IRpcClientInterceptor, T>();
        return builder;
    }
}
