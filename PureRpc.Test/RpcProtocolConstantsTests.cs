using PureRpc.Abstractions;

namespace PureRpc.Test;

public sealed class RpcProtocolConstantsTests
{
    [Fact]
    public void MaxServiceNameLength_Is256()
    {
        Assert.Equal(256, RpcProtocolConstants.MaxServiceNameLength);
    }

    [Fact]
    public void MaxMethodNameLength_Is256()
    {
        Assert.Equal(256, RpcProtocolConstants.MaxMethodNameLength);
    }

    [Fact]
    public void MaxHeaderCount_Is64()
    {
        Assert.Equal(64, RpcProtocolConstants.MaxHeaderCount);
    }

    [Fact]
    public void MaxHeaderKeyLength_Is256()
    {
        Assert.Equal(256, RpcProtocolConstants.MaxHeaderKeyLength);
    }

    [Fact]
    public void MaxHeaderValueLength_Is4096()
    {
        Assert.Equal(4096, RpcProtocolConstants.MaxHeaderValueLength);
    }

    [Fact]
    public void MaxFrameSize_Is64MB()
    {
        Assert.Equal(64 * 1024 * 1024, RpcProtocolConstants.MaxFrameSize);
    }

    [Fact]
    public void DefaultObjectPoolMaxRetained_Is1024()
    {
        Assert.Equal(1024, RpcProtocolConstants.DefaultObjectPoolMaxRetained);
    }

    [Fact]
    public void DefaultRequestThrottleLimit_Is512()
    {
        Assert.Equal(512, RpcProtocolConstants.DefaultRequestThrottleLimit);
    }
}

public sealed class RpcMessageTypeTests
{
    [Fact]
    public void Request_Is1()
    {
        Assert.Equal(1, (byte)RpcMessageType.Request);
    }

    [Fact]
    public void Response_Is2()
    {
        Assert.Equal(2, (byte)RpcMessageType.Response);
    }

    [Fact]
    public void Error_Is3()
    {
        Assert.Equal(3, (byte)RpcMessageType.Error);
    }

    [Fact]
    public void Cancel_Is8()
    {
        Assert.Equal(8, (byte)RpcMessageType.Cancel);
    }

    [Fact]
    public void AllValues_AreDistinct()
    {
        var values = new byte[]
        {
            (byte)RpcMessageType.Request,
            (byte)RpcMessageType.Response,
            (byte)RpcMessageType.Error,
            (byte)RpcMessageType.Cancel
        };
        Assert.Equal(values.Length, values.Distinct().Count());
    }
}
