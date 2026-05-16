using PureRpc.Abstractions;

namespace PureRpc.Test;

internal class CalcService : ServiceBase, ICalcService
{
    public Task<int> AddAsync(AddRequest request, CancellationToken ct) =>
        Task.FromResult(request.A + request.B);

    public Task<string> EchoAsync(EchoRequest request, CancellationToken ct) =>
        Task.FromResult(request.Message);
}

internal class AdvancedService : ServiceBase, IAdvancedService
{
    public Task<bool> ThrowAsync(CancellationToken ct) =>
        throw new InvalidOperationException("Intentional error for testing.");

    public Task<string> EchoHeaderAsync(EchoHeaderRequest request, CancellationToken ct)
    {
        Context.Headers.TryGetValue(request.HeaderName, out var value);
        return Task.FromResult(value ?? "");
    }

    public async Task<int> DelayAsync(DelayRequest request, CancellationToken ct)
    {
        await Task.Delay(request.Ms, ct);
        return request.Ms;
    }
}
