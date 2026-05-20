using System.Buffers;
using PureRpc.Abstractions;

namespace PureRpc.Test;

public sealed class ServiceBaseTests
{
    [Fact]
    public void Context_SetterAndGetter_Work()
    {
        var ctx = new RpcContext(new ArrayBufferWriter<byte>());
        ctx.ServiceName = "TestService";

        var service = new TestServiceImpl();
        service.Context = ctx;

        Assert.Equal("TestService", service.Context.ServiceName);
    }

    [Fact]
    public void Context_NotSet_Throws()
    {
        var service = new TestServiceImpl();
        Assert.Throws<InvalidOperationException>(() => _ = service.Context);
    }

    [Fact]
    public async Task Context_IsAsyncLocal_IsolatedPerExecutionFlow()
    {
        var ctx1 = new RpcContext(new ArrayBufferWriter<byte>()) { ServiceName = "Svc1" };
        var ctx2 = new RpcContext(new ArrayBufferWriter<byte>()) { ServiceName = "Svc2" };

        var service1 = new TestServiceImpl();
        var service2 = new TestServiceImpl();

        string? result1 = null;
        string? result2 = null;

        var task1 = Task.Run(() =>
        {
            service1.Context = ctx1;
            result1 = service1.Context.ServiceName;
        });

        var task2 = Task.Run(() =>
        {
            service2.Context = ctx2;
            result2 = service2.Context.ServiceName;
        });

        await Task.WhenAll(task1, task2);

        Assert.Equal("Svc1", result1);
        Assert.Equal("Svc2", result2);
    }

    [Fact]
    public void Context_ResetToNull_DoesNotThrowOnSubsequentSets()
    {
        var ctx = new RpcContext(new ArrayBufferWriter<byte>());
        var service = new TestServiceImpl();

        service.Context = ctx;
        Assert.NotNull(service.Context);

        service.Context = null!;
        Assert.Throws<InvalidOperationException>(() => _ = service.Context);
    }

    private sealed class TestServiceImpl : ServiceBase { }
}