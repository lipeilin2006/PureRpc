using PureRpc.Abstractions;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// RPC 拦截器注册扩展方法 / RPC interceptor registration extension methods.
/// 提供简洁的方法注册服务端和客户端拦截器 / 
/// Provides concise methods for registering server and client interceptors.
/// </summary>
public static class InterceptorExtensions
{
    /// <summary>
    /// 向服务端构建器注册一个拦截器 / Registers a server interceptor with the server builder.
    /// 拦截器将按注册顺序组成管道 / Interceptors form a pipeline in registration order.
    /// </summary>
    /// <typeparam name="T">拦截器实现类型 / The interceptor implementation type.</typeparam>
    /// <param name="builder">服务端构建器 / The server builder.</param>
    /// <returns>服务端构建器（支持链式调用） / The server builder (supports fluent chaining).</returns>
    public static IServerBuilder AddServerInterceptor<T>(this IServerBuilder builder)
        where T : class, IRpcServerInterceptor
    {
        builder.Services.AddSingleton<IRpcServerInterceptor, T>();
        return builder;
    }

    /// <summary>
    /// 向客户端构建器注册一个拦截器 / Registers a client interceptor with the client builder.
    /// 拦截器将按注册顺序组成管道 / Interceptors form a pipeline in registration order.
    /// </summary>
    /// <typeparam name="T">拦截器实现类型 / The interceptor implementation type.</typeparam>
    /// <param name="builder">客户端构建器 / The client builder.</param>
    /// <returns>客户端构建器（支持链式调用） / The client builder (supports fluent chaining).</returns>
    public static IClientBuilder AddClientInterceptor<T>(this IClientBuilder builder)
        where T : class, IRpcClientInterceptor
    {
        builder.Services.AddSingleton<IRpcClientInterceptor, T>();
        return builder;
    }
}