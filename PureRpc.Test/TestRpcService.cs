using PureRpc.Abstractions;

namespace PureRpc.Test;

internal class CalcService : ServiceBase, ICalcService
{
    public Task<int> AddAsync(AddRequest request, CancellationToken ct) =>
        Task.FromResult(request.A + request.B);

    public Task<string> EchoAsync(EchoRequest request, CancellationToken ct) =>
        Task.FromResult(request.Message);
}
