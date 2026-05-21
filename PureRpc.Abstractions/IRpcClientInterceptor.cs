using System.Buffers;
using System.Threading;
using System.Threading.Tasks;

namespace PureRpc.Abstractions;

/// <summary>
/// RPC 客户端调用管道的委托类型 / Delegate type for the RPC client call pipeline.
/// 表示管道中的下一步调用操作 / Represents the next call operation in the pipeline.
/// </summary>
/// <param name="serviceName">目标服务名称 / The target service name.</param>
/// <param name="methodName">目标方法名称 / The target method name.</param>
/// <param name="requestPayload">已序列化的请求参数负载 / The serialized request parameter payload.</param>
/// <param name="ct">取消令牌 / Cancellation token.</param>
/// <param name="headers">可选的请求头部 / Optional request headers.</param>
/// <returns>包含原始响应数据的 ValueTask / A ValueTask containing the raw response data.</returns>
public delegate ValueTask<ReadOnlySequence<byte>> RpcCallDelegate(
    string serviceName, string methodName, ReadOnlySequence<byte> requestPayload,
    CancellationToken ct, IDictionary<string, string>? headers);

/// <summary>
/// 定义 RPC 客户端拦截器接口 / Defines the RPC client interceptor interface.
/// 拦截器可在调用发出前后执行自定义逻辑（如重试、熔断、请求日志） / 
/// Interceptors can execute custom logic before/after a call is made (e.g. retry, circuit breaking, request logging).
/// </summary>
public interface IRpcClientInterceptor
{
    /// <summary>
    /// 执行拦截逻辑并调用管道中的下一个委托 / 
    /// Executes interceptor logic and invokes the next delegate in the pipeline.
    /// </summary>
    /// <param name="serviceName">目标服务名称 / The target service name.</param>
    /// <param name="methodName">目标方法名称 / The target method name.</param>
    /// <param name="requestPayload">已序列化的请求参数负载 / The serialized request parameter payload.</param>
    /// <param name="ct">取消令牌 / Cancellation token.</param>
    /// <param name="headers">可选的请求头部 / Optional request headers.</param>
    /// <param name="next">管道中的下一个调用委托 / The next call delegate in the pipeline.</param>
    /// <returns>包含原始响应数据的 ValueTask / A ValueTask containing the raw response data.</returns>
    ValueTask<ReadOnlySequence<byte>> InvokeAsync(
        string serviceName, string methodName, ReadOnlySequence<byte> requestPayload,
        CancellationToken ct, IDictionary<string, string>? headers, RpcCallDelegate next);
}
