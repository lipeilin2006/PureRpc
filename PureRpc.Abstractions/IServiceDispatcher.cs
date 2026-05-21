using System.Buffers;
using System.Threading.Tasks;

namespace PureRpc.Abstractions;

/// <summary>
/// 定义服务分发器接口 / Defines the service dispatcher interface.
/// 由 Source Generator 生成的实现类将负责根据方法名将请求路由到具体的服务实例 / 
/// Source Generator-generated implementations route requests to concrete service instances by method name.
/// </summary>
public interface IServiceDispatcher
{
    /// <summary>
    /// 获取服务名称，用于全局路由分发 / Gets the service name used for global routing dispatch
    /// （例如 "IChatService" 或 "PureRpc.Tests.IMyService"） / (e.g. "IChatService" or "PureRpc.Tests.IMyService").
    /// </summary>
    string ServiceName { get; }

    /// <summary>
    /// 执行具体的 RPC 方法分发逻辑 / Dispatches the RPC method invocation to the target implementation.
    /// </summary>
    /// <param name="methodName">目标方法的名称（对应 RpcMethodAttribute.MethodName 或 C# 方法名） / 
    /// The target method name (corresponds to RpcMethodAttribute.MethodName or C# method name).</param>
    /// <param name="payload">请求的原始字节序列，尚未反序列化 / 
    /// The raw byte sequence of the request payload, not yet deserialized.</param>
    /// <param name="context">当前请求的执行上下文，包含 ResponseBuffer 和取消令牌 / 
    /// The execution context for the current request, containing ResponseBuffer and cancellation token.</param>
    /// <returns>表示分发与执行过程的 ValueTask / A ValueTask representing the dispatch and execution process.</returns>
    ValueTask DispatchAsync(string methodName, ReadOnlySequence<byte> payload, RpcContext context);
}
