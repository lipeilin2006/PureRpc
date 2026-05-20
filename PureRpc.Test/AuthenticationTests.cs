using System.Buffers;
using System.Security.Claims;
using MemoryPack;
using Microsoft.Extensions.DependencyInjection;
using PureRpc.Abstractions;

namespace PureRpc.Test;

// ============================================================
// AuthorizeAttribute & AllowAnonymousAttribute unit tests
// ============================================================

public sealed class AuthorizeAttributeTests
{
    [Fact]
    public void Constructor_NoArgs_PolicyIsNull()
    {
        var attr = new AuthorizeAttribute();
        Assert.Null(attr.Policy);
    }

    [Fact]
    public void Constructor_WithPolicy_SetsPolicy()
    {
        var attr = new AuthorizeAttribute("admin");
        Assert.Equal("admin", attr.Policy);
    }

    [Fact]
    public void Roles_CanBeSet()
    {
        var attr = new AuthorizeAttribute { Roles = "admin,user" };
        Assert.Equal("admin,user", attr.Roles);
    }

    [Fact]
    public void Roles_DefaultIsNull()
    {
        var attr = new AuthorizeAttribute();
        Assert.Null(attr.Roles);
    }

    [Fact]
    public void AllowMultiple_IsTrue()
    {
        var usage = (AttributeUsageAttribute)typeof(AuthorizeAttribute)
            .GetCustomAttributes(typeof(AttributeUsageAttribute), false)[0];
        Assert.True(usage.AllowMultiple);
    }

    [Fact]
    public void Targets_InterfaceAndMethod()
    {
        var usage = (AttributeUsageAttribute)typeof(AuthorizeAttribute)
            .GetCustomAttributes(typeof(AttributeUsageAttribute), false)[0];
        Assert.True((usage.ValidOn & AttributeTargets.Interface) != 0);
        Assert.True((usage.ValidOn & AttributeTargets.Method) != 0);
    }
}

public sealed class AllowAnonymousAttributeTests
{
    [Fact]
    public void AllowMultiple_IsFalse()
    {
        var usage = (AttributeUsageAttribute)typeof(AllowAnonymousAttribute)
            .GetCustomAttributes(typeof(AttributeUsageAttribute), false)[0];
        Assert.False(usage.AllowMultiple);
    }

    [Fact]
    public void Targets_MethodOnly()
    {
        var usage = (AttributeUsageAttribute)typeof(AllowAnonymousAttribute)
            .GetCustomAttributes(typeof(AttributeUsageAttribute), false)[0];
        Assert.Equal(AttributeTargets.Method, usage.ValidOn);
    }
}

// ============================================================
// Dispatcher-level authorization integration tests
// ============================================================

internal sealed class TestAuthorizationHandler : IAuthorizationHandler
{
    public bool ShouldSucceed = true;
    public int CallCount;
    public string? ReceivedPolicy;
    public string? ReceivedRoles;
    public RpcContext? ReceivedContext;
    public ClaimsPrincipal? UserToSet;

    public ValueTask AuthorizeAsync(RpcContext context, string? policy, string? roles, CancellationToken ct)
    {
        CallCount++;
        ReceivedPolicy = policy;
        ReceivedRoles = roles;
        ReceivedContext = context;
        if (!ShouldSucceed)
            throw new RpcException("Authorization failed.");
        if (UserToSet != null)
            context.User = UserToSet;
        return default;
    }
}

internal sealed class MockServerTransport : IServerTransport
{
    public Task StartAsync(Func<RpcContext, ReadOnlySequence<byte>, Task> onRequestReceived, CancellationToken ct)
        => Task.CompletedTask;
    public ValueTask SendResponseAsync(RpcContext context, CancellationToken ct) => default;
    public ValueTask DisposeAsync() => default;
}

/// <summary>
/// Concrete AuthorizationHandlerBase for testing — resolves principal from a delegate.
/// </summary>
internal sealed class ClaimsAuthHandler : AuthorizationHandlerBase
{
    private readonly Func<RpcContext, CancellationToken, ValueTask<ClaimsPrincipal?>> _resolver;

