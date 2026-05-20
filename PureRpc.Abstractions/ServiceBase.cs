using System;
using System.Security.Claims;
using System.Threading;

namespace PureRpc.Abstractions
{
    public abstract class ServiceBase
    {
        private static readonly AsyncLocal<RpcContext?> _currentContext = new();

        /// <summary>
        /// 当前 RPC 请求上下文，由 Dispatcher 在调用前注入
        /// </summary>
        public RpcContext Context
        {
            get => _currentContext.Value ?? throw new InvalidOperationException("RPC Context is not initialized.");
            set => _currentContext.Value = value;
        }

        /// <summary>
        /// 快捷获取取消令牌，随网络断开或客户端取消而触发
        /// </summary>
        protected CancellationToken CancellationToken => Context.CancellationToken;

        /// <summary>
        /// 当前已认证用户主体，由 IAuthorizationHandler 在授权成功后设置。
        /// 若未认证或未启用授权，返回 null。
        /// </summary>
        protected ClaimsPrincipal? User => Context.User;
    }
}