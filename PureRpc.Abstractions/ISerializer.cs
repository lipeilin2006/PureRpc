using System.Buffers;

namespace PureRpc.Abstractions
{
    /// <summary>
    /// 定义 PureRpc 的序列化器接口 / Defines the serializer interface for PureRpc.
    /// 支持 MemoryPack 和 MessagePack 的高性能管道操作 / Supports high-performance pipe operations with MemoryPack and MessagePack.
    /// </summary>
    public interface ISerializer
    {
        /// <summary>
        /// 序列化对象到缓冲区 / Serializes an object to the buffer writer.
        /// </summary>
        /// <typeparam name="T">对象类型 / The type of the object to serialize.</typeparam>
        /// <param name="writer">高性能缓冲区写入器 (来自 System.Buffers) / High-performance buffer writer (from System.Buffers).</param>
        /// <param name="value">要序列化的值 / The value to serialize.</param>
        void Serialize<T>(IBufferWriter<byte> writer, T value);

        /// <summary>
        /// 从只读序列中反序列化对象 / Deserializes an object from a read-only byte sequence.
        /// </summary>
        /// <typeparam name="T">对象类型 / The type of the object to deserialize.</typeparam>
        /// <param name="sequence">只读字节序列 (可能是不连续的内存块) / Read-only byte sequence (may consist of discontiguous memory blocks).</param>
        /// <returns>反序列化后的对象 / The deserialized object.</returns>
        T Deserialize<T>(ReadOnlySequence<byte> sequence);
    }
}
