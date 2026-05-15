using System;
using System.Buffers;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace PureRpc.Abstractions;

/// <summary>
/// 定义核心 RPC 客户端契约，用于发起对远程服务的标准化调用。
/// </summary>
public interface IRpcClient : IAsyncDisposable
{
    /// <summary>
    /// 获取一个值，指示当前客户端的连接状态和可用性。
    /// </summary>
    bool IsAvailable { get; }

    /// <summary>
    /// 显式启动客户端并建立底层连接。
    /// 如果未手动调用，实现类通常应在首次发起请求时尝试自动连接。
    /// </summary>
    /// <param name="ct">连接过程的取消令牌。</param>
    Task StartAsync(CancellationToken ct = default);

    /// <summary>
    /// 执行异步 RPC 调用。
    /// </summary>
    /// <param name="serviceName">目标服务的唯一名称（例如接口全名 "IChatService"）。</param>
    /// <param name="methodName">目标方法的名称（对应 RpcMethodAttribute.MethodName 或代码方法名）。</param>
    /// <param name="requestPayload">已序列化的请求参数负载。</param>
    /// <param name="ct">RPC 调用的取消令牌。触发后，客户端将尝试同时在本地和远程取消请求。</param>
    /// <returns>
    /// 包含原始响应数据的 <see cref="ValueTask{T}"/>。
    /// 注意：实现类必须确保 <see cref="ReadOnlySequence{Byte}"/> 中的内存在 Task 完成前保持有效。
    /// </returns>
    /// <exception cref="OperationCanceledException">当 <paramref name="ct"/> 被触发时抛出。</exception>
    /// <exception cref="IOException">当连接丢失或 <see cref="IsAvailable"/> 为 false 时抛出。</exception>
    /// <exception cref="RpcException">当服务端返回业务错误或违反协议规范时抛出。</exception>
    ValueTask<ReadOnlySequence<byte>> CallAsync(
        string serviceName,
        string methodName,
        ReadOnlySequence<byte> requestPayload,
        CancellationToken ct,
        IDictionary<string, string>? headers = null);

    /// <summary>
    /// 设置默认请求头部，每次 CallAsync 调用将自动携带。
    /// </summary>
    void SetDefaultHeader(string key, string value);

    /// <summary>
    /// 移除指定的默认请求头部。
    /// </summary>
    bool RemoveDefaultHeader(string key);

    /// <summary>
    /// 清除所有默认请求头部。
    /// </summary>
    void ClearDefaultHeaders();
}