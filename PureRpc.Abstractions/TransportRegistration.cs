using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace PureRpc.Abstractions;

/// <summary>
/// 传输层注册扩展方法 / Transport layer registration extension methods.
/// 提供 IServerBuilder 和 IClientBuilder 的传输层注册便捷方法 / 
/// Provides convenient transport registration methods for IServerBuilder and IClientBuilder.
/// </summary>
internal static class TransportRegistration
{
    /// <summary>
    /// 向服务端构建器注册传输层实现 / Registers a transport implementation with the server builder.
    /// 同时配置选项并注册为 Singleton / 
    /// Configures options and registers as Singleton simultaneously.
    /// </summary>
    /// <typeparam name="TOptions">传输层选项类型 / Transport options type.</typeparam>
    /// <typeparam name="TTransport">传输层实现类型 / Transport implementation type.</typeparam>
    /// <param name="builder">服务端构建器 / The server builder.</param>
    /// <param name="configure">选项配置委托 / Options configuration delegate.</param>
    /// <returns>服务端构建器（支持链式调用） / The server builder (supports fluent chaining).</returns>
    public static IServerBuilder AddServerTransport<TOptions, TTransport>(
        this IServerBuilder builder, Action<TOptions> configure)
        where TOptions : class, new()
        where TTransport : class, IServerTransport
    {
        builder.Services.Configure(configure);
        builder.Services.TryAddSingleton<IServerTransport, TTransport>();
        return builder;
    }

    /// <summary>
    /// 向客户端构建器注册传输层实现 / Registers a transport implementation with the client builder.
    /// 同时配置选项并注册为 Singleton / 
    /// Configures options and registers as Singleton simultaneously.
    /// </summary>
    /// <typeparam name="TOptions">传输层选项类型 / Transport options type.</typeparam>
    /// <typeparam name="TTransport">传输层实现类型 / Transport implementation type.</typeparam>
    /// <param name="builder">客户端构建器 / The client builder.</param>
    /// <param name="configure">选项配置委托 / Options configuration delegate.</param>
    /// <returns>客户端构建器（支持链式调用） / The client builder (supports fluent chaining).</returns>
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
