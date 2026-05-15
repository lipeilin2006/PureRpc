using System.Buffers;
using ProtoBuf;
using ProtoBuf.Meta;
using PureRpc.Abstractions;

namespace PureRpc.Serialization.Protobuf;

internal sealed class ProtobufPureSerializer : ISerializer
{
    // 预编译序列化器以减少运行时反射
    private static readonly RuntimeTypeModel Model = RuntimeTypeModel.Default;

    public void Serialize<T>(IBufferWriter<byte> writer, T value)
    {
        // 直接序列化到 StreamWriter 再转 IBufferWriter
        using var ms = new RecyclableBufferStream(writer);
        Model.Serialize(ms, value);
    }

    public T Deserialize<T>(ReadOnlySequence<byte> sequence)
    {
        if (sequence.IsSingleSegment)
        {
            return Model.Deserialize<T>(new ReadOnlyMemoryStream(sequence.First));
        }
        // 多段回退到合并 byte[]
        var combined = sequence.ToArray();
        return Model.Deserialize<T>(new MemoryStream(combined));
    }

    /// <summary>包装 IBufferWriter 的 Stream，避免额外的 ToArray() 拷贝</summary>
    private sealed class RecyclableBufferStream : Stream
    {
        private readonly IBufferWriter<byte> _writer;

        public override bool CanWrite => true;
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public RecyclableBufferStream(IBufferWriter<byte> writer) => _writer = writer;

        public override void Write(byte[] buffer, int offset, int count)
        {
            var span = _writer.GetSpan(count);
            buffer.AsSpan(offset, count).CopyTo(span);
            _writer.Advance(count);
        }

        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }

    /// <summary>ReadOnlyMemory-backed Stream 避免 ToArray()</summary>
    private sealed class ReadOnlyMemoryStream : Stream
    {
        private readonly ReadOnlyMemory<byte> _memory;
        private int _position;

        public override bool CanRead => true;
        public override bool CanWrite => false;
        public override bool CanSeek => false;
        public override long Length => _memory.Length;
        public override long Position { get => _position; set => throw new NotSupportedException(); }

        public ReadOnlyMemoryStream(ReadOnlyMemory<byte> memory) => _memory = memory;

        public override int Read(byte[] buffer, int offset, int count)
        {
            int remaining = _memory.Length - _position;
            int toCopy = Math.Min(count, remaining);
            if (toCopy <= 0) return 0;
            _memory.Slice(_position, toCopy).Span.CopyTo(buffer.AsSpan(offset, toCopy));
            _position += toCopy;
            return toCopy;
        }

        public override void Flush() { }
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }
}
