using System.Buffers;
using System.Threading;
using System.Threading.Tasks;

namespace PureRpc.Abstractions;

public delegate ValueTask<ReadOnlySequence<byte>> RpcCallDelegate(
    string serviceName, string methodName, ReadOnlySequence<byte> requestPayload,
    CancellationToken ct, IDictionary<string, string>? headers);

public interface IRpcClientInterceptor
{
    ValueTask<ReadOnlySequence<byte>> InvokeAsync(
        string serviceName, string methodName, ReadOnlySequence<byte> requestPayload,
        CancellationToken ct, IDictionary<string, string>? headers, RpcCallDelegate next);
}
