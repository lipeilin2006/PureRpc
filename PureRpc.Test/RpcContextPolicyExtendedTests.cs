using System.Buffers;
using Microsoft.Extensions.ObjectPool;
using PureRpc.Abstractions;

namespace PureRpc.Test;

public sealed class RpcContextPolicyExtendedTests
{
    [Fact]
    public void CreatePool_ReturnsWorkingObjectPool()
    {
        var pool = RpcContextPolicy.CreatePool();

        var ctx = pool.Get();
        Assert.NotNull(ctx);
        Assert.NotNull(ctx.ResponseBuffer);

        pool.Return(ctx);
        var ctx2 = pool.Get();
        Assert.NotNull(ctx2);
    }

    [Fact]
    public void CreatePool_WithCustomMaxRetained_ReturnsWorkingPool()
    {
        var pool = RpcContextPolicy.CreatePool(5);

        var ctx = pool.Get();
        Assert.NotNull(ctx);
        pool.Return(ctx);
    }

    [Fact]
    public void PoolContexts_AreProperlyResetOnReturn()
    {
        var pool = RpcContextPolicy.CreatePool();

        var ctx = pool.Get();
        ctx.ConnectionId = 42;
        ctx.RequestId = 99;
        ctx.ServiceName = "TestService";
        ctx.MethodName = "TestMethod";
        ctx.Abort();
        ctx.Headers["h"] = "v";
        ctx.Items["i"] = "item";

        pool.Return(ctx);

        var ctx2 = pool.Get();
        Assert.Equal(0L, ctx2.ConnectionId);
        Assert.Equal(0u, ctx2.RequestId);
        Assert.Equal("", ctx2.ServiceName);
        Assert.Equal("", ctx2.MethodName);
        Assert.False(ctx2.IsAborted);
        Assert.Empty(ctx2.Headers);
        Assert.Empty(ctx2.Items);
    }

    [Fact]
    public void CreatePool_MultipleGetReturn_CyclesCorrectly()
    {
        var pool = RpcContextPolicy.CreatePool(2);

        var ctx1 = pool.Get();
        var ctx2 = pool.Get();
        ctx1.ConnectionId = 100;
        ctx2.ConnectionId = 200;

        pool.Return(ctx1);
        pool.Return(ctx2);

        var ctx3 = pool.Get();
        var ctx4 = pool.Get();

        Assert.Equal(0L, ctx3.ConnectionId);
        Assert.Equal(0L, ctx4.ConnectionId);
    }

    [Fact]
    public void Create_ReturnsContextWith4KBuffer()
    {
        var policy = new RpcContextPolicy();
        var ctx = policy.Create();

        Assert.IsType<ArrayBufferWriter<byte>>(ctx.ResponseBuffer);
        var writer = (ArrayBufferWriter<byte>)ctx.ResponseBuffer;
        Assert.Equal(4096, writer.Capacity);
    }

    [Fact]
    public void Return_LargeBuffer_IsRejected()
    {
        var policy = new RpcContextPolicy();
        var bigBuffer = new ArrayBufferWriter<byte>(1024 * 1024 + 1);
        bigBuffer.GetSpan(1024 * 1024 + 1);
        bigBuffer.Advance(1024 * 1024 + 1);
        var ctx = new RpcContext(bigBuffer);

        var result = policy.Return(ctx);

        Assert.False(result);
    }

    [Fact]
    public void Return_NormalBuffer_IsAccepted()
    {
        var policy = new RpcContextPolicy();
        var ctx = policy.Create();

        var result = policy.Return(ctx);

        Assert.True(result);
    }
}
