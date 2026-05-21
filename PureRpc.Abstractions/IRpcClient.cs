using System;
using System.Buffers;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace PureRpc.Abstractions;

/// <summary>
/// 定义核心 RPC 客户端契约，用于发起对远程服务的标准化调用 / 
/// Defines the core RPC client contract for making standardized calls to remote services.
/// </summary>
public interface IRpcClient : IAsyncDisposable
{
    /// <summary>
    /// 获取一个值，指示当前客户端的连接状态和可用性 / 
    /// Gets a value indicating the current connection state and availability of the client.
    /// </summary>
    bool IsAvailable { get; }

    /// <summary>
    /// 显式启动客户端并建立底层连接 / Explicitly starts the client and establishes the underlying connection.
    /// 如果未手动调用，实现类通常应在首次发起请求时尝试自动连接 / 
    /// If not called manually, implementations typically attempt auto-connection on the first request.
    /// </summary>
    /// <param name="ct">连接过程的取消令牌 / Cancellation token for the connection process.</param>
    /// <returns>表示启动过程的 Task / A Task representing the startup process.</returns>
    Task StartAsync(CancellationToken ct = default);

    /// <summary>
    /// 执行异步 RPC 调用 / Performs an asynchronous RPC call.
    /// </summary>
    /// <param name="serviceName">目标服务的唯一名称（例如接口全名 "IChatService"） / 
    /// The unique name of the target service (e.g. interface full name "IChatService").</param>
    /// <param name="methodName">目标方法的名称（对应 RpcMethodAttribute.MethodName 或代码方法名） / 
    /// The target method name (corresponds to RpcMethodAttribute.MethodName or code method name).</param>
    /// <param name="requestPayload">已序列化的请求参数负载 / The serialized request parameter payload.</param>
    /// <param name="ct">RPC 调用的取消令牌。触发后，客户端将尝试同时在本地和远程取消请求 / 
    /// Cancellation token for the RPC call. When triggered, the client attempts to cancel locally and remotely.</param>
    /// <param name="headers">可选的请求头部元数据 / Optional request header metadata.</param>
    /// <returns>
    /// 包含原始响应数据的 <see cref="ValueTask{T}"/> / A <see cref="ValueTask{T}"/> containing the raw response data.
    /// 注意：实现类必须确保 <see cref="ReadOnlySequence{Byte}"/> 中的内存在 Task 完成前保持有效 / 
    /// Note: Implementations must ensure memory in the <see cref="ReadOnlySequence{Byte}"/> remains valid until the Task completes.
    /// </returns>
    /// <exception cref="OperationCanceledException">当 <paramref name="ct"/> 被触发时抛出 / Thrown when <paramref name="ct"/> is triggered.</exception>
    /// <exception cref="IOException">当连接丢失或 <see cref="IsAvailable"/> 为 false 时抛出 / 
    /// Thrown when the connection is lost or <see cref="IsAvailable"/> is false.</exception>
    /// <exception cref="RpcException">当服务端返回业务错误或违反协议规范时抛出 / 
    /// Thrown when the server returns a business error or protocol violation.</exception>
    ValueTask<ReadOnlySequence<byte>> CallAsync(
        string serviceName,
        string methodName,
        ReadOnlySequence<byte> requestPayload,
        CancellationToken ct,
        IDictionary<string, string>? headers = null);

    /// <summary>
    /// 设置默认请求头部，每次 CallAsync 调用将自动携带 / 
    /// Sets a default request header that will be automatically included in every CallAsync invocation.
    /// </summary>
    /// <param name="key">头部键名 / The header key.</param>
    /// <param name="value">头部值 / The header value.</param>
    void SetDefaultHeader(string key, string value);

    /// <summary>
    /// 移除指定的默认请求头部 / Removes the specified default request header.
    /// </summary>
    /// <param name="key">要移除的头部键名 / The header key to remove.</param>
    /// <returns>如果头部存在并被移除返回 true，否则返回 false / 
    /// true if the header existed and was removed; otherwise, false.</returns>
    bool RemoveDefaultHeader(string key);

    /// <summary>
    /// 清除所有默认请求头部 / Clears all default request headers.
    /// </summary>
    void ClearDefaultHeaders();
}
