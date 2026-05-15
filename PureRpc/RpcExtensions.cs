using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PureRpc;
using PureRpc.Abstractions;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// PureRpc 框架的 DI 注册入口扩展类。
/// </summary>
public static class RpcExtensions
{
    /// <summary>
    /// 在当前服务集合中启用 PureRpc 服务端配置。
    /// </summary>
    public static IServerBuilder AddPureRpcServer(this IServiceCollection services)
    {
        // 注册服务端核心组件
        services.TryAddSingleton<IRpcServer, RpcServer>();

        services.AddHostedService<RpcServerHostedService>();

        return new RpcServerBuilder(services);
    }

    /// <summary>
    /// 在当前服务集合中启用 PureRpc 客户端配置。
    /// </summary>
    public static IClientBuilder AddPureRpcClient(this IServiceCollection services)
    {
        // 1. 注册核心客户端引擎（具体类型，供 InterceptedRpcClient 包装）
        services.TryAddSingleton<RpcClient>();

        // 2. 注册拦截器包装后的 IRpcClient
        services.TryAddSingleton<IRpcClient>(sp =>
            new InterceptedRpcClient(
                sp.GetRequiredService<RpcClient>(),
                sp.GetService<IEnumerable<IRpcClientInterceptor>>()));

        // 3. 注册托管服务，确保 Host 启动时自动触发 StartAsync (建立连接)
        services.AddHostedService<RpcClientHostedService>();

        // 4. 返回 Builder 以便后续链式配置 Transport 和其他选项
        return new RpcClientBuilder(services);
    }
}