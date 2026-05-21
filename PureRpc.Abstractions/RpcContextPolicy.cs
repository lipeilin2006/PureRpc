using Microsoft.Extensions.ObjectPool;
using System.Buffers;

namespace PureRpc.Abstractions
{
    /// <summary>
    /// RpcContext 对象池策略 / Object pool policy for RpcContext.
    /// 控制 RpcContext 实例的创建与回收，防止缓冲区膨胀导致内存泄漏 / 
    /// Controls creation and recycling of RpcContext instances, preventing buffer bloat from causing memory leaks.
    /// </summary>
    public sealed class RpcContextPolicy : IPooledObjectPolicy<RpcContext>
    {
        /// <summary>
        /// 创建 RpcContext 对象池 / Creates an object pool for RpcContext.
        /// </summary>
        /// <param name="maxRetained">池中最大保留对象数量 / Maximum number of objects to retain in the pool.</param>
        /// <returns>配置好的对象池实例 / A configured object pool instance.</returns>
        public static ObjectPool<RpcContext> CreatePool(int maxRetained = RpcProtocolConstants.DefaultObjectPoolMaxRetained)
        {
            return new DefaultObjectPoolProvider { MaximumRetained = maxRetained }.Create(new RpcContextPolicy());
        }

        /// <summary>
        /// 当池中没有可用对象时，创建一个新的 Context 实例 / 
        /// Creates a new Context instance when no objects are available in the pool.
        /// 初始分配 4KB 缓冲区（4096）通常比 1KB 更具通用性 / 
        /// Initial 4KB buffer allocation (4096) is generally more versatile than 1KB.
        /// </summary>
        /// <returns>新的 RpcContext 实例 / A new RpcContext instance.</returns>
        public RpcContext Create()
        {
            return new RpcContext(new ArrayBufferWriter<byte>(4096));
        }

        /// <summary>
        /// 当对象返回池中时调用 / Called when an object is returned to the pool.
        /// 如果缓冲区超过 1MB 则拒绝回收，避免长期持有大块内存 / 
        /// Rejects return if the buffer exceeds 1MB, preventing long-term retention of large memory blocks.
        /// </summary>
        /// <param name="obj">要归还的 RpcContext 实例 / The RpcContext instance to return.</param>
        /// <returns>如果可以回收到池中返回 true，否则返回 false / 
        /// true if the object can be returned to the pool; otherwise, false.</returns>
        public bool Return(RpcContext obj)
        {
            const int MaxPooledBufferSize = 1024 * 1024;
            if (obj.ResponseBuffer is ArrayBufferWriter<byte> writer && writer.Capacity > MaxPooledBufferSize)
                return false;

            obj.Reset();
            return true;
        }
    }
}
