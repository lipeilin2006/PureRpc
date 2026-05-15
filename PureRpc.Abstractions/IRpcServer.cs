using Microsoft.Extensions.ObjectPool;
using System;
using System.Buffers;
using System.Threading;
using System.Threading.Tasks;

namespace PureRpc.Abstractions
{
    /// <summary>
    /// 定义 PureRpc 服务器的核心契约。
    /// 服务器作为核心协调者，负责资源池化管理、启动传输层监听并调度业务分发。
    /// </summary>
    public interface IRpcServer : IAsyncDisposable
    {
        /// <summary>
        /// 启动服务器。
        /// 内部将启动关联的传输层（IServerTransport）处理循环。
        /// </summary>
        Task StartAsync(CancellationToken ct = default);

        /// <summary>
        /// 处理由传输层解析出的 RPC 请求上下文。
        /// 此方法由 IServerTransport 的实现在接收到完整协议帧后回调。
        /// 处理完成后，实现类负责将 Context 归还至 ContextPool。
        /// </summary>
        Task HandleRequestAsync(RpcContext context, ReadOnlySequence<byte> payload);
    }
}