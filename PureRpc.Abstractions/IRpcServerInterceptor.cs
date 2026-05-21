using System.Buffers;
using System.Threading.Tasks;

namespace PureRpc.Abstractions;

/// <summary>
/// RPC 请求处理管道的委托类型 / Delegate type for the RPC request processing pipeline.
/// 表示管道中的下一步操作 / Represents the next step in the pipeline.
/// </summary>
/// <param name="context">当前 RPC 请求上下文 / The current RPC request context.</param>
/// <param name="payload">请求的原始字节序列 / The raw byte sequence of the request payload.</param>
/// <returns>表示异步处理的 ValueTask / A ValueTask representing the asynchronous processing.</returns>
public delegate ValueTask RpcRequestDelegate(RpcContext context, ReadOnlySequence<byte> payload);

/// <summary>
/// 定义 RPC 服务端拦截器接口 / Defines the RPC server interceptor interface.
/// 拦截器可在请求到达 Dispatcher 前后执行自定义逻辑（如认证、日志、限流） / 
/// Interceptors can execute custom logic before/after a request reaches the Dispatcher (e.g. auth, logging, rate limiting).
/// </summary>
public interface IRpcServerInterceptor
{
    /// <summary>
    /// 执行拦截逻辑并调用管道中的下一个委托 / 
    /// Executes interceptor logic and invokes the next delegate in the pipeline.
    /// </summary>
    /// <param name="context">当前 RPC 请求上下文 / The current RPC request context.</param>
    /// <param name="payload">请求的原始字节序列 / The raw byte sequence of the request payload.</param>
    /// <param name="next">管道中的下一个处理委托 / The next processing delegate in the pipeline.</param>
    /// <returns>表示异步处理的 ValueTask / A ValueTask representing the asynchronous processing.</returns>
    ValueTask InvokeAsync(RpcContext context, ReadOnlySequence<byte> payload, RpcRequestDelegate next);
}
