using PureRpc.Abstractions;
using System.Security.Claims;

namespace PureRpc.Test;

internal class CalcService : ServiceBase, ICalcService
{
    public Task<int> AddAsync(AddRequest request, CancellationToken ct) =>
        Task.FromResult(request.A + request.B);

    public Task<string> EchoAsync(EchoRequest request, CancellationToken ct) =>
        Task.FromResult(request.Message);
}

internal class AuthService : ServiceBase, IAuthService
{
    public Task<string> PingAsync(PingRequest request, CancellationToken ct) =>
        Task.FromResult(request.Value);
}

internal class MixedAuthService : ServiceBase, IMixedAuthService
{
    public Task<string> OpenAsync(PingRequest request, CancellationToken ct) =>
        Task.FromResult($"open:{request.Value}");

    public Task<string> SecuredAsync(PingRequest request, CancellationToken ct) =>
        Task.FromResult($"secured:{request.Value}");
}

internal class PolicyAuthService : ServiceBase, IPolicyAuthService
{
    public Task<string> ExecuteAsync(PingRequest request, CancellationToken ct) =>
        Task.FromResult($"executed:{request.Value}");
}

internal class AllowAnonService : ServiceBase, IAllowAnonService
{
    public Task<string> PublicAsync(PingRequest request, CancellationToken ct) =>
        Task.FromResult($"public:{request.Value}");

    public Task<string> SecuredAsync(PingRequest request, CancellationToken ct) =>
        Task.FromResult($"secured:{request.Value}");
}

internal class UserAwareService : ServiceBase, IUserAwareService
{
    public Task<string> WhoAmIAsync(PingRequest request, CancellationToken ct)
    {
        if (User == null)
            return Task.FromResult("anonymous");
        var name = User.Identity?.Name ?? "unknown";
        var roles = string.Join(",", User.FindAll(c => c.Type == ClaimTypes.Role).Select(c => c.Value));
        return Task.FromResult($"{name}|{roles}");
    }
}

internal class MultiRoleService : ServiceBase, IMultiRoleService
{
    public Task<string> ExecuteAsync(PingRequest request, CancellationToken ct) =>
        Task.FromResult($"ok:{request.Value}");
}

internal class AdvancedService : ServiceBase, IAdvancedService
{
    public Task<bool> ThrowAsync(CancellationToken ct) =>
        throw new InvalidOperationException("Intentional error for testing.");

    public Task<string> EchoHeaderAsync(EchoHeaderRequest request, CancellationToken ct)
    {
        Context.Headers.TryGetValue(request.HeaderName, out var value);
        return Task.FromResult(value ?? "");
    }

    public async Task<int> DelayAsync(DelayRequest request, CancellationToken ct)
    {
        await Task.Delay(request.Ms, ct);
        return request.Ms;
    }
}
