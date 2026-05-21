using Microsoft.Extensions.DependencyInjection;
using PureRpc.Abstractions;

namespace PureRpc
{
    /// <summary>
    /// RPC 客户端构建器实现 / RPC client builder implementation.
    /// 提供 <see cref="IClientBuilder"/> 的默认实现，用于注册客户端依赖项 / 
    /// Provides the default implementation of <see cref="IClientBuilder"/> for registering client dependencies.
    /// </summary>
    public sealed class RpcClientBuilder : IClientBuilder
    {
        /// <summary>
        /// 获取服务集合，用于注册客户端依赖项 / 
        /// Gets the service collection for registering client dependencies.
        /// </summary>
        public IServiceCollection Services { get; }

        /// <summary>
        /// 初始化 RpcClientBuilder 实例 / Initializes a new RpcClientBuilder instance.
        /// </summary>
        /// <param name="services">服务集合 / The service collection.</param>
        public RpcClientBuilder(IServiceCollection services)
        {
            Services = services;
        }
    }
}