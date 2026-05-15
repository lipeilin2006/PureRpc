using System.Buffers;
using PureRpc.Abstractions;
using MsgPack = MessagePack;

namespace PureRpc.Serialization.MessagePack;

internal sealed class MessagePackPureSerializer : ISerializer
{
    public void Serialize<T>(IBufferWriter<byte> writer, T value)
    {
        MsgPack.MessagePackSerializer.Serialize(writer, value);
    }

    public T Deserialize<T>(ReadOnlySequence<byte> sequence)
    {
        return MsgPack.MessagePackSerializer.Deserialize<T>(sequence);
    }
}
