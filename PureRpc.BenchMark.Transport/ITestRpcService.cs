using MemoryPack;
using PureRpc.Abstractions;

namespace PureRpc.BenchMark.Transport;

[MemoryPackable]
public partial struct TestRequest
{
    public int A { get; set; }
    public int B { get; set; }
}

[RpcService("TestRpcService")]
public interface ITestRpcService
{
    [RpcMethod("Test")]
    Task<int> TestAsync(TestRequest request, CancellationToken ct = default);
}
