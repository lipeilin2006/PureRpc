using System.Buffers;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using PureRpc.Abstractions;

namespace PureRpc.Test;

internal sealed class MockServerTransport : IServerTransport
{
    public Task StartAsync(Func<RpcContext, ReadOnlySequence<byte>, Task> onRequestReceived, CancellationToken ct)
        => Task.CompletedTask;
    public ValueTask SendResponseAsync(RpcContext context, CancellationToken ct) => default;
    public ValueTask DisposeAsync() => default;
}

internal sealed class MockClientTransport : IClientTransport
{
    public bool IsConnected => true;
    public Task ConnectAsync(Action<uint, ReadOnlySequence<byte>, bool, string?, IReadOnlyDictionary<string, string>?> onResponseReceived, CancellationToken ct) => Task.CompletedTask;
    public ValueTask SendAsync(uint requestId, string serviceName, string methodName, ReadOnlySequence<byte> data, CancellationToken ct, IDictionary<string, string>? headers = null) => default;
    public ValueTask CancelRequestAsync(uint requestId, CancellationToken ct = default) => default;
    public ValueTask DisposeAsync() => default;
}

internal sealed class MockSerializer : ISerializer
{
    public void Serialize<T>(IBufferWriter<byte> writer, T value) { }
    public T Deserialize<T>(ReadOnlySequence<byte> sequence) => default!;
}

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

internal sealed class TestAuthHandler : IAuthorizationHandler
{
    public ValueTask AuthorizeAsync(RpcContext context, string? policy, string? roles, CancellationToken ct)
    {
        context.User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.Name, "test") }, "test"));
        return default;
    }
}

internal class NoopServerInterceptor : IRpcServerInterceptor
{
    public ValueTask InvokeAsync(RpcContext context, ReadOnlySequence<byte> payload, RpcRequestDelegate next)
        => next(context, payload);
}

internal class NoopClientInterceptor : IRpcClientInterceptor
{
    public ValueTask<ReadOnlySequence<byte>> InvokeAsync(
        string serviceName, string methodName, ReadOnlySequence<byte> requestPayload,
        CancellationToken ct, IDictionary<string, string>? headers, RpcCallDelegate next)
        => next(serviceName, methodName, requestPayload, ct, headers);
}
