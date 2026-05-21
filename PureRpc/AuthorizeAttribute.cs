using System;

namespace PureRpc
{
    /// <summary>
    /// 标记 RPC 接口或方法需要授权 / Marks an RPC interface or method as requiring authorization.
    /// 可指定策略名称和/或角色列表 / Can specify a policy name and/or role list.
    /// 可应用于接口级别（对所有方法生效）和方法级别（对特定方法生效）/ 
    /// Can be applied at the interface level (affecting all methods) and the method level (affecting specific methods).
    /// 方法级别和接口级别的授权为 AND 关系，与 ASP.NET Core 行为一致 / 
    /// Method-level and interface-level authorization have an AND relationship, consistent with ASP.NET Core behavior.
    /// </summary>
    [AttributeUsage(AttributeTargets.Interface | AttributeTargets.Method, AllowMultiple = true, Inherited = true)]
    public sealed class AuthorizeAttribute : Attribute
    {
        /// <summary>
        /// 获取授权策略名称 / Gets the authorization policy name.
        /// 在 <see cref="AuthorizationHandlerBase.CheckPolicyAsync"/> 中用于策略检查 / 
        /// Used for policy checking in <see cref="AuthorizationHandlerBase.CheckPolicyAsync"/>.
        /// </summary>
        public string? Policy { get; }

        /// <summary>
        /// 获取或设置允许的角色列表，逗号分隔 / Gets or sets the allowed roles, comma-separated.
        /// 任意一个角色匹配即视为授权通过 / Any single role match is considered authorized.
        /// </summary>
        public string? Roles { get; init; }

        /// <summary>
        /// 初始化 AuthorizeAttribute 实例（无策略） / Initializes an AuthorizeAttribute instance (no policy).
        /// </summary>
        public AuthorizeAttribute() { }

        /// <summary>
        /// 使用策略名称初始化 AuthorizeAttribute 实例 / Initializes an AuthorizeAttribute instance with a policy name.
        /// </summary>
        /// <param name="policy">授权策略名称 / The authorization policy name.</param>
        public AuthorizeAttribute(string policy) => Policy = policy;
    }
}