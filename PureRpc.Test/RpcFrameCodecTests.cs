using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Text;
using PureRpc.Abstractions;

namespace PureRpc.Test;

public sealed class RpcFrameCodecTests
{
    private static byte[] BuildRequestFrame(uint requestId, string serviceName, string methodName,
        ReadOnlySpan<byte> payload, IReadOnlyDictionary<string, string>? headers = null)
    {
        var svcBytes = Encoding.UTF8.GetBytes(serviceName);
        var metBytes = Encoding.UTF8.GetBytes(methodName);
        var headerInfo = RpcFrameCodec.PrepareHeaders(headers);

        int innerLen = 1 + 4 + 4 + svcBytes.Length + 4 + metBytes.Length + headerInfo.HeadersBlockSize + 4 + payload.Length;
        int frameLen = 4 + innerLen;

        var buf = new byte[frameLen];
        BinaryPrimitives.WriteInt32LittleEndian(buf, innerLen);
        int offset = RpcFrameCodec.WriteRequestSpan(buf.AsSpan(4), requestId, serviceName, methodName,
            in headerInfo, svcBytes.Length, metBytes.Length);
        payload.CopyTo(buf.AsSpan(4 + offset));
        return buf;
    }

    private static byte[] BuildResponseFrame(uint requestId, bool isError, ReadOnlySpan<byte> payload,
        IReadOnlyDictionary<string, string>? headers = null)
    {
        var headerInfo = RpcFrameCodec.PrepareHeaders(headers);
        int headerFieldSize = 4;

        int innerLen = 1 + 4 + 4 + headerFieldSize + headerInfo.HeadersBlockSize + payload.Length;
        int frameLen = 4 + innerLen;

        var buf = new byte[frameLen];
        BinaryPrimitives.WriteInt32LittleEndian(buf, innerLen);
        int offset = RpcFrameCodec.WriteResponseSpan(buf.AsSpan(4), requestId, isError, in headerInfo, payload.Length);
        payload.CopyTo(buf.AsSpan(4 + offset));
        return buf;
    }

    private static byte[] BuildCancelFrame(uint requestId)
    {
        int innerLen = 1 + 4;
        var buf = new byte[4 + innerLen];
        BinaryPrimitives.WriteInt32LittleEndian(buf, innerLen);
        RpcFrameCodec.WriteCancelSpan(buf.AsSpan(4), requestId);
        return buf;
    }

    [Fact]
    public void TryParseRequest_Success()
    {
        var payload = Encoding.UTF8.GetBytes("hello world");
        var headers = new Dictionary<string, string> { ["auth"] = "token123" };
        var frame = BuildRequestFrame(42, "TestService", "TestMethod", payload, headers);
        var buffer = new ReadOnlySequence<byte>(frame);
        var activeRequests = new ConcurrentDictionary<(long, uint), CancellationTokenSource>();

        var result = RpcFrameCodec.TryParseRequest(ref buffer, 1, activeRequests,
            out var request, out var parsedPayload);

        Assert.True(result);
        Assert.Equal(42u, request.RequestId);
        Assert.Equal("TestService", request.ServiceName);
        Assert.Equal("TestMethod", request.MethodName);
        Assert.NotNull(request.Headers);
        Assert.Equal("token123", request.Headers!["auth"]);
        Assert.True(parsedPayload.ToArray().SequenceEqual(payload));
    }

    [Fact]
    public void TryParseRequest_CancelFrame_CancelsAndReturnsFalse()
    {
        var frame = BuildCancelFrame(99);
        var buffer = new ReadOnlySequence<byte>(frame);
        var cts = new CancellationTokenSource();
        var activeRequests = new ConcurrentDictionary<(long, uint), CancellationTokenSource>();
        activeRequests[(1, 99)] = cts;

        var result = RpcFrameCodec.TryParseRequest(ref buffer, 1, activeRequests,
            out var request, out var payload);

        Assert.False(result);
        Assert.True(cts.IsCancellationRequested);
        Assert.Equal(default, request);
        Assert.Equal(default, payload);
    }

