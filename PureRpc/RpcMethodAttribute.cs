using System;

namespace PureRpc;

/// <summary>
/// 标记 RPC 接口中的方法。用于自定义方法在协议中的名称或行为。
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class RpcMethodAttribute : Attribute
{
    /// <summary>
    /// 获取在远程调用中使用的自定义方法名称。
    /// 如果不设置，默认将使用 C# 接口中的方法名。
    /// </summary>
    public string? MethodName { get; }

    /// <summary>
    /// 获取或设置该方法是否为单向调用（Fire-and-forget）。
    /// 对于不需要返回值的通知类消息，开启此项可进一步提升性能。
    /// </summary>
    public bool IsOneWay { get; init; }

    /// <summary>
    /// 初始化 <see cref="RpcMethodAttribute"/> 的新实例。
    /// </summary>
    /// <param name="methodName">自定义协议方法名。如果不传，则默认使用代码中的方法名。</param>
    public RpcMethodAttribute(string? methodName = null)
    {
        MethodName = methodName;
    }
}