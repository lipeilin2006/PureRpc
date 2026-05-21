using Microsoft.Extensions.DependencyInjection;
using PureRpc.Abstractions;
using System.Collections.Generic;

namespace PureRpc
{
    /// <summary>
    /// RPC 服务端构建器实现 / RPC server builder implementation.
    /// 提供 <see cref="IServerBuilder"/> 的默认实现，用于注册服务端依赖项 / 
    /// Provides the default implementation of <see cref="IServerBuilder"/> for registering server dependencies.
    /// </summary>
    public class RpcServerBuilder : IServerBuilder
    {
        /// <summary>
        /// 获取服务集合，用于注册服务端依赖项 / 
        /// Gets the service collection for registering server dependencies.
        /// </summary>
        public IServiceCollection Services { get; }

        /// <summary>
        /// 初始化 RpcServerBuilder 实例 / Initializes a new RpcServerBuilder instance.
        /// </summary>
        /// <param name="services">服务集合 / The service collection.</param>
        public RpcServerBuilder(IServiceCollection services)
        {
            Services = services;
        }
    }
}