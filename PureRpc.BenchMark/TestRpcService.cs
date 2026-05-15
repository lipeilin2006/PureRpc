using PureRpc.Abstractions;
using PureRpc.BenchMark;

internal class TestRpcService : ServiceBase, ITestRpcService
{
    public async Task<int> TestAsync(TestRequest request, CancellationToken ct)
    {
        return await Task.FromResult(request.A + request.B);
    }
}
