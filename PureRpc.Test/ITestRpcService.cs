using MemoryPack;
using PureRpc.Abstractions;

namespace PureRpc.Test;

[MemoryPackable]
public partial record AddRequest(int A, int B);

[MemoryPackable]
public partial record EchoRequest(string Message);

[MemoryPackable]
public partial record EchoHeaderRequest(string HeaderName);

[MemoryPackable]
public partial record DelayRequest(int Ms);

[RpcService("CalcService")]
public interface ICalcService
{
    [RpcMethod("Add")]
    Task<int> AddAsync(AddRequest request, CancellationToken ct = default);

    [RpcMethod("Echo")]
    Task<string> EchoAsync(EchoRequest request, CancellationToken ct = default);
}

[RpcService("AdvancedService")]
public interface IAdvancedService
{
    [RpcMethod("Throw")]
    Task<bool> ThrowAsync(CancellationToken ct = default);

    [RpcMethod("EchoHeader")]
    Task<string> EchoHeaderAsync(EchoHeaderRequest request, CancellationToken ct = default);

    [RpcMethod("Delay")]
    Task<int> DelayAsync(DelayRequest request, CancellationToken ct = default);
}

// --- Authorization test services ---

[MemoryPackable]
public partial record PingRequest(string Value);

/// <summary>
/// Service with service-level [Authorize] (all methods require auth).
/// </summary>
[RpcService("AuthService")]
[Authorize]
public interface IAuthService
{
    [RpcMethod("Ping")]
    Task<string> PingAsync(PingRequest request, CancellationToken ct = default);
}

/// <summary>
/// Service where only one method has [Authorize]; others are public.
/// </summary>
[RpcService("MixedAuthService")]
public interface IMixedAuthService
{
    [RpcMethod("Open")]
    Task<string> OpenAsync(PingRequest request, CancellationToken ct = default);

    [RpcMethod("Secured")]
    [Authorize("admin")]
    Task<string> SecuredAsync(PingRequest request, CancellationToken ct = default);
}

/// <summary>
/// Service with policy-based authorization.
/// </summary>
[RpcService("PolicyAuthService")]
[Authorize("admin-policy")]
public interface IPolicyAuthService
{
    [RpcMethod("Execute")]
    Task<string> ExecuteAsync(PingRequest request, CancellationToken ct = default);
}

/// <summary>
/// Service with [Authorize] at service level and [AllowAnonymous] on one method.
/// </summary>
[RpcService("AllowAnonService")]
[Authorize]
public interface IAllowAnonService
{
    [RpcMethod("Public")]
    [AllowAnonymous]
    Task<string> PublicAsync(PingRequest request, CancellationToken ct = default);

    [RpcMethod("Secured")]
    Task<string> SecuredAsync(PingRequest request, CancellationToken ct = default);
}

/// <summary>
/// Service that reads the current user via ServiceBase.User, for testing user propagation.
/// </summary>
[RpcService("UserAwareService")]
[Authorize]
public interface IUserAwareService
{
    [RpcMethod("WhoAmI")]
    Task<string> WhoAmIAsync(PingRequest request, CancellationToken ct = default);
}

/// <summary>
/// Service with [Authorize(Roles = "admin,manager")] — OR logic for multi-role.
/// </summary>
[RpcService("MultiRoleService")]
[Authorize(Roles = "admin,manager")]
public interface IMultiRoleService
{
    [RpcMethod("Execute")]
    Task<string> ExecuteAsync(PingRequest request, CancellationToken ct = default);
}
