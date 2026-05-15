using Microsoft.Extensions.ObjectPool;
using System.Buffers;

namespace PureRpc.Abstractions
{
    public sealed class RpcContextPolicy : IPooledObjectPolicy<RpcContext>
    {
        /// <summary>
        /// 当池中没有可用对象时，创建一个新的 Context 实例。
        /// </summary>
        public RpcContext Create()
        {
            // 初始分配 4KB 缓冲区（4096）通常比 1KB 更具通用性，
            // 能够覆盖大多数常见 RPC 响应大小，减少后续扩容触发频率。
            // 内存分配点：池中没有可用 Context 时，会 new RpcContext 和 ArrayBufferWriter<byte>。
            // 改进建议：可以根据业务响应大小调小/调大初始容量；若小响应占绝大多数，过大的初始容量会浪费驻留内存。
            return new RpcContext(new ArrayBufferWriter<byte>(4096));
        }

        /// <summary>
        /// 当对象返回池中时调用。
        /// </summary>
        public bool Return(RpcContext obj)
        {
            // M-05: 如果缓冲区膨胀过大，丢弃该对象，避免长期持有大块内存
            const int MaxPooledBufferSize = 1024 * 1024; // 1MB
            if (obj.ResponseBuffer is ArrayBufferWriter<byte> writer && writer.Capacity > MaxPooledBufferSize)
                return false;

            obj.Reset();
            return true;
        }
    }
}
