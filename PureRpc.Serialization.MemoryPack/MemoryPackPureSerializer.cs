using System.Buffers;
using MemoryPack;
using PureRpc.Abstractions;

namespace PureRpc.Serialization.MemoryPack;

/// <summary>
/// 基于 MemoryPack 的高性能序列化器实现 / High-performance serializer implementation based on MemoryPack.
/// 使用 MemoryPack 的零拷贝管道操作实现序列化与反序列化 / 
/// Uses MemoryPack's zero-copy pipe operations for serialization and deserialization.
/// </summary>
internal sealed class MemoryPackPureSerializer : ISerializer
{
    /// <summary>
    /// 反序列化的最大允许大小（64MB），防止恶意过大输入导致内存耗尽 / 
    /// Maximum allowed size for deserialization (64MB), preventing out-of-memory from maliciously large input.
    /// </summary>
    private const int MaxDeserializeSize = 64 * 1024 * 1024;

    /// <summary>
    /// 将对象序列化到缓冲区写入器 / Serializes an object to the buffer writer.
    /// </summary>
    /// <typeparam name="T">对象类型 / The type of the object to serialize.</typeparam>
    /// <param name="writer">高性能缓冲区写入器 / High-performance buffer writer.</param>
    /// <param name="value">要序列化的值 / The value to serialize.</param>
    public void Serialize<T>(IBufferWriter<byte> writer, T value)
    {
        MemoryPackSerializer.Serialize(writer, value);
    }

    /// <summary>
    /// 从只读序列中反序列化对象 / Deserializes an object from a read-only byte sequence.
    /// </summary>
    /// <typeparam name="T">对象类型 / The type of the object to deserialize.</typeparam>
    /// <param name="sequence">只读字节序列 / Read-only byte sequence.</param>
    /// <returns>反序列化后的对象 / The deserialized object.</returns>
    /// <exception cref="InvalidOperationException">当输入大小超过 <see cref="MaxDeserializeSize"/> 时抛出 / 
    /// Thrown when input size exceeds <see cref="MaxDeserializeSize"/>.</exception>
    public T Deserialize<T>(ReadOnlySequence<byte> sequence)
    {
        if (sequence.Length > MaxDeserializeSize)
            throw new InvalidOperationException($"Deserialization input exceeds maximum size of {MaxDeserializeSize} bytes.");
        return MemoryPackSerializer.Deserialize<T>(sequence)!;
    }
}