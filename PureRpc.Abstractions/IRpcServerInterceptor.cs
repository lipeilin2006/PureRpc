using System.Buffers;
using System.Threading.Tasks;

namespace PureRpc.Abstractions;

public delegate ValueTask RpcRequestDelegate(RpcContext context, ReadOnlySequence<byte> payload);

public interface IRpcServerInterceptor
{
    ValueTask InvokeAsync(RpcContext context, ReadOnlySequence<byte> payload, RpcRequestDelegate next);
}
