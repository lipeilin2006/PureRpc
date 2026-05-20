#pragma warning disable CS1591
using Grpc.Core;

namespace PureRpc.BenchMark.Grpc;

public class BenchmarkServiceImpl : Benchmark.BenchmarkBase
{
    public override Task<BenchmarkResponse> UnaryCall(BenchmarkRequest request, ServerCallContext context)
    {
        return Task.FromResult(new BenchmarkResponse { Result = request.A + request.B });
    }
}
