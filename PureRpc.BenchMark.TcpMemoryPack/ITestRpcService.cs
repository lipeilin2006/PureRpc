using MemoryPack;
using PureRpc.Abstractions;

namespace PureRpc.BenchMark.TcpMemoryPack
{
    [MemoryPackable]
    public partial struct TestRequest(int a, int b)
    {
        public int A { get; set; } = a;
        public int B { get; set; } = b;
    }

    [RpcService("TestRpcService")]
    public interface ITestRpcService
    {
        [RpcMethod("Test")]
        Task<int> TestAsync(TestRequest request, CancellationToken ct = default);
    }
}
