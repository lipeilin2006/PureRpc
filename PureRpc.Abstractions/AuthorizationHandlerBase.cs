using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;

namespace PureRpc.Abstractions;

/// <summary>
/// 授权处理器的抽象基类，提供 Policy / Roles 检查的默认实现。
/// 子类只需重写 <see cref="ResolvePrincipalAsync"/> 来提供当前用户主体。
/// </summary>
public abstract class AuthorizationHandlerBase : IAuthorizationHandler
{
    public async ValueTask AuthorizeAsync(RpcContext context, string? policy, string? roles, CancellationToken ct)
    {
        var principal = await ResolvePrincipalAsync(context, ct).ConfigureAwait(false);

        if (principal?.Identity?.IsAuthenticated != true)
            throw new UnauthorizedAccessException("Authentication failed: no valid principal resolved.");

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
                    throw new UnauthorizedAccessException(
                        $"Authorization failed: user does not have any of the required roles '{roles}'.");
            }
        }

        if (!string.IsNullOrEmpty(policy))
        {
            if (!await CheckPolicyAsync(principal, policy, ct).ConfigureAwait(false))
                throw new UnauthorizedAccessException($"Authorization failed: policy '{policy}' not satisfied.");
        }

        context.User = principal;
    }

    /// <summary>
    /// 解析当前请求的用户主体。返回 null 或未认证的主体视为认证失败。
    /// </summary>
    protected abstract ValueTask<ClaimsPrincipal?> ResolvePrincipalAsync(RpcContext context, CancellationToken ct);

    /// <summary>
    /// 检查用户是否具有指定角色。默认委托给 <see cref="ClaimsPrincipal.IsInRole"/>。
    /// </summary>
    protected virtual ValueTask<bool> CheckRoleAsync(ClaimsPrincipal principal, string role, CancellationToken ct)
    {
        return new ValueTask<bool>(principal.IsInRole(role));
    }

    /// <summary>
    /// 检查用户是否满足指定 Policy。默认查找用户是否有 Claim.Type 为 "Permission" 或 "policy" 且 Value 匹配的值。
    /// </summary>
    protected virtual ValueTask<bool> CheckPolicyAsync(ClaimsPrincipal principal, string policy, CancellationToken ct)
    {
        return new ValueTask<bool>(
            principal.HasClaim(c =>
                (c.Type == "Permission" || c.Type == "policy") &&
                string.Equals(c.Value, policy, StringComparison.Ordinal)));
    }
}
