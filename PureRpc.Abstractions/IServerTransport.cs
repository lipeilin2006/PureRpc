using System;
using System.Buffers;
using System.Threading;
using System.Threading.Tasks;

namespace PureRpc.Abstractions;

/// <summary>
/// 定义服务端传输层契约。
/// 负责底层的连接管理、协议帧解析（解包）与封装（封包）。
/// </summary>
public interface IServerTransport : IAsyncDisposable
{
    /// <summary>
    /// 启动传输监听并开始接受连接。
    /// </summary>
    /// <param name="onRequestReceived">
    /// 当 Transport 解析出完整的 Request 帧后，回调此方法。
    /// <para>PureRpcContext: 包含 RequestId, ServiceName, MethodName 等元数据。</para>
    /// <para>ReadOnlySequence: 业务负载 (Payload) 的原始二进制序列。</para>
    /// </param>
    /// <param name="ct">控制整个传输层生命周期的令牌。</param>
    Task StartAsync(
        Func<RpcContext, ReadOnlySequence<byte>, Task> onRequestReceived,
        CancellationToken ct);

    /// <summary>
    /// (可选) 显式触发响应发送。
    /// 在大多数实现中（如 TcpServerTransport），响应在 onRequestReceived 回调结束后自动发送。
    /// 此方法可用于需要立即冲刷缓冲区或处理非标准响应的场景。
    /// </summary>
    ValueTask SendResponseAsync(RpcContext context, CancellationToken ct);
}