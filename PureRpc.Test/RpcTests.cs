namespace PureRpc.Test;

public abstract class RpcTestBase : IAsyncLifetime
{
    private RpcTestFixture? _fixture;
    protected ICalcService Client => _fixture!.Client;

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
