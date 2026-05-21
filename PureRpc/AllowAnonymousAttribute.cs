using System;

namespace PureRpc
{
    /// <summary>
    /// 标记 RPC 接口中的方法为允许匿名访问 / Marks an RPC interface method as allowing anonymous access.
    /// 当此属性应用于方法时，将跳过该方法的授权检查 / 
    /// When this attribute is applied to a method, authorization checks will be skipped for that method.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
    public sealed class AllowAnonymousAttribute : Attribute { }
}