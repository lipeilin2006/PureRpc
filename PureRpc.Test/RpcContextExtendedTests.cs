using System.Buffers;
using System.Net;
using PureRpc.Abstractions;

namespace PureRpc.Test;

public sealed class RpcContextExtendedTests
{
    [Fact]
    public void PopulateRequest_SetsAllFields()
    {
        var ctx = new RpcContext(new ArrayBufferWriter<byte>());
        var ep = new IPEndPoint(IPAddress.Loopback, 1234);
        var headers = new Dictionary<string, string> { ["h1"] = "v1" };

        ctx.PopulateRequest(10, 20, "Svc", "Met", ep, headers);

        Assert.Equal(10L, ctx.ConnectionId);
        Assert.Equal(20u, ctx.RequestId);
        Assert.Equal("Svc", ctx.ServiceName);
        Assert.Equal("Met", ctx.MethodName);
        Assert.Same(ep, ctx.RemoteEndPoint);
        Assert.Equal("v1", ctx.Headers["h1"]);
    }

    [Fact]
    public void PopulateRequest_NullHeaders_DoesNotForceHeadersAllocation()
    {
        var ctx = new RpcContext(new ArrayBufferWriter<byte>());

        ctx.PopulateRequest(1, 1, "S", "M", null, null);

        Assert.Null(ctx.HeadersOrNull);
    }

    [Fact]
    public void PopulateRequest_EmptyHeaders_DoesNotForceHeadersAllocation()
    {
        var ctx = new RpcContext(new ArrayBufferWriter<byte>());

        ctx.PopulateRequest(1, 1, "S", "M", null, new Dictionary<string, string>());

        Assert.Null(ctx.HeadersOrNull);
    }

    [Fact]
    public void PopulateRequest_NonEmptyHeaders_PopulatesHeaders()
    {
        var ctx = new RpcContext(new ArrayBufferWriter<byte>());
        var headers = new Dictionary<string, string> { ["key"] = "val" };

        ctx.PopulateRequest(1, 1, "S", "M", null, headers);

        Assert.NotNull(ctx.HeadersOrNull);
        Assert.Equal("val", ctx.Headers["key"]);
    }

    [Fact]
    public void Headers_Lazy_NotAllocatedBeforeFirstAccess()
    {
        var ctx = new RpcContext(new ArrayBufferWriter<byte>());

        Assert.Null(ctx.HeadersOrNull);
    }

    [Fact]
    public void Headers_Lazy_SameReferenceOnSubsequentAccess()
    {
        var ctx = new RpcContext(new ArrayBufferWriter<byte>());

        var h1 = ctx.Headers;
        var h2 = ctx.Headers;

        Assert.Same(h1, h2);
    }

    [Fact]
    public void Items_Lazy_SameReferenceOnSubsequentAccess()
    {
        var ctx = new RpcContext(new ArrayBufferWriter<byte>());

        var i1 = ctx.Items;
        var i2 = ctx.Items;

        Assert.Same(i1, i2);
    }

    [Fact]
    public void HeadersOrNull_NullBeforeAccess_NonNullAfter()
    {
        var ctx = new RpcContext(new ArrayBufferWriter<byte>());

        Assert.Null(ctx.HeadersOrNull);
        _ = ctx.Headers;
        Assert.NotNull(ctx.HeadersOrNull);
    }

    [Fact]
    public void Reset_ClearsHeadersAndItemsContents()
    {
        var ctx = new RpcContext(new ArrayBufferWriter<byte>());
        ctx.Headers["h"] = "v";
        ctx.Items["key"] = "value";

        ctx.Reset();

        Assert.Empty(ctx.Headers);
        Assert.Empty(ctx.Items);
    }

    [Fact]
    public void Reset_PreservesHeadersAndItemsInstances()
    {
        var ctx = new RpcContext(new ArrayBufferWriter<byte>());
        var headers = ctx.Headers;
        var items = ctx.Items;
        ctx.Headers["h"] = "v";
        ctx.Items["key"] = "value";

        ctx.Reset();

        Assert.Same(headers, ctx.Headers);
        Assert.Same(items, ctx.Items);
    }
}