    public ClaimsAuthHandler(Func<RpcContext, CancellationToken, ValueTask<ClaimsPrincipal?>> resolver)
    {
        _resolver = resolver;
    }

    public ClaimsAuthHandler(Func<RpcContext, ClaimsPrincipal?> resolver)
        : this((ctx, _) => new ValueTask<ClaimsPrincipal?>(resolver(ctx))) { }

    protected override ValueTask<ClaimsPrincipal?> ResolvePrincipalAsync(RpcContext context, CancellationToken ct)
        => _resolver(context, ct);
}

public sealed class AuthorizationFlowTests
{
    private readonly byte[] _pingPayload;

    public AuthorizationFlowTests()
    {
        _pingPayload = MemoryPackSerializer.Serialize(new PingRequest("hello"));
    }

    // ---- Helpers ----

    private ServiceProvider BuildServer(TestAuthorizationHandler? handler = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IServerTransport>(new MockServerTransport());
        services.AddPureRpcServer()
            .WithMemoryPackSerializer()
            .WithAuthService<AuthService>()
            .WithMixedAuthService<MixedAuthService>()
            .WithPolicyAuthService<PolicyAuthService>()
            .WithAllowAnonService<AllowAnonService>()
            .WithUserAwareService<UserAwareService>();
        if (handler != null)
            services.AddSingleton<IAuthorizationHandler>(handler);
        return services.BuildServiceProvider();
    }

    private static IServiceDispatcher GetDispatcher(ServiceProvider sp, string serviceName)
        => sp.GetServices<IServiceDispatcher>().First(d => d.ServiceName == serviceName);

    private async Task<string?> DispatchAndReadAsync(IServiceDispatcher dispatcher, string methodName)
    {
        var buffer = new ArrayBufferWriter<byte>();
        var context = new RpcContext(buffer) { MethodName = methodName };

        await dispatcher.DispatchAsync(methodName, new ReadOnlySequence<byte>(_pingPayload), context);

        return MemoryPackSerializer.Deserialize<string>(buffer.WrittenSpan);
    }

    // ---- Service-level [Authorize] ----

    [Fact]
    public async Task ServiceAuth_HandlerAccepts_Succeeds()
    {
        var handler = new TestAuthorizationHandler { ShouldSucceed = true };
        using var sp = BuildServer(handler);
        var dispatcher = GetDispatcher(sp, "AuthService");

        var result = await DispatchAndReadAsync(dispatcher, "Ping");

        Assert.Equal("hello", result);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task ServiceAuth_HandlerRejects_ThrowsRpcException()
    {
        var handler = new TestAuthorizationHandler { ShouldSucceed = false };
        using var sp = BuildServer(handler);
        var dispatcher = GetDispatcher(sp, "AuthService");

        var ex = await Assert.ThrowsAsync<RpcException>(() =>
            DispatchAndReadAsync(dispatcher, "Ping"));
        Assert.Contains("Authorization failed", ex.Message);
    }

    [Fact]
    public async Task ServiceAuth_NoHandler_ThrowsInvalidOperation()
    {
        using var sp = BuildServer();
        var dispatcher = GetDispatcher(sp, "AuthService");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            DispatchAndReadAsync(dispatcher, "Ping"));
        Assert.Contains("IAuthorizationHandler", ex.Message);
    }

    // ---- Method-level [Authorize] (MixedAuthService) ----

    [Fact]
    public async Task MixedAuth_PublicMethod_SucceedsWithoutHandler()
    {
        using var sp = BuildServer();
        var dispatcher = GetDispatcher(sp, "MixedAuthService");

        var result = await DispatchAndReadAsync(dispatcher, "Open");

        Assert.Equal("open:hello", result);
    }

