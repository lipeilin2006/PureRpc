using System.Buffers;
using Microsoft.Extensions.DependencyInjection;
using PureRpc.Abstractions;

namespace PureRpc.Test;

public sealed class SerializerRoundtripTests
{
    private static ISerializer BuildSerializer(Action<IClientBuilder> configure)
    {
        var services = new ServiceCollection();
        configure(services.AddPureRpcClient());
        var sp = services.BuildServiceProvider();
        return sp.GetRequiredService<ISerializer>();
    }

    [Fact]
    public void MemoryPack_Roundtrip_AddRequest()
    {
        var ser = BuildSerializer(b => b.WithMemoryPackSerializer());
        var writer = new ArrayBufferWriter<byte>();
        ser.Serialize(writer, new AddRequest(10, 20));
        var r = ser.Deserialize<AddRequest>(new ReadOnlySequence<byte>(writer.WrittenMemory));
        Assert.Equal(10, r.A); Assert.Equal(20, r.B);
    }

    [Fact]
    public void Json_Roundtrip()
    {
        var ser = BuildSerializer(b => b.WithJsonSerializer());
        var writer = new ArrayBufferWriter<byte>();
        ser.Serialize(writer, 42);
        var r = ser.Deserialize<int>(new ReadOnlySequence<byte>(writer.WrittenMemory));
        Assert.Equal(42, r);
    }

    [Fact]
    public void MessagePack_Roundtrip()
    {
        var ser = BuildSerializer(b => b.WithMessagePackSerializer());
        var writer = new ArrayBufferWriter<byte>();
        ser.Serialize(writer, "Hello");
        var r = ser.Deserialize<string>(new ReadOnlySequence<byte>(writer.WrittenMemory));
        Assert.Equal("Hello", r);
    }

    [Fact]
    public void Protobuf_Roundtrip()
    {
        var ser = BuildSerializer(b => b.WithProtobufSerializer());
        var writer = new ArrayBufferWriter<byte>();
        ser.Serialize(writer, 99);
        var r = ser.Deserialize<int>(new ReadOnlySequence<byte>(writer.WrittenMemory));
        Assert.Equal(99, r);
    }
}