    [Fact]
    public void TryParseRequest_CancelFrame_NoActiveRequest_StillReturnsFalse()
    {
        var frame = BuildCancelFrame(99);
        var buffer = new ReadOnlySequence<byte>(frame);
        var activeRequests = new ConcurrentDictionary<(long, uint), CancellationTokenSource>();

        var result = RpcFrameCodec.TryParseRequest(ref buffer, 1, activeRequests,
            out var request, out var payload);

        Assert.False(result);
        Assert.Equal(default, request);
    }

    [Fact]
    public void TryParseRequest_UnknownType_SkipsFrameAndReturnsFalse()
    {
        int innerLen = 1 + 4;
        var buf = new byte[4 + innerLen];
        BinaryPrimitives.WriteInt32LittleEndian(buf, innerLen);
        buf[4] = 99;
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(5, 4), 1);

        var buffer = new ReadOnlySequence<byte>(buf);
        var activeRequests = new ConcurrentDictionary<(long, uint), CancellationTokenSource>();

        var result = RpcFrameCodec.TryParseRequest(ref buffer, 1, activeRequests,
            out var request, out var payload);

        Assert.False(result);
        Assert.Equal(default, request);
        Assert.Equal(0, buffer.Length);
    }

    [Fact]
    public void TryParseRequest_BufferTooSmall_ReturnsFalse()
    {
        var buf = new byte[5];
        var buffer = new ReadOnlySequence<byte>(buf);
        var activeRequests = new ConcurrentDictionary<(long, uint), CancellationTokenSource>();

        var result = RpcFrameCodec.TryParseRequest(ref buffer, 1, activeRequests,
            out var request, out var payload);

        Assert.False(result);
    }

    [Fact]
    public void TryParseRequest_OversizedFrame_ReturnsFalse()
    {
        var buf = new byte[9];
        BinaryPrimitives.WriteInt32LittleEndian(buf, RpcProtocolConstants.MaxFrameSize + 1);
        buf[4] = (byte)RpcMessageType.Request;
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(5, 4), 1);

        var buffer = new ReadOnlySequence<byte>(buf);
        var activeRequests = new ConcurrentDictionary<(long, uint), CancellationTokenSource>();

        var result = RpcFrameCodec.TryParseRequest(ref buffer, 1, activeRequests,
            out var request, out var payload);

        Assert.False(result);
    }

    [Fact]
    public void TryParseRequest_OversizedServiceName_ReturnsFalse()
    {
        var longName = new string('A', RpcProtocolConstants.MaxServiceNameLength + 1);
        var svcBytes = Encoding.UTF8.GetBytes(longName);
        int innerLen = 1 + 4 + 4 + svcBytes.Length + 4;
        var buf = new byte[4 + innerLen + 100];
        BinaryPrimitives.WriteInt32LittleEndian(buf, innerLen + 100);
        buf[4] = (byte)RpcMessageType.Request;
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(5, 4), 1);
        BinaryPrimitives.WriteInt32LittleEndian(buf.AsSpan(9, 4), svcBytes.Length);
        svcBytes.CopyTo(buf.AsSpan(13));

        var buffer = new ReadOnlySequence<byte>(buf);
        var activeRequests = new ConcurrentDictionary<(long, uint), CancellationTokenSource>();

        var result = RpcFrameCodec.TryParseRequest(ref buffer, 1, activeRequests,
            out var request, out var payload);

        Assert.False(result);
    }

    [Fact]
    public void TryParseRequest_OversizedMethodName_ReturnsFalse()
    {
        var svcBytes = Encoding.UTF8.GetBytes("Svc");
        var longMet = new string('B', RpcProtocolConstants.MaxMethodNameLength + 1);
        var metBytes = Encoding.UTF8.GetBytes(longMet);
        int innerLen = 1 + 4 + 4 + svcBytes.Length + 4 + metBytes.Length + 4;
        var buf = new byte[4 + innerLen + 100];
        BinaryPrimitives.WriteInt32LittleEndian(buf, innerLen + 100);
        buf[4] = (byte)RpcMessageType.Request;
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(5, 4), 1);
        BinaryPrimitives.WriteInt32LittleEndian(buf.AsSpan(9, 4), svcBytes.Length);
        svcBytes.CopyTo(buf.AsSpan(13));
        int off = 13 + svcBytes.Length;
        BinaryPrimitives.WriteInt32LittleEndian(buf.AsSpan(off, 4), metBytes.Length);
        metBytes.CopyTo(buf.AsSpan(off + 4));

        var buffer = new ReadOnlySequence<byte>(buf);
        var activeRequests = new ConcurrentDictionary<(long, uint), CancellationTokenSource>();

        var result = RpcFrameCodec.TryParseRequest(ref buffer, 1, activeRequests,
            out var request, out var payload);

        Assert.False(result);
    }

    [Fact]
    public void TryParseRequest_TotalLenTooSmall_ReturnsFalse()
    {
        var buf = new byte[20];
        BinaryPrimitives.WriteInt32LittleEndian(buf, 6);
        buf[4] = (byte)RpcMessageType.Request;
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(5, 4), 1);
        for (int i = 9; i < 20; i++) buf[i] = 0;

        var buffer = new ReadOnlySequence<byte>(buf);
        var activeRequests = new ConcurrentDictionary<(long, uint), CancellationTokenSource>();

        var result = RpcFrameCodec.TryParseRequest(ref buffer, 1, activeRequests,
            out var request, out var payload);

        Assert.False(result);
    }

    [Fact]
    public void TryParseResponse_Success()
    {
        var payload = Encoding.UTF8.GetBytes("response data");
        var headers = new Dictionary<string, string> { ["trace-id"] = "abc" };
        var frame = BuildResponseFrame(7, false, payload, headers);
        var buffer = new ReadOnlySequence<byte>(frame);

        var result = RpcFrameCodec.TryParseResponse(ref buffer, out var response);

        Assert.True(result);
        Assert.Equal(7u, response.RequestId);
        Assert.Equal((byte)RpcMessageType.Response, response.Type);
        Assert.Equal(200, response.StatusCode);
        Assert.NotNull(response.Headers);
        Assert.Equal("abc", response.Headers!["trace-id"]);
        Assert.True(response.Payload.ToArray().SequenceEqual(payload));
    }

    [Fact]
    public void TryParseResponse_ErrorResponse()
    {
        var payload = Encoding.UTF8.GetBytes("error detail");
        var frame = BuildResponseFrame(3, true, payload);
        var buffer = new ReadOnlySequence<byte>(frame);

        var result = RpcFrameCodec.TryParseResponse(ref buffer, out var response);

        Assert.True(result);
        Assert.Equal(3u, response.RequestId);
        Assert.Equal((byte)RpcMessageType.Error, response.Type);
        Assert.Equal(500, response.StatusCode);
    }

    [Fact]
    public void TryParseResponse_BufferTooSmall_ReturnsFalse()
    {
        var buf = new byte[10];
        var buffer = new ReadOnlySequence<byte>(buf);

        var result = RpcFrameCodec.TryParseResponse(ref buffer, out var response);

        Assert.False(result);
    }

    [Fact]
    public void TryParseResponse_TotalLenTooSmall_ReturnsFalse()
    {
        var buf = new byte[20];
        BinaryPrimitives.WriteInt32LittleEndian(buf, 5);
        buf[4] = (byte)RpcMessageType.Response;
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(5, 4), 1);
        BinaryPrimitives.WriteInt32LittleEndian(buf.AsSpan(9, 4), 200);
        BinaryPrimitives.WriteInt32LittleEndian(buf.AsSpan(13, 4), 0);

        var buffer = new ReadOnlySequence<byte>(buf);

        var result = RpcFrameCodec.TryParseResponse(ref buffer, out var response);

        Assert.False(result);
    }

    [Fact]
    public void TryParseResponse_HeaderCountExceedsMax_ReturnsFalse()
    {
        var buf = new byte[30];
        BinaryPrimitives.WriteInt32LittleEndian(buf, 20);
        buf[4] = (byte)RpcMessageType.Response;
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(5, 4), 1);
        BinaryPrimitives.WriteInt32LittleEndian(buf.AsSpan(9, 4), 200);
        BinaryPrimitives.WriteInt32LittleEndian(buf.AsSpan(13, 4), 100);

        var buffer = new ReadOnlySequence<byte>(buf);

        var result = RpcFrameCodec.TryParseResponse(ref buffer, out var response);

        Assert.False(result);
    }

    [Fact]
    public void TryReadString_Success()
    {
        var str = "Hello";
        var strBytes = Encoding.UTF8.GetBytes(str);
        var buf = new byte[4 + strBytes.Length];
        BinaryPrimitives.WriteInt32LittleEndian(buf, strBytes.Length);
        strBytes.CopyTo(buf.AsSpan(4));
        var seq = new ReadOnlySequence<byte>(buf);
        var reader = new SequenceReader<byte>(seq);

        var result = RpcFrameCodec.TryReadString(ref reader, out var value);

        Assert.True(result);
        Assert.Equal("Hello", value);
        Assert.Equal(4 + strBytes.Length, reader.Consumed);
    }

    [Fact]
    public void TryReadString_ZeroLength_ReturnsFalse()
    {
        var buf = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(buf, 0);
        var seq = new ReadOnlySequence<byte>(buf);
        var reader = new SequenceReader<byte>(seq);

        var result = RpcFrameCodec.TryReadString(ref reader, out var value);

        Assert.False(result);
    }

    [Fact]
    public void TryReadString_ExceedsMaxLength_ReturnsFalse()
    {
        var str = "Hello";
        var strBytes = Encoding.UTF8.GetBytes(str);
        var buf = new byte[4 + strBytes.Length];
        BinaryPrimitives.WriteInt32LittleEndian(buf, strBytes.Length);
        strBytes.CopyTo(buf.AsSpan(4));
        var seq = new ReadOnlySequence<byte>(buf);
        var reader = new SequenceReader<byte>(seq);

        var result = RpcFrameCodec.TryReadString(ref reader, out var value, 3);

        Assert.False(result);
    }

    [Fact]
    public void TryReadString_InsufficientRemaining_ReturnsFalse()
    {
        var buf = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(buf, 10);
        var seq = new ReadOnlySequence<byte>(buf);
        var reader = new SequenceReader<byte>(seq);

        var result = RpcFrameCodec.TryReadString(ref reader, out var value);

        Assert.False(result);
    }

    [Fact]
    public void TryReadString_NegativeLength_ReturnsFalse()
    {
        var buf = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(buf, -1);
        var seq = new ReadOnlySequence<byte>(buf);
        var reader = new SequenceReader<byte>(seq);

        var result = RpcFrameCodec.TryReadString(ref reader, out var value);

        Assert.False(result);
    }

    [Fact]
    public void DecodeUtf8_SingleSegment()
    {
        var text = "test string";
        var bytes = Encoding.UTF8.GetBytes(text);
        var seq = new ReadOnlySequence<byte>(bytes);

        var result = RpcFrameCodec.DecodeUtf8(seq);

        Assert.Equal(text, result);
    }

    [Fact]
    public void DecodeUtf8_MultiSegment()
    {
        var text = "hello world multi segment";
        var bytes = Encoding.UTF8.GetBytes(text);
        var seg1 = new byte[bytes.Length / 2];
        var seg2 = new byte[bytes.Length - seg1.Length];
        Array.Copy(bytes, seg1, seg1.Length);
        Array.Copy(bytes, seg1.Length, seg2, 0, seg2.Length);

        var firstSegment = new TestSegment(seg1);
        var secondSegment = new TestSegment(seg2, firstSegment);
        firstSegment.SetNext(secondSegment);
        var seq = new ReadOnlySequence<byte>(firstSegment, 0, secondSegment, seg2.Length);

        var result = RpcFrameCodec.DecodeUtf8(seq);

        Assert.Equal(text, result);
    }

    [Fact]
    public void PrepareHeaders_Null_ReturnsZeroCount()
    {
        var info = RpcFrameCodec.PrepareHeaders(null);

        Assert.Equal(0, info.HeaderCount);
        Assert.Equal(0, info.HeadersBlockSize);
        Assert.Null(info.Keys);
    }

    [Fact]
    public void PrepareHeaders_Empty_ReturnsZeroCount()
    {
        var info = RpcFrameCodec.PrepareHeaders(new Dictionary<string, string>());

        Assert.Equal(0, info.HeaderCount);
        Assert.Equal(0, info.HeadersBlockSize);
    }

    [Fact]
    public void PrepareHeaders_WithHeaders_SetsSizes()
    {
        var headers = new Dictionary<string, string> { ["key1"] = "val1", ["k2"] = "value2" };
        var info = RpcFrameCodec.PrepareHeaders(headers);

        Assert.Equal(2, info.HeaderCount);
        Assert.Equal(
            4 + Encoding.UTF8.GetByteCount("key1") + 4 + Encoding.UTF8.GetByteCount("val1") +
            4 + Encoding.UTF8.GetByteCount("k2") + 4 + Encoding.UTF8.GetByteCount("value2"),
            info.HeadersBlockSize);
    }

    [Fact]
    public void WriteHeaders_ReadHeaders_Roundtrip()
    {
        var headers = new Dictionary<string, string>
        {
            ["Authorization"] = "Bearer token",
            ["x-trace-id"] = "12345"
        };
        var info = RpcFrameCodec.PrepareHeaders(headers);
        var buf = new byte[4 + info.HeadersBlockSize];
        int writtenOffset = RpcFrameCodec.WriteHeaders(buf, 0, in info);

        int readOffset = 4;
        var result = RpcFrameCodec.TryParseHeadersSpan(buf, ref readOffset, info.HeaderCount, out var parsed);

        Assert.True(result);
        Assert.Equal(2, parsed!.Count);
        Assert.Equal("Bearer token", parsed["Authorization"]);
        Assert.Equal("12345", parsed["x-trace-id"]);
    }

    [Fact]
    public void TryParseHeadersSpan_ZeroCount_ReturnsTrueWithNullHeaders()
    {
        var buf = new byte[0];
        int offset = 0;
        var result = RpcFrameCodec.TryParseHeadersSpan(buf, ref offset, 0, out var headers);

        Assert.True(result);
        Assert.Null(headers);
    }

    [Fact]
    public void TryParseHeadersSpan_NegativeCount_ReturnsFalse()
    {
        var buf = new byte[0];
        int offset = 0;
        var result = RpcFrameCodec.TryParseHeadersSpan(buf, ref offset, -1, out var headers);

        Assert.False(result);
    }

    [Fact]
    public void TryParseHeadersSequence_ZeroCount_ReturnsTrueWithNullHeaders()
    {
        var seq = new ReadOnlySequence<byte>(Array.Empty<byte>());
        var reader = new SequenceReader<byte>(seq);
        var result = RpcFrameCodec.TryParseHeadersSequence(ref reader, 0, out var headers);

        Assert.True(result);
        Assert.Null(headers);
    }

    [Fact]
    public void TryParseHeadersSequence_NegativeCount_ReturnsFalse()
    {
        var seq = new ReadOnlySequence<byte>(Array.Empty<byte>());
        var reader = new SequenceReader<byte>(seq);
        var result = RpcFrameCodec.TryParseHeadersSequence(ref reader, -1, out var headers);

        Assert.False(result);
    }

    [Fact]
    public void TryParseHeadersSequence_Roundtrip()
    {
        var headers = new Dictionary<string, string> { ["a"] = "b" };
        var info = RpcFrameCodec.PrepareHeaders(headers);
        var buf = new byte[4 + info.HeadersBlockSize];
        RpcFrameCodec.WriteHeaders(buf, 0, in info);

        var seq = new ReadOnlySequence<byte>(buf.AsMemory(4));
        var reader = new SequenceReader<byte>(seq);
        var result = RpcFrameCodec.TryParseHeadersSequence(ref reader, info.HeaderCount, out var parsed);

        Assert.True(result);
        Assert.Equal("b", parsed!["a"]);
    }

    [Fact]
    public void WriteRequestSpan_FormatVerification()
    {
        var svcBytes = Encoding.UTF8.GetBytes("Svc");
        var metBytes = Encoding.UTF8.GetBytes("Met");
        var headers = new Dictionary<string, string> { ["h"] = "v" };
        var info = RpcFrameCodec.PrepareHeaders(headers);

        int innerLen = 1 + 4 + 4 + svcBytes.Length + 4 + metBytes.Length + 4 + info.HeadersBlockSize;
        var buf = new byte[innerLen];
        int offset = RpcFrameCodec.WriteRequestSpan(buf, 10, "Svc", "Met", in info, svcBytes.Length, metBytes.Length);

        Assert.Equal((byte)RpcMessageType.Request, buf[0]);
        Assert.Equal(10u, BinaryPrimitives.ReadUInt32LittleEndian(buf.AsSpan(1, 4)));
        Assert.Equal(svcBytes.Length, BinaryPrimitives.ReadInt32LittleEndian(buf.AsSpan(5, 4)));
        Assert.True(buf.AsSpan(9, svcBytes.Length).SequenceEqual(svcBytes));
    }

    [Fact]
    public void WriteResponseSpan_SuccessFormat()
    {
        var info = RpcFrameCodec.PrepareHeaders(null);
        var buf = new byte[100];
        int offset = RpcFrameCodec.WriteResponseSpan(buf, 5, false, in info, 0);

        Assert.Equal((byte)RpcMessageType.Response, buf[0]);
        Assert.Equal(5u, BinaryPrimitives.ReadUInt32LittleEndian(buf.AsSpan(1, 4)));
        Assert.Equal(200, BinaryPrimitives.ReadInt32LittleEndian(buf.AsSpan(5, 4)));
    }

    [Fact]
    public void WriteResponseSpan_ErrorFormat()
    {
        var info = RpcFrameCodec.PrepareHeaders(null);
        var buf = new byte[100];
        int offset = RpcFrameCodec.WriteResponseSpan(buf, 5, true, in info, 0);

        Assert.Equal((byte)RpcMessageType.Error, buf[0]);
        Assert.Equal(5u, BinaryPrimitives.ReadUInt32LittleEndian(buf.AsSpan(1, 4)));
        Assert.Equal(500, BinaryPrimitives.ReadInt32LittleEndian(buf.AsSpan(5, 4)));
    }

    [Fact]
    public void WriteCancelSpan_FormatVerification()
    {
        var buf = new byte[5];
        RpcFrameCodec.WriteCancelSpan(buf, 42);

        Assert.Equal((byte)RpcMessageType.Cancel, buf[0]);
        Assert.Equal(42u, BinaryPrimitives.ReadUInt32LittleEndian(buf.AsSpan(1, 4)));
    }

    [Fact]
    public void EndToEnd_WriteRequest_ParseRequest()
    {
        var headers = new Dictionary<string, string> { ["auth"] = "token" };
        var payload = Encoding.UTF8.GetBytes("request body");
        var frame = BuildRequestFrame(100, "MyService", "MyMethod", payload, headers);

        var buffer = new ReadOnlySequence<byte>(frame);
        var activeRequests = new ConcurrentDictionary<(long, uint), CancellationTokenSource>();

        var result = RpcFrameCodec.TryParseRequest(ref buffer, 1, activeRequests,
            out var request, out var parsedPayload);

        Assert.True(result);
        Assert.Equal(100u, request.RequestId);
        Assert.Equal("MyService", request.ServiceName);
        Assert.Equal("MyMethod", request.MethodName);
        Assert.Equal("token", request.Headers!["auth"]);
        Assert.True(parsedPayload.ToArray().SequenceEqual(payload));
        Assert.Equal(0, buffer.Length);
    }

    [Fact]
    public void EndToEnd_WriteResponse_ParseResponse()
    {
        var headers = new Dictionary<string, string> { ["x-correlation"] = "xyz" };
        var payload = Encoding.UTF8.GetBytes("response body");
        var frame = BuildResponseFrame(200, false, payload, headers);

        var buffer = new ReadOnlySequence<byte>(frame);

        var result = RpcFrameCodec.TryParseResponse(ref buffer, out var response);

        Assert.True(result);
        Assert.Equal(200u, response.RequestId);
        Assert.Equal((byte)RpcMessageType.Response, response.Type);
        Assert.Equal(200, response.StatusCode);
        Assert.Equal("xyz", response.Headers!["x-correlation"]);
        Assert.True(response.Payload.ToArray().SequenceEqual(payload));
        Assert.Equal(0, buffer.Length);
    }

    [Fact]
    public void EndToEnd_WriteCancel_ParseCancel()
    {
        var frame = BuildCancelFrame(77);
        var buffer = new ReadOnlySequence<byte>(frame);
        var cts = new CancellationTokenSource();
        var activeRequests = new ConcurrentDictionary<(long, uint), CancellationTokenSource>();
        activeRequests[(1, 77)] = cts;

        var result = RpcFrameCodec.TryParseRequest(ref buffer, 1, activeRequests,
            out var request, out var payload);

        Assert.False(result);
        Assert.True(cts.IsCancellationRequested);
        Assert.Equal(0, buffer.Length);
    }

    [Fact]
    public void TryParseRequest_NoHeaders_Success()
    {
        var payload = Encoding.UTF8.GetBytes("data");
        var frame = BuildRequestFrame(1, "Svc", "Met", payload, null);
        var buffer = new ReadOnlySequence<byte>(frame);
        var activeRequests = new ConcurrentDictionary<(long, uint), CancellationTokenSource>();

        var result = RpcFrameCodec.TryParseRequest(ref buffer, 1, activeRequests,
            out var request, out var parsedPayload);

        Assert.True(result);
        Assert.Null(request.Headers);
    }

    [Fact]
    public void TryParseResponse_NoHeaders_Success()
    {
        var payload = Encoding.UTF8.GetBytes("ok");
        var frame = BuildResponseFrame(1, false, payload, null);
        var buffer = new ReadOnlySequence<byte>(frame);

        var result = RpcFrameCodec.TryParseResponse(ref buffer, out var response);

        Assert.True(result);
        Assert.Null(response.Headers);
    }
}

internal class TestSegment : ReadOnlySequenceSegment<byte>
{
    public TestSegment(byte[] data, TestSegment? previous = null)
    {
        Memory = data;
        if (previous != null)
        {
            RunningIndex = previous.RunningIndex + previous.Memory.Length;
        }
    }

    public void SetNext(TestSegment next)
    {
        Next = next;
        next.RunningIndex = RunningIndex + Memory.Length;
    }
}
