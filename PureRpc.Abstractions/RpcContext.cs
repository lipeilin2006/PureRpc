using System;
using System.Buffers;
using System.Net;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;

namespace PureRpc.Abstractions;

public sealed class RpcContext
{
    public long ConnectionId { get; set; }
    public uint RequestId { get; set; }
    public string ServiceName { get; set; } = string.Empty;
    public string MethodName { get; set; } = string.Empty;
    public EndPoint? RemoteEndPoint { get; set; }

    // 响应缓冲区（通常是池化的 ArrayBufferWriter）。
    // 内存分配点：ArrayBufferWriter 会按写入量自动扩容，扩容时会分配更大的 byte[]。
    // 改进建议：如果某次响应特别大，Clear() 不会释放内部数组，池化后可能长期占用大内存；
    // 可在对象池 Return 策略里增加容量阈值，超过阈值时丢弃该 Context 或重建缓冲区。
    public IBufferWriter<byte> ResponseBuffer { get; }

    public bool IsAborted { get; private set; }

    // 在高性能场景下，建议由 Transport 传入 CancellationToken 而非 Context 自带 CTS
    public CancellationToken CancellationToken { get; set; }

    /// <summary>
    /// 当前认证用户主体，由 IAuthorizationHandler 在授权成功后设置。
    /// 服务方法可通过 ServiceBase.User 读取。
    /// </summary>
    public ClaimsPrincipal? User { get; set; }

    /// <summary>
    /// 请求/响应元数据头部。
    /// 服务端：Transport 反序列化请求帧后自动填充请求头部；
    /// 同时服务端拦截器可写入响应头部（在 Dispatcher 返回前设置）。
    /// </summary>
    public Dictionary<string, string> Headers { get; } = new();

    /// <summary>
    /// 扩展数据字典，用于拦截器与处理器之间的自定义状态传递。
    /// 生命周期仅限当前请求，请求结束后自动清空。
    /// </summary>
    public Dictionary<object, object?> Items { get; } = new();

    public RpcContext(IBufferWriter<byte> responseBuffer)
    {
        ResponseBuffer = responseBuffer ?? throw new ArgumentNullException(nameof(responseBuffer));
    }

    public void Abort()
    {
        IsAborted = true;
    }

    /// <summary>
    /// 重置 Context 状态，以便返回池中复用。
    /// 由池化策略 (IPooledObjectPolicy) 调用。
    /// </summary>
    public void Reset()
    {
        ConnectionId = 0;
        RequestId = 0;
        ServiceName = string.Empty;
        MethodName = string.Empty;
        RemoteEndPoint = null;
        User = null;
        IsAborted = false;
        CancellationToken = CancellationToken.None;
        Headers.Clear();
        Items.Clear();

        // Clear 只重置已写入计数，不释放 ArrayBufferWriter 内部数组。
        // 这能减少下次响应的分配，但也可能让大响应后的缓冲区被长期保留。
        if (ResponseBuffer is ArrayBufferWriter<byte> writer)
        {
            writer.Clear();
        }
    }
}
