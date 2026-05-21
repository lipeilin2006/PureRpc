using System;
using System.Buffers;
using System.Net;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;

namespace PureRpc.Abstractions;

/// <summary>
/// RPC 请求上下文，封装单次 RPC 调用的所有元数据和状态 / 
/// RPC request context, encapsulating all metadata and state for a single RPC call.
/// 对象池化复用，通过 <see cref="RpcContextPolicy"/> 管理生命周期 / 
/// Pooled and reused, lifecycle managed through <see cref="RpcContextPolicy"/>.
/// </summary>
public sealed class RpcContext
{
    /// <summary>
    /// 连接唯一标识符 / Unique connection identifier.
    /// </summary>
    public long ConnectionId { get; set; }

    /// <summary>
    /// 请求唯一标识符 / Unique request identifier within the connection.
    /// </summary>
    public uint RequestId { get; set; }

    /// <summary>
    /// 目标服务名称 / Target service name.
    /// </summary>
    public string ServiceName { get; set; } = string.Empty;

    /// <summary>
    /// 目标方法名称 / Target method name.
    /// </summary>
    public string MethodName { get; set; } = string.Empty;

    /// <summary>
    /// 远程端点地址 / Remote endpoint address.
    /// </summary>
    public EndPoint? RemoteEndPoint { get; set; }

    /// <summary>
    /// 响应缓冲区写入器，服务端实现通过此写入器写入序列化后的响应数据 / 
    /// Response buffer writer; server implementations write serialized response data through this writer.
    /// </summary>
    public IBufferWriter<byte> ResponseBuffer { get; }

    /// <summary>
    /// 获取或设置请求是否已被中止 / Gets or sets whether the request has been aborted.
    /// 中止后服务端将发送 Error 类型响应 / When aborted, the server sends an Error-type response.
    /// </summary>
    public bool IsAborted { get; private set; }

    /// <summary>
    /// 请求的取消令牌 / Cancellation token for the request.
    /// </summary>
    public CancellationToken CancellationToken { get; set; }

    /// <summary>
    /// 当前已认证用户主体 / The current authenticated user principal.
    /// 由 IAuthorizationHandler 在授权成功后设置 / 
    /// Set by IAuthorizationHandler after successful authorization.
    /// </summary>
    public ClaimsPrincipal? User { get; set; }

    /// <summary>
    /// 请求头部字典的后备字段 / Backing field for the headers dictionary.
    /// 延迟初始化以避免不必要的内存分配 / Lazy-initialized to avoid unnecessary allocations.
    /// </summary>
    private Dictionary<string, string>? _headers;

    /// <summary>
    /// 请求头部字典，延迟初始化 / Request headers dictionary, lazily initialized.
    /// 首次访问时分配 / Allocated on first access.
    /// </summary>
    public Dictionary<string, string> Headers => _headers ??= new Dictionary<string, string>();

    /// <summary>
    /// 获取头部字典的原始引用（未初始化时为 null），内部用于协议帧写入 / 
    /// Gets the raw reference to the headers dictionary (null when not initialized), used internally for frame writing.
    /// </summary>
    internal Dictionary<string, string>? HeadersOrNull => _headers;

    /// <summary>
    /// 自定义项字典的后备字段 / Backing field for the items dictionary.
    /// 延迟初始化以避免不必要的内存分配 / Lazy-initialized to avoid unnecessary allocations.
    /// </summary>
    private Dictionary<object, object?>? _items;

    /// <summary>
    /// 自定义项字典，延迟初始化 / Custom items dictionary, lazily initialized.
    /// 用于在拦截器管道中传递额外数据 / Used to pass additional data through the interceptor pipeline.
    /// </summary>
    public Dictionary<object, object?> Items => _items ??= new Dictionary<object, object?>();

    /// <summary>
    /// 初始化 RpcContext 实例 / Initializes a new RpcContext instance.
    /// </summary>
    /// <param name="responseBuffer">响应缓冲区写入器 / The response buffer writer.</param>
    /// <exception cref="ArgumentNullException">当 <paramref name="responseBuffer"/> 为 null 时抛出 / 
    /// Thrown when <paramref name="responseBuffer"/> is null.</exception>
    public RpcContext(IBufferWriter<byte> responseBuffer)
    {
        ResponseBuffer = responseBuffer ?? throw new ArgumentNullException(nameof(responseBuffer));
    }

    /// <summary>
    /// 标记当前请求为已中止 / Marks the current request as aborted.
    /// 中止后服务端将发送 Error 类型响应 / When aborted, the server sends an Error-type response.
    /// </summary>
    public void Abort()
    {
        IsAborted = true;
    }

    /// <summary>
    /// 从协议帧解析结果填充请求元数据 / Populates request metadata from the parsed protocol frame.
    /// 仅在 headers 非空且有内容时触发 Headers 延迟分配 / 
    /// Only triggers Headers lazy allocation when headers is non-null and non-empty.
    /// </summary>
    /// <param name="connId">连接标识符 / Connection identifier.</param>
    /// <param name="reqId">请求标识符 / Request identifier.</param>
    /// <param name="svc">服务名称 / Service name.</param>
    /// <param name="met">方法名称 / Method name.</param>
    /// <param name="remoteEP">远程端点 / Remote endpoint.</param>
    /// <param name="headers">请求头部字典（可为 null） / Request headers dictionary (may be null).</param>
    public void PopulateRequest(long connId, uint reqId, string svc, string met, EndPoint? remoteEP, IReadOnlyDictionary<string, string>? headers)
    {
        ConnectionId = connId;
        RequestId = reqId;
        ServiceName = svc;
        MethodName = met;
        RemoteEndPoint = remoteEP;
        if (headers is { Count: > 0 })
        {
            var h = Headers;
            foreach (var kv in headers)
                h[kv.Key] = kv.Value;
        }
    }

    /// <summary>
    /// 重置上下文状态以便对象池复用 / Resets the context state for object pool reuse.
    /// 清空所有字段、Headers、Items 和 ResponseBuffer / 
    /// Clears all fields, Headers, Items, and ResponseBuffer.
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

        if (_headers is { Count: > 0 })
            _headers.Clear();

        if (_items is { Count: > 0 })
            _items.Clear();

        if (ResponseBuffer is ArrayBufferWriter<byte> writer)
        {
            writer.Clear();
        }
    }
}
