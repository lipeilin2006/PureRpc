using System;
using System.Buffers;
using System.Threading;
using System.Threading.Tasks;

namespace PureRpc.Abstractions;

/// <summary>
/// 定义客户端传输层行为。
/// 具体的连接目标（Host/Port）由实现类通过 IOptions 模式在内部持有。
/// </summary>
public interface IClientTransport : IAsyncDisposable
{
    /// <summary>
    /// 获取当前传输层是否已建立连接且可用。
    /// </summary>
    bool IsConnected { get; }

    /// <summary>
    /// 建立与远程服务器的连接并启动响应监听循环。
    /// </summary>
    /// <param name="onResponseReceived">
    /// 当传输层解析出一个完整的响应包时回调。
    /// <list type="bullet">
    /// <item><description>uint: RequestId (请求标识)</description></item>
    /// <item><description>ReadOnlySequence&lt;byte&gt;: Payload (响应负载)</description></item>
    /// <item><description>bool: Success (是否成功)</description></item>
    /// <item><description>string?: ErrorMessage (若失败，包含错误描述)</description></item>
    /// <item><description>IReadOnlyDictionary&lt;string, string&gt;?: 响应头部</description></item>
    /// </list>
    /// </param>
    /// <param name="ct">连接过程的取消令牌。</param>
    Task ConnectAsync(
        Action<uint, ReadOnlySequence<byte>, bool, string?, IReadOnlyDictionary<string, string>?> onResponseReceived,
        CancellationToken ct);

    /// <summary>
    /// 发送 RPC 请求帧。
    /// 传输层负责根据具体协议（如 PureTcp 或 gRPC）封装协议头。
    /// </summary>
    /// <param name="requestId">唯一请求标识。</param>
    /// <param name="serviceName">目标服务名称（通常为接口全名）。</param>
    /// <param name="methodName">目标方法名称。</param>
    /// <param name="data">已序列化的参数数据。</param>
    /// <param name="ct">发送过程的取消令牌。</param>
    /// <param name="headers">可选的请求头部元数据（如认证令牌），传输层将序列化到协议帧中。</param>
    ValueTask SendAsync(
        uint requestId,
        string serviceName,
        string methodName,
        ReadOnlySequence<byte> data,
        CancellationToken ct,
        IDictionary<string, string>? headers = null);

    /// <summary>
    /// 通知服务端取消特定的请求。
    /// 传输层应向服务端发送特定类型（如 Cancel）的控制帧。
    /// </summary>
    /// <param name="requestId">要取消的请求标识。</param>
    /// <param name="ct">取消操作的令牌。</param>
    ValueTask CancelRequestAsync(uint requestId, CancellationToken ct = default);
}