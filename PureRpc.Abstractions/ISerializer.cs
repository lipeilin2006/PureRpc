using System.Buffers;

namespace PureRpc.Abstractions
{
    /// <summary>
    /// 定义 PureRpc 的序列化器接口
    /// 支持 MemoryPack 和 MessagePack 的高性能管道操作
    /// </summary>
    public interface ISerializer
    {
        /// <summary>
        /// 序列化对象到缓冲区
        /// </summary>
        /// <typeparam name="T">对象类型</typeparam>
        /// <param name="writer">高性能缓冲区写入器 (来自 System.Buffers)</param>
        /// <param name="value">要序列化的值</param>
        void Serialize<T>(IBufferWriter<byte> writer, T value);

        /// <summary>
        /// 从只读序列中反序列化对象
        /// </summary>
        /// <typeparam name="T">对象类型</typeparam>
        /// <param name="sequence">只读字节序列 (可能是不连续的内存块)</param>
        /// <returns>反序列化后的对象</returns>
        T Deserialize<T>(ReadOnlySequence<byte> sequence);
    }
}