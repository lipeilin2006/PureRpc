using System;
using System.Diagnostics;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;

namespace PureRpc.Abstractions;

/// <summary>
/// 授权处理器的抽象基类，提供 Policy / Roles 检查的默认实现 / 
/// Abstract base class for authorization handlers, providing default Policy / Roles checking.
/// 子类只需重写 <see cref="ResolvePrincipalAsync"/> 来提供当前用户主体 / 
/// Subclasses only need to override <see cref="ResolvePrincipalAsync"/> to provide the current user principal.
/// </summary>
public abstract class AuthorizationHandlerBase : IAuthorizationHandler
{
    /// <summary>
    /// 授权操作的 ActivitySource，用于 OpenTelemetry 追踪 / 
    /// ActivitySource for authorization operations, used for OpenTelemetry tracing.
    /// </summary>
    private static readonly ActivitySource AuthActivitySource = new("PureRpc.Authorization");

    /// <summary>
    /// 对当前 RPC 请求执行授权检查 / Performs authorization checks on the current RPC request.
    /// 先尝试使用拦截器已设置的 User，否则调用 <see cref="ResolvePrincipalAsync"/> 解析 / 
    /// First attempts to use the User already set by interceptors; otherwise calls <see cref="ResolvePrincipalAsync"/> to resolve.
    /// </summary>
    /// <param name="context">当前 RPC 请求上下文 / The current RPC request context.</param>
    /// <param name="policy">要检查的策略名称（可为 null） / The policy name to check (may be null).</param>
    /// <param name="roles">要检查的角色列表，逗号分隔（可为 null） / The comma-separated roles to check (may be null).</param>
    /// <param name="ct">取消令牌 / Cancellation token.</param>
    /// <exception cref="UnauthorizedAccessException">认证失败或角色/策略检查不通过时抛出 / 
    /// Thrown when authentication fails or role/policy check does not pass.</exception>
    public async ValueTask AuthorizeAsync(RpcContext context, string? policy, string? roles, CancellationToken ct)
    {
        using var activity = AuthActivitySource.StartActivity("Authorize");
        activity?.SetTag("rpc.service", context.ServiceName);
        activity?.SetTag("rpc.method", context.MethodName);
        if (policy != null) activity?.SetTag("auth.policy", policy);
        if (roles != null) activity?.SetTag("auth.roles", roles);

        var principal = context.User;

        if (principal?.Identity?.IsAuthenticated != true)
        {
            principal = await ResolvePrincipalAsync(context, ct).ConfigureAwait(false);
        }

        if (principal?.Identity?.IsAuthenticated != true)
        {
            activity?.SetStatus(ActivityStatusCode.Error, "Authentication failed");
            throw new UnauthorizedAccessException("Authentication failed: no valid principal resolved.");
        }

        if (!string.IsNullOrEmpty(roles))
        {
            var roleList = roles.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (roleList.Length > 0)
            {
                bool anyMatch = false;
                foreach (var role in roleList)
                {
                    if (await CheckRoleAsync(principal, role, ct).ConfigureAwait(false))
                    {
                        anyMatch = true;
                        break;
                    }
                }
                if (!anyMatch)
                {
                    activity?.SetStatus(ActivityStatusCode.Error, "Role check failed");
                    throw new UnauthorizedAccessException(
                        $"Authorization failed: user does not have any of the required roles '{roles}'.");
                }
            }
        }

        if (!string.IsNullOrEmpty(policy))
        {
            if (!await CheckPolicyAsync(principal, policy, ct).ConfigureAwait(false))
            {
                activity?.SetStatus(ActivityStatusCode.Error, "Policy check failed");
                throw new UnauthorizedAccessException($"Authorization failed: policy '{policy}' not satisfied.");
            }
        }

        activity?.SetStatus(ActivityStatusCode.Ok);
        context.User = principal;
    }

    /// <summary>
    /// 解析当前请求的用户主体 / Resolves the user principal for the current request.
    /// 返回 null 或未认证的主体视为认证失败 / 
    /// Returning null or an unauthenticated principal is treated as authentication failure.
    /// </summary>
    /// <param name="context">当前 RPC 请求上下文 / The current RPC request context.</param>
    /// <param name="ct">取消令牌 / Cancellation token.</param>
    /// <returns>解析出的用户主体 / The resolved user principal.</returns>
    protected abstract ValueTask<ClaimsPrincipal?> ResolvePrincipalAsync(RpcContext context, CancellationToken ct);

    /// <summary>
    /// 检查用户是否具有指定角色 / Checks if the user has the specified role.
    /// 默认委托给 <see cref="ClaimsPrincipal.IsInRole"/> / 
    /// Defaults to delegating to <see cref="ClaimsPrincipal.IsInRole"/>.
    /// </summary>
    /// <param name="principal">用户主体 / The user principal.</param>
    /// <param name="role">角色名称 / The role name.</param>
    /// <param name="ct">取消令牌 / Cancellation token.</param>
    /// <returns>用户是否属于指定角色 / Whether the user belongs to the specified role.</returns>
    protected virtual ValueTask<bool> CheckRoleAsync(ClaimsPrincipal principal, string role, CancellationToken ct)
    {
        return new ValueTask<bool>(principal.IsInRole(role));
    }

    /// <summary>
    /// 检查用户是否满足指定 Policy / Checks if the user satisfies the specified policy.
    /// 默认查找用户是否有 Claim.Type 为 "Permission" 或 "policy" 且 Value 匹配的值 / 
    /// Defaults to checking if the user has a Claim with Type "Permission" or "policy" and matching Value.
    /// </summary>
    /// <param name="principal">用户主体 / The user principal.</param>
    /// <param name="policy">策略名称 / The policy name.</param>
    /// <param name="ct">取消令牌 / Cancellation token.</param>
    /// <returns>用户是否满足策略 / Whether the user satisfies the policy.</returns>
    protected virtual ValueTask<bool> CheckPolicyAsync(ClaimsPrincipal principal, string policy, CancellationToken ct)
    {
        return new ValueTask<bool>(
            principal.HasClaim(c =>
                (c.Type == "Permission" || c.Type == "policy") &&
                string.Equals(c.Value, policy, StringComparison.Ordinal)));
    }
}
