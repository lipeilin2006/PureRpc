using PureRpc.Abstractions;

namespace PureRpc.Test;

public abstract class RpcTestBase : IAsyncLifetime
{
    private RpcTestFixture? _fixture;
    protected ICalcService Client => _fixture!.Client;
    protected IAdvancedService AdvancedClient => _fixture!.AdvancedClient;
    protected IRpcClient RawClient => _fixture!.RawClient;

    public async Task InitializeAsync()
    {
        _fixture = new RpcTestFixture();
        await _fixture.InitializeAsync();
    }

    public async Task DisposeAsync()
    {
        if (_fixture != null)
            await _fixture.DisposeAsync();
    }
}

public sealed class AddTests : RpcTestBase
{
    [Fact]
    public async Task Add_TwoNumbers_ReturnsSum()
    {
        var result = await Client.AddAsync(new AddRequest(3, 5));
        Assert.Equal(8, result);
    }

    [Fact]
    public async Task Add_NegativeNumbers_ReturnsSum()
    {
        var result = await Client.AddAsync(new AddRequest(-10, 7));
        Assert.Equal(-3, result);
    }
}

public sealed class EchoTests : RpcTestBase
{
    [Fact]
    public async Task Echo_String_ReturnsSame()
    {
        var result = await Client.EchoAsync(new EchoRequest("Hello PureRpc!"));
        Assert.Equal("Hello PureRpc!", result);
    }

    [Fact]
    public async Task Echo_EmptyString_ReturnsEmpty()
    {
        var result = await Client.EchoAsync(new EchoRequest(""));
        Assert.Equal("", result);
    }
}

public sealed class LoadTests : RpcTestBase
{
    [Fact]
    public async Task MultipleCalls_Sequential_Correct()
    {
        for (int i = 0; i < 100; i++)
        {
            var result = await Client.AddAsync(new AddRequest(i, i * 2));
            Assert.Equal(i + i * 2, result);
        }
    }

    [Fact]
    public async Task MultipleCalls_Concurrent_Correct()
    {
        var tasks = Enumerable.Range(0, 50).Select(i =>
            Client.AddAsync(new AddRequest(i, i * 10)));
        var results = await Task.WhenAll(tasks);

        for (int i = 0; i < 50; i++)
            Assert.Equal(i + i * 10, results[i]);
    }
}

public sealed class CancellationTests : RpcTestBase
{
    [Fact]
    public async Task CancellationToken_CancelledBeforeCall_Throws()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            Client.AddAsync(new AddRequest(1, 2), cts.Token));
    }
}

// --- All 4 serializer integration tests ---

public abstract class SerializerTestBase : IAsyncLifetime
{
    private RpcTestFixture? _fixture;
    protected ICalcService Client => _fixture!.Client;

    protected SerializerTestBase(SerializerType serializer) => Serializer = serializer;

    protected SerializerType Serializer { get; }

    public async Task InitializeAsync()
    {
        _fixture = new RpcTestFixture(TransportType.Tcp, Serializer);
        await _fixture.InitializeAsync();
    }

    public async Task DisposeAsync()
    {
        if (_fixture != null)
            await _fixture.DisposeAsync();
    }
}

public sealed class MemoryPackRpcTests : SerializerTestBase
{
    public MemoryPackRpcTests() : base(SerializerType.MemoryPack) { }

    [Fact]
    public async Task Add_Works() =>
        Assert.Equal(15, await Client.AddAsync(new AddRequest(7, 8)));

    [Fact]
    public async Task Echo_Works() =>
        Assert.Equal("mpack", await Client.EchoAsync(new EchoRequest("mpack")));
}

public sealed class JsonRpcTests : SerializerTestBase
{
    public JsonRpcTests() : base(SerializerType.Json) { }

    [Fact]
    public async Task Add_Works() =>
        Assert.Equal(15, await Client.AddAsync(new AddRequest(7, 8)));

    [Fact]
    public async Task Echo_Works() =>
        Assert.Equal("json", await Client.EchoAsync(new EchoRequest("json")));
}

// --- Advanced RPC scenarios ---

public abstract class AdvancedRpcTestBase : IAsyncLifetime
{
    private RpcTestFixture? _fixture;
    protected IAdvancedService AdvancedClient => _fixture!.AdvancedClient;
    protected IRpcClient RawClient => _fixture!.RawClient;

    public async Task InitializeAsync()
    {
        _fixture = new RpcTestFixture();
        await _fixture.InitializeAsync();
    }

    public async Task DisposeAsync()
    {
        if (_fixture != null)
            await _fixture.DisposeAsync();
    }
}

