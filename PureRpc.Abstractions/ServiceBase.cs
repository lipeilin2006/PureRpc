using System;
using System.Security.Claims;
using System.Threading;

namespace PureRpc.Abstractions
{
    /// <summary>
    /// RPC 服务基类 / Base class for RPC services.
    /// 提供 RPC 请求上下文访问、取消令牌和认证用户信息 / 
    /// Provides access to the RPC request context, cancellation token, and authenticated user information.
    /// </summary>
    public abstract class ServiceBase
    {
        /// <summary>
        /// 异步本地存储，用于保存当前线程的 RPC 请求上下文 / 
        /// Async-local storage for the current thread's RPC request context.
        /// </summary>
        private static readonly AsyncLocal<RpcContext?> _currentContext = new();

        /// <summary>
        /// 当前 RPC 请求上下文，由 Dispatcher 在调用前注入 / 
        /// The current RPC request context, injected by the Dispatcher before invocation.
        /// </summary>
        /// <exception cref="InvalidOperationException">当上下文未初始化时抛出 / Thrown when the context is not initialized.</exception>
        public RpcContext Context
        {
            get => _currentContext.Value ?? throw new InvalidOperationException("RPC Context is not initialized.");
            set => _currentContext.Value = value;
        }

        /// <summary>
        /// 快捷获取取消令牌，随网络断开或客户端取消而触发 / 
        /// Shortcut to get the cancellation token, triggered on network disconnect or client cancellation.
        /// </summary>
        protected CancellationToken CancellationToken => Context.CancellationToken;

        /// <summary>
        /// 当前已认证用户主体，由 IAuthorizationHandler 在授权成功后设置 / 
        /// The current authenticated user principal, set by IAuthorizationHandler after successful authorization.
        /// 若未认证或未启用授权，返回 null / Returns null if not authenticated or authorization is not enabled.
        /// </summary>
        protected ClaimsPrincipal? User => Context.User;
    }
}
