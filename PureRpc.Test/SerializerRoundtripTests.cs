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
    public void MemoryPack_Roundtrip_String()
    {
        var ser = BuildSerializer(b => b.WithMemoryPackSerializer());
        var writer = new ArrayBufferWriter<byte>();
        ser.Serialize(writer, "hello");
        var r = ser.Deserialize<string>(new ReadOnlySequence<byte>(writer.WrittenMemory));
        Assert.Equal("hello", r);
    }

    [Fact]
    public void MemoryPack_Roundtrip_Int()
    {
        var ser = BuildSerializer(b => b.WithMemoryPackSerializer());
        var writer = new ArrayBufferWriter<byte>();
        ser.Serialize(writer, 42);
        var r = ser.Deserialize<int>(new ReadOnlySequence<byte>(writer.WrittenMemory));
        Assert.Equal(42, r);
    }

    [Fact]
    public void MemoryPack_Roundtrip_NestedTypes()
    {
        var ser = BuildSerializer(b => b.WithMemoryPackSerializer());
        var writer = new ArrayBufferWriter<byte>();
        var req = new EchoHeaderRequest("test-header");
        ser.Serialize(writer, req);
        var r = ser.Deserialize<EchoHeaderRequest>(new ReadOnlySequence<byte>(writer.WrittenMemory));
        Assert.Equal("test-header", r.HeaderName);
    }

    [Fact]
    public void Json_Roundtrip_Int()
    {
        var ser = BuildSerializer(b => b.WithJsonSerializer());
        var writer = new ArrayBufferWriter<byte>();
        ser.Serialize(writer, 42);
        var r = ser.Deserialize<int>(new ReadOnlySequence<byte>(writer.WrittenMemory));
        Assert.Equal(42, r);
    }

    [Fact]
    public void Json_Roundtrip_String()
    {
        var ser = BuildSerializer(b => b.WithJsonSerializer());
        var writer = new ArrayBufferWriter<byte>();
        ser.Serialize(writer, "hello json");
        var r = ser.Deserialize<string>(new ReadOnlySequence<byte>(writer.WrittenMemory));
        Assert.Equal("hello json", r);
    }

    [Fact]
    public void MessagePack_Roundtrip_Int()
    {
        var ser = BuildSerializer(b => b.WithMessagePackSerializer());
        var writer = new ArrayBufferWriter<byte>();
        ser.Serialize(writer, 42);
        var r = ser.Deserialize<int>(new ReadOnlySequence<byte>(writer.WrittenMemory));
        Assert.Equal(42, r);
    }

    [Fact]
    public void MessagePack_Roundtrip_String()
    {
        var ser = BuildSerializer(b => b.WithMessagePackSerializer());
        var writer = new ArrayBufferWriter<byte>();
        ser.Serialize(writer, "Hello");
        var r = ser.Deserialize<string>(new ReadOnlySequence<byte>(writer.WrittenMemory));
        Assert.Equal("Hello", r);
    }

    [Fact]
    public void MessagePack_Roundtrip_MultiSegment()
    {
        var ser = BuildSerializer(b => b.WithMessagePackSerializer());
        var writer = new ArrayBufferWriter<byte>();
        ser.Serialize(writer, 42);
        var bytes = writer.WrittenMemory.ToArray();
        var seq = CreateMultiSegmentSequence(bytes, 2);
        var r = ser.Deserialize<int>(seq);
        Assert.Equal(42, r);
    }

    [Fact]
    public void Protobuf_Roundtrip_Int()
    {
        var ser = BuildSerializer(b => b.WithProtobufSerializer());
        var writer = new ArrayBufferWriter<byte>();
        ser.Serialize(writer, 99);
        var r = ser.Deserialize<int>(new ReadOnlySequence<byte>(writer.WrittenMemory));
        Assert.Equal(99, r);
    }

    [Fact]
    public void Protobuf_Roundtrip_MultiSegment()
    {
        var ser = BuildSerializer(b => b.WithProtobufSerializer());
        var writer = new ArrayBufferWriter<byte>();
        ser.Serialize(writer, 42);
        var bytes = writer.WrittenMemory.ToArray();
        var seq = CreateMultiSegmentSequence(bytes, 1);
        var r = ser.Deserialize<int>(seq);
        Assert.Equal(42, r);
    }

    private static ReadOnlySequence<byte> CreateMultiSegmentSequence(byte[] data, int splitPos)
    {
        if (splitPos >= data.Length) return new ReadOnlySequence<byte>(data);
        var segment1 = new SimpleSegment(data, 0, splitPos);
        var segment2 = new SimpleSegment(data, splitPos, data.Length - splitPos);
        segment1.Next = segment2;
        return new ReadOnlySequence<byte>(segment1, 0, segment2, segment2.Memory.Length);
    }

    private sealed class SimpleSegment : ReadOnlySequenceSegment<byte>
    {
        public SimpleSegment(byte[] data, int offset, int count)
        {
            Memory = new ReadOnlyMemory<byte>(data, offset, count);
            RunningIndex = 0;
        }

        public new SimpleSegment? Next
        {
            get => (SimpleSegment?)base.Next;
            set
            {
                base.Next = value;
                if (value != null)
                    value.RunningIndex = RunningIndex + Memory.Length;
            }
        }
    }
}