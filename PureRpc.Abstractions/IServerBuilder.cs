using Microsoft.Extensions.DependencyInjection;

namespace PureRpc.Abstractions
{
    /// <summary>
    /// 定义 RPC 服务端构建器接口 / Defines the RPC server builder interface.
    /// 用于配置服务端的服务注册和传输层 / Used to configure server service registration and transport layer.
    /// </summary>
    public interface IServerBuilder
    {
        /// <summary>
        /// 获取服务集合，用于注册服务端依赖项 / 
        /// Gets the service collection for registering server dependencies.
        /// </summary>
        IServiceCollection Services { get; }
    }
}
