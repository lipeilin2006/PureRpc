using PureRpc.Abstractions;
using PureRpc.BenchMark.TcpMemoryPack;

internal class TestRpcService : ServiceBase, ITestRpcService
{
    public async Task<int> TestAsync(TestRequest request, CancellationToken ct)
    {
        return await Task.FromResult(request.A + request.B);
    }
}
