using System.Buffers;
using ProtoBuf;
using ProtoBuf.Meta;
using PureRpc.Abstractions;

namespace PureRpc.Serialization.Protobuf;

/// <summary>
/// 基于 protobuf-net 的序列化器实现 / Serializer implementation based on protobuf-net.
/// 使用 RuntimeTypeModel 进行序列化与反序列化 / 
/// Uses RuntimeTypeModel for serialization and deserialization.
/// </summary>
internal sealed class ProtobufPureSerializer : ISerializer
{
    /// <summary>
    /// 预编译的序列化器模型 / Pre-compiled serializer model.
    /// 使用 RuntimeTypeModel.Default 以利用 protobuf-net 的类型自动发现 / 
    /// Uses RuntimeTypeModel.Default to leverage protobuf-net's automatic type discovery.
    /// </summary>
    private static readonly RuntimeTypeModel Model = RuntimeTypeModel.Default;

    /// <summary>
    /// 将对象序列化到缓冲区写入器 / Serializes an object to the buffer writer.
    /// </summary>
    /// <typeparam name="T">对象类型 / The type of the object to serialize.</typeparam>
    /// <param name="writer">高性能缓冲区写入器 / High-performance buffer writer.</param>
    /// <param name="value">要序列化的值 / The value to serialize.</param>
    public void Serialize<T>(IBufferWriter<byte> writer, T value)
    {
        using var ms = new RecyclableBufferStream(writer);
        Model.Serialize(ms, value);
    }

    /// <summary>
    /// 从只读序列中反序列化对象 / Deserializes an object from a read-only byte sequence.
    /// </summary>
    /// <typeparam name="T">对象类型 / The type of the object to deserialize.</typeparam>
    /// <param name="sequence">只读字节序列 / Read-only byte sequence.</param>
    /// <returns>反序列化后的对象 / The deserialized object.</returns>
    public T Deserialize<T>(ReadOnlySequence<byte> sequence)
    {
        if (sequence.IsSingleSegment)
        {
            return Model.Deserialize<T>(new ReadOnlyMemoryStream(sequence.First));
        }
        var combined = sequence.ToArray();
        return Model.Deserialize<T>(new MemoryStream(combined));
    }

    /// <summary>
    /// 包装 IBufferWriter 的 Stream，避免额外的 ToArray() 拷贝 / 
    /// Stream wrapping IBufferWriter, avoiding extra ToArray() copies.
    /// </summary>
    private sealed class RecyclableBufferStream : Stream
    {
        private readonly IBufferWriter<byte> _writer;

        /// <summary>
        /// 获取当前流是否支持写入 / Gets whether the current stream supports writing.
        /// </summary>
        public override bool CanWrite => true;

        /// <summary>
        /// 获取当前流是否支持读取 / Gets whether the current stream supports reading.
        /// </summary>
        public override bool CanRead => false;

        /// <summary>
        /// 获取当前流是否支持定位 / Gets whether the current stream supports seeking.
        /// </summary>
        public override bool CanSeek => false;

        /// <summary>
        /// 获取流的长度（不支持） / Gets the length of the stream (not supported).
        /// </summary>
        /// <exception cref="NotSupportedException">始终抛出 / Always thrown.</exception>
        public override long Length => throw new NotSupportedException();

        /// <summary>
        /// 获取或设置流中的当前位置（不支持） / Gets or sets the current position in the stream (not supported).
        /// </summary>
        /// <exception cref="NotSupportedException">始终抛出 / Always thrown.</exception>
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        /// <summary>
        /// 初始化 RecyclableBufferStream 实例 / Initializes a RecyclableBufferStream instance.
        /// </summary>
        /// <param name="writer">缓冲区写入器 / The buffer writer.</param>
        public RecyclableBufferStream(IBufferWriter<byte> writer) => _writer = writer;

        /// <summary>
        /// 将字节数组写入缓冲区 / Writes a byte array to the buffer.
        /// </summary>
        /// <param name="buffer">要写入的字节数组 / The byte array to write.</param>
        /// <param name="offset">起始偏移量 / The starting offset.</param>
        /// <param name="count">要写入的字节数 / The number of bytes to write.</param>
        public override void Write(byte[] buffer, int offset, int count)
        {
            var span = _writer.GetSpan(count);
            buffer.AsSpan(offset, count).CopyTo(span);
            _writer.Advance(count);
        }

        /// <summary>
        /// 刷新缓冲区（空操作） / Flushes the buffer (no-op).
        /// </summary>
        public override void Flush() { }

