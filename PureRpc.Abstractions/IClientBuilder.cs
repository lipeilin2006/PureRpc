using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Text;

namespace PureRpc.Abstractions
{
    /// <summary>
    /// 定义 RPC 客户端构建器接口 / Defines the RPC client builder interface.
    /// 用于配置客户端的服务注册和传输层 / Used to configure client service registration and transport layer.
    /// </summary>
    public interface IClientBuilder
    {
        /// <summary>
        /// 获取服务集合，用于注册客户端依赖项 / 
        /// Gets the service collection for registering client dependencies.
        /// </summary>
        IServiceCollection Services { get; }
    }
}