    [Fact]
    public async Task MixedAuth_SecuredMethod_ThrowsWithoutHandler()
    {
        using var sp = BuildServer();
        var dispatcher = GetDispatcher(sp, "MixedAuthService");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            DispatchAndReadAsync(dispatcher, "Secured"));
        Assert.Contains("IAuthorizationHandler", ex.Message);
    }

    [Fact]
    public async Task MixedAuth_SecuredMethod_SucceedsWithHandler()
    {
        var handler = new TestAuthorizationHandler { ShouldSucceed = true };
        using var sp = BuildServer(handler);
        var dispatcher = GetDispatcher(sp, "MixedAuthService");

        var result = await DispatchAndReadAsync(dispatcher, "Secured");

        Assert.Equal("secured:hello", result);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task MixedAuth_PublicMethod_DoesNotCallHandler()
    {
        var handler = new TestAuthorizationHandler { ShouldSucceed = true };
        using var sp = BuildServer(handler);
        var dispatcher = GetDispatcher(sp, "MixedAuthService");

        var result = await DispatchAndReadAsync(dispatcher, "Open");

        Assert.Equal("open:hello", result);
        Assert.Equal(0, handler.CallCount);
    }

    // ---- [AllowAnonymous] override (AllowAnonService) ----

    [Fact]
    public async Task AllowAnon_PublicMethod_SucceedsWithoutHandler()
    {
        using var sp = BuildServer();
        var dispatcher = GetDispatcher(sp, "AllowAnonService");

        var result = await DispatchAndReadAsync(dispatcher, "Public");

        Assert.Equal("public:hello", result);
    }

    [Fact]
    public async Task AllowAnon_SecuredMethod_ThrowsWithoutHandler()
    {
        using var sp = BuildServer();
        var dispatcher = GetDispatcher(sp, "AllowAnonService");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            DispatchAndReadAsync(dispatcher, "Secured"));
        Assert.Contains("IAuthorizationHandler", ex.Message);
    }

    [Fact]
    public async Task AllowAnon_PublicMethod_DoesNotCallHandler_WithHandler()
    {
        var handler = new TestAuthorizationHandler { ShouldSucceed = true };
        using var sp = BuildServer(handler);
        var dispatcher = GetDispatcher(sp, "AllowAnonService");

        var result = await DispatchAndReadAsync(dispatcher, "Public");

        Assert.Equal("public:hello", result);
        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public async Task AllowAnon_SecuredMethod_CallsHandler()
    {
        var handler = new TestAuthorizationHandler { ShouldSucceed = true };
        using var sp = BuildServer(handler);
        var dispatcher = GetDispatcher(sp, "AllowAnonService");

        var result = await DispatchAndReadAsync(dispatcher, "Secured");

        Assert.Equal("secured:hello", result);
        Assert.Equal(1, handler.CallCount);
    }

    // ---- Policy verification ----

    [Fact]
    public async Task ServiceAuth_HandlerReceivesNullPolicy()
    {
        var handler = new TestAuthorizationHandler();
        using var sp = BuildServer(handler);
        var dispatcher = GetDispatcher(sp, "AuthService");

        await DispatchAndReadAsync(dispatcher, "Ping");

        Assert.Null(handler.ReceivedPolicy);
    }

    [Fact]
    public async Task PolicyAuth_HandlerReceivesCorrectPolicy()
    {
        var handler = new TestAuthorizationHandler();
        using var sp = BuildServer(handler);
        var dispatcher = GetDispatcher(sp, "PolicyAuthService");

        await DispatchAndReadAsync(dispatcher, "Execute");

        Assert.Equal("admin-policy", handler.ReceivedPolicy);
    }

    [Fact]
    public async Task MixedAuth_SecuredMethod_HandlerReceivesMethodPolicy()
    {
        var handler = new TestAuthorizationHandler();
        using var sp = BuildServer(handler);
        var dispatcher = GetDispatcher(sp, "MixedAuthService");

        await DispatchAndReadAsync(dispatcher, "Secured");

        Assert.Equal("admin", handler.ReceivedPolicy);
    }

    // ---- Context delivery ----

    [Fact]
    public async Task ServiceAuth_HandlerReceivesContext()
    {
        var handler = new TestAuthorizationHandler();
        using var sp = BuildServer(handler);
        var dispatcher = GetDispatcher(sp, "AuthService");

        await DispatchAndReadAsync(dispatcher, "Ping");

        Assert.NotNull(handler.ReceivedContext);
        Assert.Equal("Ping", handler.ReceivedContext!.MethodName);
    }

    // ---- User propagation (RpcContext.User / ServiceBase.User) ----

    [Fact]
    public async Task ServiceAuth_HandlerSetsUser_ServiceReadsIt()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.Name, "alice"),
            new Claim(ClaimTypes.Role, "admin"),
        }, "test"));
        var handler = new TestAuthorizationHandler { UserToSet = principal };
        using var sp = BuildServer(handler);
        var dispatcher = GetDispatcher(sp, "UserAwareService");

        var result = await DispatchAndReadAsync(dispatcher, "WhoAmI");

        Assert.Equal("alice|admin", result);
    }

    [Fact]
    public async Task ServiceAuth_HandlerDoesNotSetUser_UserIsNull()
    {
        var handler = new TestAuthorizationHandler { ShouldSucceed = true };
        using var sp = BuildServer(handler);
        var dispatcher = GetDispatcher(sp, "UserAwareService");

        var result = await DispatchAndReadAsync(dispatcher, "WhoAmI");

        Assert.Equal("anonymous", result);
    }
}