        /// <summary>
        /// 从流中读取数据（不支持） / Reads data from the stream (not supported).
        /// </summary>
        /// <param name="buffer">目标缓冲区 / The destination buffer.</param>
        /// <param name="offset">起始偏移量 / The starting offset.</param>
        /// <param name="count">要读取的字节数 / The number of bytes to read.</param>
        /// <returns>实际读取的字节数 / The number of bytes actually read.</returns>
        /// <exception cref="NotSupportedException">始终抛出 / Always thrown.</exception>
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        /// <summary>
        /// 设置流的长度（不支持） / Sets the length of the stream (not supported).
        /// </summary>
        /// <param name="value">流的长度 / The length of the stream.</param>
        /// <exception cref="NotSupportedException">始终抛出 / Always thrown.</exception>
        public override void SetLength(long value) => throw new NotSupportedException();

        /// <summary>
        /// 设置流的当前位置（不支持） / Sets the current position of the stream (not supported).
        /// </summary>
        /// <param name="offset">偏移量 / The offset.</param>
        /// <param name="origin">起始位置 / The origin.</param>
        /// <returns>新位置 / The new position.</returns>
        /// <exception cref="NotSupportedException">始终抛出 / Always thrown.</exception>
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    }

    /// <summary>
    /// ReadOnlyMemory 支持的 Stream，避免 ToArray() 拷贝 / 
    /// Stream backed by ReadOnlyMemory, avoiding ToArray() copies.
    /// </summary>
    private sealed class ReadOnlyMemoryStream : Stream
    {
        private readonly ReadOnlyMemory<byte> _memory;
        private int _position;

        /// <summary>
        /// 获取当前流是否支持读取 / Gets whether the current stream supports reading.
        /// </summary>
        public override bool CanRead => true;

        /// <summary>
        /// 获取当前流是否支持写入 / Gets whether the current stream supports writing.
        /// </summary>
        public override bool CanWrite => false;

        /// <summary>
        /// 获取当前流是否支持定位 / Gets whether the current stream supports seeking.
        /// </summary>
        public override bool CanSeek => false;

        /// <summary>
        /// 获取流的长度 / Gets the length of the stream.
        /// </summary>
        public override long Length => _memory.Length;

        /// <summary>
        /// 获取或设置流中的当前位置（不支持设置） / Gets the current position in the stream (setting not supported).
        /// </summary>
        /// <exception cref="NotSupportedException">设置时始终抛出 / Always thrown when set.</exception>
        public override long Position { get => _position; set => throw new NotSupportedException(); }

        /// <summary>
        /// 初始化 ReadOnlyMemoryStream 实例 / Initializes a ReadOnlyMemoryStream instance.
        /// </summary>
        /// <param name="memory">只读内存区域 / The read-only memory region.</param>
        public ReadOnlyMemoryStream(ReadOnlyMemory<byte> memory) => _memory = memory;

        /// <summary>
        /// 从流中读取数据到缓冲区 / Reads data from the stream into a buffer.
        /// </summary>
        /// <param name="buffer">目标缓冲区 / The destination buffer.</param>
        /// <param name="offset">起始偏移量 / The starting offset.</param>
        /// <param name="count">要读取的字节数 / The number of bytes to read.</param>
        /// <returns>实际读取的字节数 / The number of bytes actually read.</returns>
        public override int Read(byte[] buffer, int offset, int count)
        {
            int remaining = _memory.Length - _position;
            int toCopy = Math.Min(count, remaining);
            if (toCopy <= 0) return 0;
            _memory.Slice(_position, toCopy).Span.CopyTo(buffer.AsSpan(offset, toCopy));
            _position += toCopy;
            return toCopy;
        }

        /// <summary>
        /// 刷新缓冲区（空操作） / Flushes the buffer (no-op).
        /// </summary>
        public override void Flush() { }

        /// <summary>
        /// 写入数据到流（不支持） / Writes data to the stream (not supported).
        /// </summary>
        /// <param name="buffer">要写入的字节数组 / The byte array to write.</param>
        /// <param name="offset">起始偏移量 / The starting offset.</param>
        /// <param name="count">要写入的字节数 / The number of bytes to write.</param>
        /// <exception cref="NotSupportedException">始终抛出 / Always thrown.</exception>
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        /// <summary>
        /// 设置流的当前位置（不支持） / Sets the current position of the stream (not supported).
        /// </summary>
        /// <param name="offset">偏移量 / The offset.</param>
        /// <param name="origin">起始位置 / The origin.</param>
        /// <returns>新位置 / The new position.</returns>
        /// <exception cref="NotSupportedException">始终抛出 / Always thrown.</exception>
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        /// <summary>
        /// 设置流的长度（不支持） / Sets the length of the stream (not supported).
        /// </summary>
        /// <param name="value">流的长度 / The length of the stream.</param>
        /// <exception cref="NotSupportedException">始终抛出 / Always thrown.</exception>
        public override void SetLength(long value) => throw new NotSupportedException();
    }
}