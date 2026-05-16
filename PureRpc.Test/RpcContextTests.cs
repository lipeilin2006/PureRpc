using System.Buffers;
using Microsoft.Extensions.ObjectPool;
using PureRpc.Abstractions;

namespace PureRpc.Test;

public sealed class RpcContextTests
{
    [Fact]
    public void Constructor_SetsResponseBuffer()
    {
        var buffer = new ArrayBufferWriter<byte>();
        var ctx = new RpcContext(buffer);
        Assert.Same(buffer, ctx.ResponseBuffer);
    }

    [Fact]
    public void Constructor_ThrowsOnNullBuffer()
    {
        Assert.Throws<ArgumentNullException>(() => new RpcContext(null!));
    }

    [Fact]
    public void DefaultValues_AreCorrect()
    {
        var ctx = new RpcContext(new ArrayBufferWriter<byte>());
        Assert.Equal(0L, ctx.ConnectionId);
        Assert.Equal(0u, ctx.RequestId);
        Assert.Equal("", ctx.ServiceName);
        Assert.Equal("", ctx.MethodName);
        Assert.Null(ctx.RemoteEndPoint);
        Assert.False(ctx.IsAborted);
        Assert.Equal(CancellationToken.None, ctx.CancellationToken);
        Assert.Empty(ctx.Headers);
    }

    [Fact]
    public void Abort_SetsIsAborted()
    {
        var ctx = new RpcContext(new ArrayBufferWriter<byte>());
        Assert.False(ctx.IsAborted);
        ctx.Abort();
        Assert.True(ctx.IsAborted);
    }

    [Fact]
    public void Headers_CanBeAdded()
    {
        var ctx = new RpcContext(new ArrayBufferWriter<byte>());
        ctx.Headers["key1"] = "val1";
        ctx.Headers["key2"] = "val2";
        Assert.Equal(2, ctx.Headers.Count);
        Assert.Equal("val1", ctx.Headers["key1"]);
    }

    [Fact]
    public void Reset_ClearsAllProperties()
    {
        var ctx = new RpcContext(new ArrayBufferWriter<byte>());
        ctx.ConnectionId = 100;
        ctx.RequestId = 50;
        ctx.ServiceName = "TestService";
        ctx.MethodName = "TestMethod";
        ctx.Abort();
        ctx.Headers["h"] = "v";

        ctx.Reset();

        Assert.Equal(0L, ctx.ConnectionId);
        Assert.Equal(0u, ctx.RequestId);
        Assert.Equal("", ctx.ServiceName);
        Assert.Equal("", ctx.MethodName);
        Assert.Null(ctx.RemoteEndPoint);
        Assert.False(ctx.IsAborted);
        Assert.Empty(ctx.Headers);
    }

    [Fact]
    public void Properties_AreSettable()
    {
        var ctx = new RpcContext(new ArrayBufferWriter<byte>());
        ctx.ConnectionId = 42;
        ctx.RequestId = 7;
        ctx.ServiceName = "Svc";
        ctx.MethodName = "Mtd";
        ctx.CancellationToken = new CancellationToken(true);

        Assert.Equal(42L, ctx.ConnectionId);
        Assert.Equal(7u, ctx.RequestId);
        Assert.Equal("Svc", ctx.ServiceName);
        Assert.Equal("Mtd", ctx.MethodName);
        Assert.True(ctx.CancellationToken.IsCancellationRequested);
    }
}

public sealed class RpcContextPolicyTests
{
    [Fact]
    public void Create_ReturnsContextWithBuffer()
    {
        var policy = new RpcContextPolicy();
        var ctx = policy.Create();
        Assert.NotNull(ctx);
        Assert.NotNull(ctx.ResponseBuffer);
        Assert.IsType<ArrayBufferWriter<byte>>(ctx.ResponseBuffer);
    }

    [Fact]
    public void Return_NormalContext_ReturnsTrue()
    {
        var policy = new RpcContextPolicy();
        var ctx = policy.Create();
        var result = policy.Return(ctx);
        Assert.True(result);
        Assert.Equal(0L, ctx.ConnectionId);
    }

    [Fact]
    public void Return_ContextWithHugeBuffer_ReturnsFalse()
    {
        var policy = new RpcContextPolicy();
        var buffer = new ArrayBufferWriter<byte>(1024 * 1024 + 1);
        buffer.GetSpan(1024 * 1024 + 1);
        buffer.Advance(1024 * 1024 + 1);
        var ctx = new RpcContext(buffer);

        var result = policy.Return(ctx);

        Assert.False(result);
    }

    [Fact]
    public void ObjectPool_ReusesContexts()
    {
        var provider = new DefaultObjectPoolProvider { MaximumRetained = 10 };
        var policy = new RpcContextPolicy();
        var pool = provider.Create(policy);

        var ctx1 = pool.Get();
        ctx1.ConnectionId = 99;
        pool.Return(ctx1);

        var ctx2 = pool.Get();
        Assert.Equal(0L, ctx2.ConnectionId);
    }
}