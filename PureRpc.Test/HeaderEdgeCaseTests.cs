using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Text;
using PureRpc.Abstractions;

namespace PureRpc.Test;

public sealed class HeaderEdgeCaseTests
{
    private static byte[] BuildRequestFrameWithHeaders(Dictionary<string, string> headers)
    {
        var svcBytes = Encoding.UTF8.GetBytes("Svc");
        var metBytes = Encoding.UTF8.GetBytes("Met");
        var headerInfo = RpcFrameCodec.PrepareHeaders(headers);

        int innerLen = 1 + 4 + 4 + svcBytes.Length + 4 + metBytes.Length + 4 + headerInfo.HeadersBlockSize;
        var buf = new byte[4 + innerLen];
        BinaryPrimitives.WriteInt32LittleEndian(buf, innerLen);
        int offset = RpcFrameCodec.WriteRequestSpan(buf.AsSpan(4), 1, "Svc", "Met",
            in headerInfo, svcBytes.Length, metBytes.Length);
        return buf;
    }

    [Fact]
    public void UnicodeCharacters_InHeaders_Roundtrip()
    {
        var headers = new Dictionary<string, string>
        {
            ["用户"] = "张三",
            ["ключ"] = "значение"
        };
        var frame = BuildRequestFrameWithHeaders(headers);
        var buffer = new ReadOnlySequence<byte>(frame);
        var activeRequests = new ConcurrentDictionary<(long, uint), CancellationTokenSource>();

        var result = RpcFrameCodec.TryParseRequest(ref buffer, 1, activeRequests,
            out var request, out _);

        Assert.True(result);
        Assert.Equal("张三", request.Headers!["用户"]);
        Assert.Equal("значение", request.Headers!["ключ"]);
    }

    [Fact]
    public void EmptyStringValue_RejectedByCodec()
    {
        var headers = new Dictionary<string, string>
        {
            ["key"] = ""
        };
        var info = RpcFrameCodec.PrepareHeaders(headers);
        var buf = new byte[4 + info.HeadersBlockSize];
        RpcFrameCodec.WriteHeaders(buf, 0, in info);

        int offset = 4;
        var result = RpcFrameCodec.TryParseHeadersSpan(buf, ref offset, 1, out var parsed);

        Assert.False(result);
    }

    [Fact]
    public void MaxHeaderCount_64Headers_Roundtrip()
    {
        var headers = new Dictionary<string, string>();
        for (int i = 0; i < 64; i++)
            headers[$"h{i}"] = $"v{i}";

        var frame = BuildRequestFrameWithHeaders(headers);
        var buffer = new ReadOnlySequence<byte>(frame);
        var activeRequests = new ConcurrentDictionary<(long, uint), CancellationTokenSource>();

        var result = RpcFrameCodec.TryParseRequest(ref buffer, 1, activeRequests,
            out var request, out _);

        Assert.True(result);
        Assert.Equal(64, request.Headers!.Count);
    }

    [Fact]
    public void MaxHeaderKeyLength_256Bytes_Roundtrip()
    {
        var key = new string('K', 256);
        var headers = new Dictionary<string, string> { [key] = "val" };
        var frame = BuildRequestFrameWithHeaders(headers);
        var buffer = new ReadOnlySequence<byte>(frame);
        var activeRequests = new ConcurrentDictionary<(long, uint), CancellationTokenSource>();

        var result = RpcFrameCodec.TryParseRequest(ref buffer, 1, activeRequests,
            out var request, out _);

        Assert.True(result);
        Assert.Equal("val", request.Headers![key]);
    }

    [Fact]
    public void MaxHeaderValueLength_4096Bytes_Roundtrip()
    {
        var value = new string('V', 4096);
        var headers = new Dictionary<string, string> { ["key"] = value };
        var frame = BuildRequestFrameWithHeaders(headers);
        var buffer = new ReadOnlySequence<byte>(frame);
        var activeRequests = new ConcurrentDictionary<(long, uint), CancellationTokenSource>();

        var result = RpcFrameCodec.TryParseRequest(ref buffer, 1, activeRequests,
            out var request, out _);

        Assert.True(result);
        Assert.Equal(value, request.Headers!["key"]);
    }

