using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PureRpc;
using PureRpc.Abstractions;

namespace Microsoft.Extensions.DependencyInjection;

public static class RpcExtensions
{
    public static IServerBuilder AddPureRpcServer(this IServiceCollection services)
    {
        services.TryAddSingleton<RpcMetrics>();

        services.TryAddSingleton<IRpcServer, RpcServer>();

        services.AddHostedService<RpcServerHostedService>();

        return new RpcServerBuilder(services);
    }

    public static IClientBuilder AddPureRpcClient(this IServiceCollection services)
    {
        services.TryAddSingleton<RpcMetrics>();

        services.TryAddSingleton<RpcClient>();

        services.TryAddSingleton<IRpcClient>(sp =>
            new InterceptedRpcClient(
                sp.GetRequiredService<RpcClient>(),
                sp.GetService<IEnumerable<IRpcClientInterceptor>>()));

        services.AddHostedService<RpcClientHostedService>();

        return new RpcClientBuilder(services);
    }
}