// ============================================================
// AuthorizationHandlerBase integration tests
// ============================================================

public sealed class AuthorizationHandlerBaseTests
{
    private ServiceProvider BuildServer(AuthorizationHandlerBase handler)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IServerTransport>(new MockServerTransport());
        services.AddPureRpcServer()
            .WithMemoryPackSerializer()
            .WithUserAwareService<UserAwareService>();
        services.AddSingleton<IAuthorizationHandler>(handler);
        return services.BuildServiceProvider();
    }

    private static IServiceDispatcher GetDispatcher(ServiceProvider sp)
        => sp.GetServices<IServiceDispatcher>().First(d => d.ServiceName == "UserAwareService");

    private static async Task<string?> DispatchAsync(IServiceDispatcher dispatcher)
    {
        var payload = MemoryPackSerializer.Serialize(new PingRequest("test"));
        var buffer = new ArrayBufferWriter<byte>();
        var context = new RpcContext(buffer) { MethodName = "WhoAmI" };
        await dispatcher.DispatchAsync("WhoAmI", new ReadOnlySequence<byte>(payload), context);
        return MemoryPackSerializer.Deserialize<string>(buffer.WrittenSpan);
    }

    [Fact]
    public async Task ResolvePrincipal_ReturnsNull_ThrowsUnauthorizedAccess()
    {
        var handler = new ClaimsAuthHandler(_ => (ClaimsPrincipal?)null);
        using var sp = BuildServer(handler);
        var dispatcher = GetDispatcher(sp);

        var ex = await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            DispatchAsync(dispatcher));
        Assert.Contains("Authentication failed", ex.Message);
    }

    [Fact]
    public async Task ResolvePrincipal_Unauthenticated_ThrowsUnauthorizedAccess()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity()); // no authentication
        var handler = new ClaimsAuthHandler(_ => principal);
        using var sp = BuildServer(handler);
        var dispatcher = GetDispatcher(sp);

        var ex = await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            DispatchAsync(dispatcher));
        Assert.Contains("Authentication failed", ex.Message);
    }

    [Fact]
    public async Task RoleCheck_Success_SetsUserAndInvokesService()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.Name, "bob"),
            new Claim(ClaimTypes.Role, "admin"),
        }, "test"));
        var handler = new ClaimsAuthHandler(_ => principal);
        using var sp = BuildServer(handler);
        var dispatcher = GetDispatcher(sp);

        var result = await DispatchAsync(dispatcher);

        Assert.Equal("bob|admin", result);
    }

    [Fact]
    public async Task PolicyCheck_Success_SetsUserAndInvokesService()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.Name, "carol"),
            new Claim("Permission", "read-only"),
        }, "test"));
        var handler = new ClaimsAuthHandler(_ => principal);
        using var sp = BuildServer(handler);
        var dispatcher = GetDispatcher(sp);

        var result = await DispatchAsync(dispatcher);

        Assert.Equal("carol|", result);
    }

    // ---- Multi-role (OR logic) ----

    private ServiceProvider BuildMultiRoleServer(AuthorizationHandlerBase handler)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IServerTransport>(new MockServerTransport());
        services.AddPureRpcServer()
            .WithMemoryPackSerializer()
            .WithMultiRoleService<MultiRoleService>();
        services.AddSingleton<IAuthorizationHandler>(handler);
        return services.BuildServiceProvider();
    }

    private static async Task<string?> DispatchMultiRoleAsync(IServiceDispatcher dispatcher)
    {
        var payload = MemoryPackSerializer.Serialize(new PingRequest("test"));
        var buffer = new ArrayBufferWriter<byte>();
        var context = new RpcContext(buffer) { MethodName = "Execute" };
        await dispatcher.DispatchAsync("Execute", new ReadOnlySequence<byte>(payload), context);
        return MemoryPackSerializer.Deserialize<string>(buffer.WrittenSpan);
    }

    [Fact]
    public async Task MultiRole_UserHasFirstRole_Succeeds()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.Name, "alice"),
            new Claim(ClaimTypes.Role, "admin"),
        }, "test"));
        var handler = new ClaimsAuthHandler(_ => principal);
        using var sp = BuildMultiRoleServer(handler);
        var dispatcher = sp.GetServices<IServiceDispatcher>()
            .First(d => d.ServiceName == "MultiRoleService");

        var result = await DispatchMultiRoleAsync(dispatcher);

        Assert.Equal("ok:test", result);
    }

    [Fact]
    public async Task MultiRole_UserHasSecondRole_Succeeds()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.Name, "bob"),
            new Claim(ClaimTypes.Role, "manager"),
        }, "test"));
        var handler = new ClaimsAuthHandler(_ => principal);
        using var sp = BuildMultiRoleServer(handler);
        var dispatcher = sp.GetServices<IServiceDispatcher>()
            .First(d => d.ServiceName == "MultiRoleService");

        var result = await DispatchMultiRoleAsync(dispatcher);

        Assert.Equal("ok:test", result);
    }

    [Fact]
    public async Task MultiRole_UserHasAllRoles_Succeeds()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.Name, "carol"),
            new Claim(ClaimTypes.Role, "admin"),
            new Claim(ClaimTypes.Role, "manager"),
            new Claim(ClaimTypes.Role, "user"),
        }, "test"));
        var handler = new ClaimsAuthHandler(_ => principal);
        using var sp = BuildMultiRoleServer(handler);
        var dispatcher = sp.GetServices<IServiceDispatcher>()
            .First(d => d.ServiceName == "MultiRoleService");

        var result = await DispatchMultiRoleAsync(dispatcher);

        Assert.Equal("ok:test", result);
    }

    [Fact]
    public async Task MultiRole_UserHasNoRequiredRole_Fails()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.Name, "dave"),
            new Claim(ClaimTypes.Role, "user"),
        }, "test"));
        var handler = new ClaimsAuthHandler(_ => principal);
        using var sp = BuildMultiRoleServer(handler);
        var dispatcher = sp.GetServices<IServiceDispatcher>()
            .First(d => d.ServiceName == "MultiRoleService");

        var ex = await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            DispatchMultiRoleAsync(dispatcher));
        Assert.Contains("any of the required roles", ex.Message);
    }

    // ---- DelegatingAuthorizationHandler ----

    [Fact]
    public async Task DelegatingHandler_AsyncResolver_Succeeds()
    {
        var handler = new DelegatingAuthorizationHandler((ctx, ct) =>
        {
            var principal = new ClaimsPrincipal(new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.Name, "dave"),
                new Claim(ClaimTypes.Role, "admin"),
            }, "test"));
            return new ValueTask<ClaimsPrincipal?>(principal);
        });
        using var sp = BuildServer(handler);
        var dispatcher = GetDispatcher(sp);

        var result = await DispatchAsync(dispatcher);

        Assert.Equal("dave|admin", result);
    }

    [Fact]
    public async Task DelegatingHandler_SyncResolver_Succeeds()
    {
        var handler = new DelegatingAuthorizationHandler(ctx =>
        {
            return new ClaimsPrincipal(new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.Name, "eve"),
                new Claim(ClaimTypes.Role, "user"),
            }, "test"));
        });
        using var sp = BuildServer(handler);
        var dispatcher = GetDispatcher(sp);

        var result = await DispatchAsync(dispatcher);

        Assert.Equal("eve|user", result);
    }
}