    [Fact]
    public void OverLimit_HeaderCount_Exceeds64_RejectedByTryParseHeadersSpan()
    {
        var buf = new byte[0];
        int offset = 0;
        var result = RpcFrameCodec.TryParseHeadersSpan(buf, ref offset, 65, out var parsed);

        Assert.False(result);
    }

    [Fact]
    public void OverLimit_HeaderCount_Exceeds64_RejectedByTryParseHeadersSequence()
    {
        var seq = new ReadOnlySequence<byte>(Array.Empty<byte>());
        var reader = new SequenceReader<byte>(seq);
        var result = RpcFrameCodec.TryParseHeadersSequence(ref reader, 65, out var parsed);

        Assert.False(result);
    }

    [Fact]
    public void OverLimit_KeyLength_Exceeds256_RejectedByTryParseHeadersSpan()
    {
        var key = new string('K', 257);
        var val = "v";
        var keyBytes = Encoding.UTF8.GetBytes(key);
        var valBytes = Encoding.UTF8.GetBytes(val);
        var buf = new byte[4 + keyBytes.Length + 4 + valBytes.Length];
        int off = 0;
        BinaryPrimitives.WriteInt32LittleEndian(buf.AsSpan(off, 4), keyBytes.Length); off += 4;
        keyBytes.CopyTo(buf.AsSpan(off)); off += keyBytes.Length;
        BinaryPrimitives.WriteInt32LittleEndian(buf.AsSpan(off, 4), valBytes.Length); off += 4;
        valBytes.CopyTo(buf.AsSpan(off)); off += valBytes.Length;

        int readOffset = 0;
        var result = RpcFrameCodec.TryParseHeadersSpan(buf, ref readOffset, 1, out var parsed);

        Assert.False(result);
    }

    [Fact]
    public void OverLimit_ValueLength_Exceeds4096_RejectedByTryParseHeadersSpan()
    {
        var key = "k";
        var val = new string('V', 4097);
        var keyBytes = Encoding.UTF8.GetBytes(key);
        var valBytes = Encoding.UTF8.GetBytes(val);
        var buf = new byte[4 + keyBytes.Length + 4 + valBytes.Length];
        int off = 0;
        BinaryPrimitives.WriteInt32LittleEndian(buf.AsSpan(off, 4), keyBytes.Length); off += 4;
        keyBytes.CopyTo(buf.AsSpan(off)); off += keyBytes.Length;
        BinaryPrimitives.WriteInt32LittleEndian(buf.AsSpan(off, 4), valBytes.Length); off += 4;
        valBytes.CopyTo(buf.AsSpan(off)); off += valBytes.Length;

        int readOffset = 0;
        var result = RpcFrameCodec.TryParseHeadersSpan(buf, ref readOffset, 1, out var parsed);

        Assert.False(result);
    }

    [Fact]
    public void OverLimit_KeyLength_Exceeds256_RejectedByTryReadString()
    {
        var key = new string('K', 257);
        var keyBytes = Encoding.UTF8.GetBytes(key);
        var buf = new byte[4 + keyBytes.Length];
        BinaryPrimitives.WriteInt32LittleEndian(buf, keyBytes.Length);
        keyBytes.CopyTo(buf.AsSpan(4));
        var seq = new ReadOnlySequence<byte>(buf);
        var reader = new SequenceReader<byte>(seq);

        var result = RpcFrameCodec.TryReadString(ref reader, out var value, RpcProtocolConstants.MaxHeaderKeyLength);

        Assert.False(result);
    }

    [Fact]
    public void OverLimit_ValueLength_Exceeds4096_RejectedByTryReadString()
    {
        var val = new string('V', 4097);
        var valBytes = Encoding.UTF8.GetBytes(val);
        var buf = new byte[4 + valBytes.Length];
        BinaryPrimitives.WriteInt32LittleEndian(buf, valBytes.Length);
        valBytes.CopyTo(buf.AsSpan(4));
        var seq = new ReadOnlySequence<byte>(buf);
        var reader = new SequenceReader<byte>(seq);

        var result = RpcFrameCodec.TryReadString(ref reader, out var value, RpcProtocolConstants.MaxHeaderValueLength);

        Assert.False(result);
    }
}
