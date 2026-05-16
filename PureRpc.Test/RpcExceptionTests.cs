namespace PureRpc.Test;

public sealed class RpcExceptionTests
{
    [Fact]
    public void Constructor_MessageOnly_SetsMessage()
    {
        var ex = new RpcException("test error");
        Assert.Equal("test error", ex.Message);
        Assert.Null(ex.RequestId);
        Assert.Null(ex.ErrorData);
    }

    [Fact]
    public void Constructor_MessageAndInnerEx_SetsBoth()
    {
        var inner = new InvalidOperationException("inner");
        var ex = new RpcException("outer", inner);
        Assert.Equal("outer", ex.Message);
        Assert.Same(inner, ex.InnerException);
    }

    [Fact]
    public void Constructor_WithRequestId_SetsRequestId()
    {
        var ex = new RpcException("error", 42UL);
        Assert.Equal(42UL, ex.RequestId);
        Assert.Null(ex.ErrorData);
    }

    [Fact]
    public void Constructor_WithRequestIdAndErrorData_SetsBoth()
    {
        var data = new byte[] { 1, 2, 3 };
        var ex = new RpcException("error", 99UL, data);
        Assert.Equal(99UL, ex.RequestId);
        Assert.Equal(data, ex.ErrorData);
    }

    [Fact]
    public void IsAssignableFromException()
    {
        var ex = new RpcException("test");
        Assert.IsAssignableFrom<Exception>(ex);
    }

    [Fact]
    public async Task ThrownAndCaught_AsRpcException()
    {
        var tcs = new TaskCompletionSource<int>();
        tcs.SetException(new RpcException("remote error", 1UL));
        var ex = await Assert.ThrowsAsync<RpcException>(() => tcs.Task);
        Assert.Equal("remote error", ex.Message);
        Assert.Equal(1UL, ex.RequestId);
    }
}