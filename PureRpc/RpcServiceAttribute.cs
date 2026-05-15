using System;

namespace PureRpc
{
    /// <summary>
    /// 标记一个接口为 PureRpc 服务契约。
    /// Source Generator 将根据此特性生成对应的 Dispatcher 和 Client Proxy。
    /// </summary>
    [AttributeUsage(AttributeTargets.Interface, AllowMultiple = false, Inherited = false)]
    public sealed class RpcServiceAttribute : Attribute
    {
        /// <summary>
        /// 获取服务的唯一名称。如果为空，Source Generator 通常默认使用接口全名。
        /// </summary>
        public string? ServiceName { get; }

        /// <summary>
        /// 初始化 <see cref="RpcServiceAttribute"/> 的新实例。
        /// </summary>
        /// <param name="serviceName">可选的服务自定义名称。</param>
        public RpcServiceAttribute(string? serviceName = null)
        {
            ServiceName = serviceName;
        }
    }
}