public sealed class ErrorHandlingTests : AdvancedRpcTestBase
{
    [Fact]
    public async Task ServerError_ThrowsRpcException()
    {
        var ex = await Assert.ThrowsAsync<RpcException>(() =>
            AdvancedClient.ThrowAsync());
        Assert.NotNull(ex);
    }
}

public sealed class DelayRpcTests : AdvancedRpcTestBase
{
    [Fact]
    public async Task Delay_ShortDelay_Works()
    {
        var result = await AdvancedClient.DelayAsync(new DelayRequest(10));
        Assert.Equal(10, result);
    }

    [Fact]
    public async Task Delay_Cancelled_Throws()
    {
        using var cts = new CancellationTokenSource(5);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            AdvancedClient.DelayAsync(new DelayRequest(10000), cts.Token));
    }
}

// --- Interceptor Integration Tests ---

public sealed class InterceptorIntegrationTests : IAsyncLifetime
{
    private RpcTestFixture? _fixture;
    protected ICalcService Client => _fixture!.Client;

    public async Task InitializeAsync()
    {
        CallCounter.Reset();
        _fixture = new RpcTestFixture();
        await _fixture.WithInterceptor<TestClientInterceptor, TestServerInterceptor>();
    }

    public async Task DisposeAsync()
    {
        if (_fixture != null)
            await _fixture.DisposeAsync();
    }

    [Fact]
    public async Task ClientInterceptor_IsInvoked()
    {
        Assert.Equal(0, CallCounter.ClientCount);
        await Client.AddAsync(new AddRequest(1, 2));
        Assert.Equal(1, CallCounter.ClientCount);
    }

    [Fact]
    public async Task ServerInterceptor_IsInvoked()
    {
        Assert.Equal(0, CallCounter.ServerCount);
        await Client.AddAsync(new AddRequest(3, 4));
        Assert.Equal(1, CallCounter.ServerCount);
    }

    [Fact]
    public async Task BothInterceptors_InvokedOnEachCall()
    {
        await Client.AddAsync(new AddRequest(1, 1));
        await Client.AddAsync(new AddRequest(2, 2));
        Assert.Equal(2, CallCounter.ClientCount);
        Assert.Equal(2, CallCounter.ServerCount);
    }
}

// --- Default Headers Tests ---

public sealed class DefaultHeaderTests : AdvancedRpcTestBase
{
    [Fact]
    public async Task EchoHeader_ReturnsDefaultHeader()
    {
        RawClient.SetDefaultHeader("auth", "secret-token");
        RawClient.SetDefaultHeader("trace-id", "trace-123");
        var result = await AdvancedClient.EchoHeaderAsync(new EchoHeaderRequest("auth"));
        Assert.Equal("secret-token", result);
    }

    [Fact]
    public async Task EchoHeader_MissingHeader_ReturnsEmpty()
    {
        var result = await AdvancedClient.EchoHeaderAsync(new EchoHeaderRequest("non-existent"));
        Assert.Equal("", result);
    }

    [Fact]
    public void SetDefaultHeader_ThenRemove_Works()
    {
        RawClient.SetDefaultHeader("test-key", "test-val");
        Assert.True(RawClient.RemoveDefaultHeader("test-key"));
        Assert.False(RawClient.RemoveDefaultHeader("non-existent"));
    }

    [Fact]
    public void ClearDefaultHeaders_RemovesAll()
    {
        RawClient.SetDefaultHeader("k1", "v1");
        RawClient.SetDefaultHeader("k2", "v2");
        RawClient.ClearDefaultHeaders();
    }
}

// --- WebSocket Transport ---

public abstract class WebSocketTestBase : IAsyncLifetime
{
    private RpcTestFixture? _fixture;
    protected ICalcService Client => _fixture!.Client;

    public async Task InitializeAsync()
    {
        _fixture = new RpcTestFixture(TransportType.WebSocket);
        await _fixture.InitializeAsync();
    }

    public async Task DisposeAsync()
    {
        if (_fixture != null)
            await _fixture.DisposeAsync();
    }
}

public sealed class WebSocketAddTests : WebSocketTestBase
{
    [Fact]
    public async Task Add_TwoNumbers_ReturnsSum()
    {
        var result = await Client.AddAsync(new AddRequest(3, 5));
        Assert.Equal(8, result);
    }

    [Fact]
    public async Task Echo_String_ReturnsSame()
    {
        var result = await Client.EchoAsync(new EchoRequest("Hello WS!"));
        Assert.Equal("Hello WS!", result);
    }
}

