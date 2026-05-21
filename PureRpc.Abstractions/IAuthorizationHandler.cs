using System.Threading;
using System.Threading.Tasks;

namespace PureRpc.Abstractions;

/// <summary>
/// 定义 RPC 授权处理器接口 / Defines the RPC authorization handler interface.
/// 用于在请求分发前执行认证和授权检查 / 
/// Used to perform authentication and authorization checks before request dispatch.
/// </summary>
public interface IAuthorizationHandler
{
    /// <summary>
    /// 对当前 RPC 请求执行授权检查 / Performs authorization checks on the current RPC request.
    /// </summary>
    /// <param name="context">当前 RPC 请求上下文 / The current RPC request context.</param>
    /// <param name="policy">要检查的策略名称（可为 null） / The policy name to check (may be null).</param>
    /// <param name="roles">要检查的角色列表（逗号分隔，可为 null） / The comma-separated roles to check (may be null).</param>
    /// <param name="ct">取消令牌 / Cancellation token.</param>
    /// <returns>表示异步授权过程的 ValueTask / A ValueTask representing the asynchronous authorization process.</returns>
    /// <exception cref="System.UnauthorizedAccessException">授权失败时抛出 / Thrown when authorization fails.</exception>
    ValueTask AuthorizeAsync(RpcContext context, string? policy, string? roles, CancellationToken ct);
}