// ============================================================
// AuthorizationExtensions DI integration tests
// ============================================================

public sealed class AuthorizationExtensionsTests
{
    [Fact]
    public void AddAuthorization_T_ServicesContainHandler()
    {
        var services = new ServiceCollection();
        services.AddPureRpcServer()
            .AddAuthorization<TestAuthHandler>();

        var sp = services.BuildServiceProvider();
        var handler = sp.GetService<IAuthorizationHandler>();
        Assert.IsType<TestAuthHandler>(handler);
    }

    [Fact]
    public async Task AddAuthorization_FuncInline_ResolvesPrincipal()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IServerTransport>(new MockServerTransport());
        services.AddPureRpcServer()
            .WithMemoryPackSerializer()
            .WithUserAwareService<UserAwareService>()
            .AddAuthorization(ctx =>
            {
                return new ClaimsPrincipal(new ClaimsIdentity(new[]
                {
                    new Claim(ClaimTypes.Name, "inline"),
                    new Claim(ClaimTypes.Role, "admin"),
                }, "test"));
            });
        using var sp = services.BuildServiceProvider();
        var dispatcher = sp.GetServices<IServiceDispatcher>()
            .First(d => d.ServiceName == "UserAwareService");

        var payload = MemoryPackSerializer.Serialize(new PingRequest("test"));
        var buffer = new ArrayBufferWriter<byte>();
        var context = new RpcContext(buffer) { MethodName = "WhoAmI" };
        await dispatcher.DispatchAsync("WhoAmI", new ReadOnlySequence<byte>(payload), context);

