using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;

namespace PureRpc;

/// <summary>
/// 定义一个授权策略：通过一组自定义的断言函数来描述授权要求。
/// </summary>
public sealed class AuthorizationPolicy
{
    internal List<Func<ClaimsPrincipal, CancellationToken, ValueTask<bool>>> Requirements { get; } = new();

    public AuthorizationPolicy RequireRole(string role)
    {
        Requirements.Add((principal, ct) => new ValueTask<bool>(principal.IsInRole(role)));
        return this;
    }

    public AuthorizationPolicy RequireClaim(string claimType, string? claimValue = null)
    {
        Requirements.Add((principal, ct) =>
            new ValueTask<bool>(
                claimValue == null
                    ? principal.HasClaim(c => c.Type == claimType)
                    : principal.HasClaim(c => c.Type == claimType && c.Value == claimValue)));
        return this;
    }

    public AuthorizationPolicy RequireAssertion(Func<ClaimsPrincipal, CancellationToken, ValueTask<bool>> assertion)
    {
        Requirements.Add(assertion);
        return this;
    }
}

/// <summary>
/// 授权策略配置，用于注册命名策略供 [Authorize("policyName")] 引用。
/// </summary>
public sealed class AuthorizationOptions
{
    internal Dictionary<string, AuthorizationPolicy> Policies { get; } = new(StringComparer.OrdinalIgnoreCase);

    public AuthorizationOptions AddPolicy(string name, Action<AuthorizationPolicy> configure)
    {
        var policy = new AuthorizationPolicy();
        configure(policy);
        Policies[name] = policy;
        return this;
    }
}
