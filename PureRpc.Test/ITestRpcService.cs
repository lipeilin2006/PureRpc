using MemoryPack;
using PureRpc.Abstractions;

namespace PureRpc.Test;

[MemoryPackable]
public partial record AddRequest(int A, int B);

[MemoryPackable]
public partial record EchoRequest(string Message);

[MemoryPackable]
public partial record EchoHeaderRequest(string HeaderName);

[MemoryPackable]
public partial record DelayRequest(int Ms);

[RpcService("CalcService")]
public interface ICalcService
{
    [RpcMethod("Add")]
    Task<int> AddAsync(AddRequest request, CancellationToken ct = default);

    [RpcMethod("Echo")]
    Task<string> EchoAsync(EchoRequest request, CancellationToken ct = default);
}

[RpcService("AdvancedService")]
public interface IAdvancedService
{
    [RpcMethod("Throw")]
    Task<bool> ThrowAsync(CancellationToken ct = default);

    [RpcMethod("EchoHeader")]
    Task<string> EchoHeaderAsync(EchoHeaderRequest request, CancellationToken ct = default);

    [RpcMethod("Delay")]
    Task<int> DelayAsync(DelayRequest request, CancellationToken ct = default);
}