        var result = MemoryPackSerializer.Deserialize<string>(buffer.WrittenSpan);
        Assert.Equal("inline|admin", result);
    }

    [Fact]
    public async Task AddAuthorization_FuncInline_SetsUserOnContext()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IServerTransport>(new MockServerTransport());
        services.AddPureRpcServer()
            .WithMemoryPackSerializer()
            .WithUserAwareService<UserAwareService>()
            .AddAuthorization((ctx, ct) =>
            {
                var principal = new ClaimsPrincipal(new ClaimsIdentity(new[]
                {
                    new Claim(ClaimTypes.Name, "from-header"),
                    new Claim(ClaimTypes.Role, "user"),
                }, "bearer"));
                return new ValueTask<ClaimsPrincipal?>(principal);
            });
        using var sp = services.BuildServiceProvider();
        var dispatcher = sp.GetServices<IServiceDispatcher>()
            .First(d => d.ServiceName == "UserAwareService");

        var payload = MemoryPackSerializer.Serialize(new PingRequest("test"));
        var buffer = new ArrayBufferWriter<byte>();
        var context = new RpcContext(buffer) { MethodName = "WhoAmI" };
        context.Headers["Authorization"] = "Bearer test-token";
        await dispatcher.DispatchAsync("WhoAmI", new ReadOnlySequence<byte>(payload), context);

        var result = MemoryPackSerializer.Deserialize<string>(buffer.WrittenSpan);
        Assert.Equal("from-header|user", result);
    }
}

internal sealed class TestAuthHandler : IAuthorizationHandler
{
    public ValueTask AuthorizeAsync(RpcContext context, string? policy, string? roles, CancellationToken ct)
    {
        context.User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.Name, "test") }, "test"));
        return default;
    }
}
