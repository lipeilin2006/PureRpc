using System.Buffers;
using PureRpc.Abstractions;
using MsgPack = MessagePack;

namespace PureRpc.Serialization.MessagePack;

/// <summary>
/// 基于 MessagePack 的序列化器实现 / Serializer implementation based on MessagePack.
/// 使用 MessagePack 的高性能管道操作实现序列化与反序列化 / 
/// Uses MessagePack's high-performance pipe operations for serialization and deserialization.
/// </summary>
internal sealed class MessagePackPureSerializer : ISerializer
{
    /// <summary>
    /// 将对象序列化到缓冲区写入器 / Serializes an object to the buffer writer.
    /// </summary>
    /// <typeparam name="T">对象类型 / The type of the object to serialize.</typeparam>
    /// <param name="writer">高性能缓冲区写入器 / High-performance buffer writer.</param>
    /// <param name="value">要序列化的值 / The value to serialize.</param>
    public void Serialize<T>(IBufferWriter<byte> writer, T value)
    {
        MsgPack.MessagePackSerializer.Serialize(writer, value);
    }

    /// <summary>
    /// 从只读序列中反序列化对象 / Deserializes an object from a read-only byte sequence.
    /// </summary>
    /// <typeparam name="T">对象类型 / The type of the object to deserialize.</typeparam>
    /// <param name="sequence">只读字节序列 / Read-only byte sequence.</param>
    /// <returns>反序列化后的对象 / The deserialized object.</returns>
    public T Deserialize<T>(ReadOnlySequence<byte> sequence)
    {
        return MsgPack.MessagePackSerializer.Deserialize<T>(sequence);
    }
}