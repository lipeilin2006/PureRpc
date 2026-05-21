using Microsoft.Extensions.ObjectPool;
using System;
using System.Buffers;
using System.Threading;
using System.Threading.Tasks;

namespace PureRpc.Abstractions
{
    /// <summary>
    /// 定义 PureRpc 服务器的核心契约 / Defines the core contract for the PureRpc server.
    /// 服务器作为核心协调者，负责资源池化管理、启动传输层监听并调度业务分发 / 
    /// The server acts as the central coordinator, managing resource pools, starting transport listeners, and dispatching business logic.
    /// </summary>
    public interface IRpcServer : IAsyncDisposable
    {
        /// <summary>
        /// 启动服务器 / Starts the server.
        /// 内部将启动关联的传输层（IServerTransport）处理循环 / 
        /// Internally starts the associated transport (IServerTransport) processing loop.
        /// </summary>
        /// <param name="ct">控制服务器生命周期的取消令牌 / Cancellation token controlling the server lifecycle.</param>
        /// <returns>表示启动过程的 Task / A Task representing the startup process.</returns>
        Task StartAsync(CancellationToken ct = default);

        /// <summary>
        /// 处理由传输层解析出的 RPC 请求上下文 / Handles an RPC request context parsed by the transport layer.
        /// 此方法由 IServerTransport 的实现在接收到完整协议帧后回调 / 
        /// This method is called back by IServerTransport implementations after receiving a complete protocol frame.
        /// 处理完成后，实现类负责将 Context 归还至 ContextPool / 
        /// After processing, the implementation is responsible for returning the Context to the ContextPool.
        /// </summary>
        /// <param name="context">当前 RPC 请求上下文 / The current RPC request context.</param>
        /// <param name="payload">请求的原始字节序列 / The raw byte sequence of the request payload.</param>
        /// <returns>表示处理过程的 Task / A Task representing the handling process.</returns>
        Task HandleRequestAsync(RpcContext context, ReadOnlySequence<byte> payload);
    }
}
