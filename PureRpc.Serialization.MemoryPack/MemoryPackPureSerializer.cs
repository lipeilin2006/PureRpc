using System.Buffers;
using MemoryPack;
using PureRpc.Abstractions;

namespace PureRpc.Serialization.MemoryPack;

internal sealed class MemoryPackPureSerializer : ISerializer
{
    private const int MaxDeserializeSize = 64 * 1024 * 1024;

    public void Serialize<T>(IBufferWriter<byte> writer, T value)
    {
        MemoryPackSerializer.Serialize(writer, value);
    }

    public T Deserialize<T>(ReadOnlySequence<byte> sequence)
    {
        if (sequence.Length > MaxDeserializeSize)
            throw new InvalidOperationException($"Deserialization input exceeds maximum size of {MaxDeserializeSize} bytes.");
        return MemoryPackSerializer.Deserialize<T>(sequence)!;
    }
}
