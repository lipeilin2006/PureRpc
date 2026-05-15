namespace PureRpc.Test;

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
    [Fact(Skip = "WebSocket 需要管理员权限 (HttpListener urlacl)")]
    public async Task Add_TwoNumbers_ReturnsSum()
    {
        var result = await Client.AddAsync(new AddRequest(3, 5));
        Assert.Equal(8, result);
    }

    [Fact(Skip = "WebSocket 需要管理员权限")]
    public async Task Echo_String_ReturnsSame()
    {
        var result = await Client.EchoAsync(new EchoRequest("Hello WS!"));
        Assert.Equal("Hello WS!", result);
    }
}
