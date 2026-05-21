using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PureRpc;
using PureRpc.Abstractions;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// PureRpc 服务注册扩展方法 / PureRpc service registration extension methods.
/// 提供简洁的 DSL 来配置 RPC 客户端和服务端 / 
/// Provides a concise DSL for configuring RPC clients and servers.
/// </summary>
public static class RpcExtensions
{
    /// <summary>
    /// 注册 PureRpc 服务端及其依赖项 / Registers the PureRpc server and its dependencies.
    /// 包括 RpcMetrics、RpcServer 和后台托管服务 / 
    /// Includes RpcMetrics, RpcServer, and background hosted service.
    /// </summary>
    /// <param name="services">服务集合 / The service collection.</param>
    /// <returns>服务端构建器，用于进一步配置传输层和序列化器 / The server builder for further configuration.</returns>
    public static IServerBuilder AddPureRpcServer(this IServiceCollection services)
    {
        services.TryAddSingleton<RpcMetrics>();

        services.TryAddSingleton<IRpcServer, RpcServer>();

        services.AddHostedService<RpcServerHostedService>();

        return new RpcServerBuilder(services);
    }

    /// <summary>
    /// 注册 PureRpc 客户端及其依赖项 / Registers the PureRpc client and its dependencies.
    /// 包括 RpcMetrics、RpcClient、拦截器管道和后台托管服务 / 
    /// Includes RpcMetrics, RpcClient, interceptor pipeline, and background hosted service.
    /// </summary>
    /// <param name="services">服务集合 / The service collection.</param>
    /// <returns>客户端构建器，用于进一步配置传输层和序列化器 / The client builder for further configuration.</returns>
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