using MemoryPack;
using PureRpc.Abstractions;

namespace PureRpc.Test;

[MemoryPackable]
public partial record AddRequest(int A, int B);

[MemoryPackable]
public partial record EchoRequest(string Message);

[RpcService("CalcService")]
public interface ICalcService
{
    [RpcMethod("Add")]
    Task<int> AddAsync(AddRequest request, CancellationToken ct = default);

    [RpcMethod("Echo")]
    Task<string> EchoAsync(EchoRequest request, CancellationToken ct = default);
}
