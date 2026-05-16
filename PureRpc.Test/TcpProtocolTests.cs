using System.Buffers;
using System.Buffers.Binary;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using PureRpc.Abstractions;
using PureRpc.Transport.Tcp;

namespace PureRpc.Test;

public sealed class TcpClientOptionsTests
{
    [Fact]
    public void DefaultValues_AreCorrect()
    {
        var opts = new TcpClientOptions();
        Assert.Equal("127.0.0.1", opts.Host);
        Assert.Equal(5000, opts.Port);
        Assert.Equal(TimeSpan.FromSeconds(15), opts.ConnectTimeout);
        Assert.True(opts.NoDelay);
        Assert.Equal(64 * 1024, opts.SendBufferSize);
        Assert.Equal(64 * 1024, opts.ReceiveBufferSize);
        Assert.Null(opts.TargetHost);
        Assert.Equal(new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, 5000), opts.RemoteEndPoint);
    }

    [Fact]
    public void CustomValues_AreApplied()
    {
        var opts = new TcpClientOptions
        {
            Host = "192.168.1.1",
            Port = 9999,
            ConnectTimeout = TimeSpan.FromSeconds(10),
            NoDelay = false,
            SendBufferSize = 16384,
            ReceiveBufferSize = 16384,
            TargetHost = "example.com"
        };

        Assert.Equal("192.168.1.1", opts.Host);
        Assert.Equal(9999, opts.Port);
        Assert.Equal(TimeSpan.FromSeconds(10), opts.ConnectTimeout);
        Assert.False(opts.NoDelay);
        Assert.Equal(16384, opts.SendBufferSize);
        Assert.Equal(16384, opts.ReceiveBufferSize);
        Assert.Equal("example.com", opts.TargetHost);
    }

    [Fact]
    public void RemoteEndPoint_ReflectsHostAndPort()
    {
        var opts = new TcpClientOptions { Host = "10.0.0.1", Port = 8080 };
        var ep = (System.Net.IPEndPoint)opts.RemoteEndPoint;
        Assert.Equal(System.Net.IPAddress.Parse("10.0.0.1"), ep.Address);
        Assert.Equal(8080, ep.Port);
    }
}

public sealed class TcpServerOptionsTests
{
    [Fact]
    public void DefaultValues_AreCorrect()
    {
        var opts = new TcpServerOptions();
        Assert.Equal(1000, opts.Backlog);
        Assert.Equal(10000, opts.MaxConnections);
        Assert.True(opts.ReuseAddress);
        Assert.Null(opts.ServerCertificate);
        Assert.NotNull(opts.EndPoint);
    }

    [Fact]
    public void CustomEndPoint_IsApplied()
    {
        var ep = new System.Net.IPEndPoint(System.Net.IPAddress.Any, 9999);
        var opts = new TcpServerOptions { EndPoint = ep };
        Assert.Equal(9999, ((System.Net.IPEndPoint)opts.EndPoint).Port);
    }
}

public sealed class TcpFrameFormatTests
{
    [Fact]
    public void RequestFrame_Roundtrips()
    {
        var requestId = 42u;
        var serviceName = "TestService";
        var methodName = "TestMethod";
        var payloadData = "hello"u8.ToArray();

        var frame = BuildRequestFrame(requestId, serviceName, methodName, payloadData, null);

        Assert.True(TryParseRequest(frame, out var parsedId, out var parsedSvc, out var parsedMet, out var parsedHeaders, out var parsedPayload));
        Assert.Equal(requestId, parsedId);
        Assert.Equal(serviceName, parsedSvc);
        Assert.Equal(methodName, parsedMet);
        Assert.Null(parsedHeaders);
        Assert.Equal(payloadData, parsedPayload.ToArray());
    }

    [Fact]
    public void RequestFrame_WithHeaders_Roundtrips()
    {
        var headers = new Dictionary<string, string> { { "auth", "token123" }, { "trace", "abc" } };
        var frame = BuildRequestFrame(1u, "Svc", "Method", Array.Empty<byte>(), headers);

        Assert.True(TryParseRequest(frame, out _, out _, out _, out var parsedHeaders, out _));
        Assert.NotNull(parsedHeaders);
        Assert.Equal("token123", parsedHeaders["auth"]);
        Assert.Equal("abc", parsedHeaders["trace"]);
    }

    [Fact]
    public void RequestFrame_EmptyPayload_Works()
    {
        var frame = BuildRequestFrame(7u, "Svc", "Mtd", Array.Empty<byte>(), null);
        Assert.True(TryParseRequest(frame, out var id, out var svc, out var mtd, out _, out var payload));
        Assert.Equal(7u, id);
        Assert.Equal("Svc", svc);
        Assert.Equal("Mtd", mtd);
        Assert.Empty(payload.ToArray());
    }

    [Fact]
    public void ResponseFrame_Roundtrips()
    {
        var requestId = 100u;
        var payloadData = "result"u8.ToArray();

        var frame = BuildResponseFrame(requestId, 200, payloadData, null);

        Assert.True(TryParseResponse(frame, out var parsedId, out var parsedType, out var parsedCode, out var parsedHeaders, out var parsedPayload));
        Assert.Equal(requestId, parsedId);
        Assert.Equal(200, parsedCode);
        Assert.Equal(payloadData, parsedPayload.ToArray());
    }

    [Fact]
    public void ResponseFrame_WithHeaders_Roundtrips()
    {
        var headers = new Dictionary<string, string> { { "server-time", "123456" } };
        var frame = BuildResponseFrame(1u, 200, Array.Empty<byte>(), headers);

        Assert.True(TryParseResponse(frame, out _, out _, out _, out var parsedHeaders, out _));
        Assert.NotNull(parsedHeaders);
        Assert.Equal("123456", parsedHeaders["server-time"]);
    }

    [Fact]
    public void CancellationFrame_Parses()
    {
        var frame = BuildCancelFrame(55u);

        var buffer = new ReadOnlySequence<byte>(frame);
        var parsed = TryParseRequest(ref buffer, 0, out var header, out _);
        Assert.False(parsed, "Cancel frame should return false (skip)");
    }

    [Fact]
    public void ErrorResponseFrame_Parses()
    {
        var errorMsg = "Internal server error";
        var payloadBytes = Encoding.UTF8.GetBytes(errorMsg);
        var frame = BuildResponseFrame(1u, 500, payloadBytes, null);

        Assert.True(TryParseResponse(frame, out _, out var type, out var code, out _, out var payload));
        Assert.Equal(500, code);
        Assert.Equal(errorMsg, Encoding.UTF8.GetString(payload));
    }

    [Fact]
    public void OversizedServiceName_Rejected()
    {
        var longName = new string('X', 257);
        var frame = BuildRequestFrame(1u, longName, "Mtd", Array.Empty<byte>(), null);
        Assert.False(TryParseRequest(frame, out _, out _, out _, out _, out _));
    }

    [Fact]
    public void OversizedMethodName_Rejected()
    {
        var longName = new string('Y', 257);
        var frame = BuildRequestFrame(1u, "Svc", longName, Array.Empty<byte>(), null);
        Assert.False(TryParseRequest(frame, out _, out _, out _, out _, out _));
    }

    [Fact]
    public void NegativeHeaderCount_Rejected()
    {
        var frame = BuildRequestFrame(1u, "Svc", "Mtd", Array.Empty<byte>(), null);
        // Corrupt the header count to be negative
        frame[^1] = 0xFF;
        Assert.False(TryParseRequest(frame, out _, out _, out _, out _, out _));
    }

    private static byte[] BuildRequestFrame(uint requestId, string serviceName, string methodName, byte[] data, IDictionary<string, string>? headers)
    {
        int svcBytes = Encoding.UTF8.GetByteCount(serviceName);
        int metBytes = Encoding.UTF8.GetByteCount(methodName);
        int hc = headers?.Count ?? 0;
        int hbs = 0;
        string[]? ks = null, vs = null;
        int[]? kSizes = null, vSizes = null;
        if (hc > 0)
        {
            ks = new string[hc]; vs = new string[hc]; kSizes = new int[hc]; vSizes = new int[hc];
            int i = 0;
            foreach (var kv in headers!)
            {
                ks[i] = kv.Key; vs[i] = kv.Value;
                kSizes[i] = Encoding.UTF8.GetByteCount(kv.Key);
                vSizes[i] = Encoding.UTF8.GetByteCount(kv.Value);
                hbs += 4 + kSizes[i] + 4 + vSizes[i]; i++;
            }
        }
        int bodyLen = 1 + 4 + 4 + svcBytes + 4 + metBytes + 4 + hbs + data.Length;
        var buf = new byte[bodyLen + 4];
        BinaryPrimitives.WriteInt32LittleEndian(buf.AsSpan(0, 4), bodyLen);
        buf[4] = 1; // Request type
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(5, 4), requestId);
        int offset = 9;
        BinaryPrimitives.WriteInt32LittleEndian(buf.AsSpan(offset, 4), svcBytes); offset += 4;
        Encoding.UTF8.GetBytes(serviceName, buf.AsSpan(offset, svcBytes)); offset += svcBytes;
        BinaryPrimitives.WriteInt32LittleEndian(buf.AsSpan(offset, 4), metBytes); offset += 4;
        Encoding.UTF8.GetBytes(methodName, buf.AsSpan(offset, metBytes)); offset += metBytes;
        BinaryPrimitives.WriteInt32LittleEndian(buf.AsSpan(offset, 4), hc); offset += 4;
        if (hc > 0)
        {
            for (int i = 0; i < hc; i++)
            {
                BinaryPrimitives.WriteInt32LittleEndian(buf.AsSpan(offset, 4), kSizes![i]); offset += 4;
                Encoding.UTF8.GetBytes(ks![i], buf.AsSpan(offset, kSizes[i])); offset += kSizes[i];
                BinaryPrimitives.WriteInt32LittleEndian(buf.AsSpan(offset, 4), vSizes![i]); offset += 4;
                Encoding.UTF8.GetBytes(vs![i], buf.AsSpan(offset, vSizes[i])); offset += vSizes[i];
            }
        }
        if (data.Length > 0) data.CopyTo(buf.AsSpan(offset));
        return buf;
    }

    private static byte[] BuildResponseFrame(uint requestId, int statusCode, byte[] data, IDictionary<string, string>? headers)
    {
        int hc = headers?.Count ?? 0;
        int hbs = 0;
        string[]? ks = null, vs = null;
        int[]? kSizes = null, vSizes = null;
        if (hc > 0)
        {
            ks = new string[hc]; vs = new string[hc]; kSizes = new int[hc]; vSizes = new int[hc];
            int i = 0;
            foreach (var kv in headers!)
            {
                ks[i] = kv.Key; vs[i] = kv.Value;
                kSizes[i] = Encoding.UTF8.GetByteCount(kv.Key);
                vSizes[i] = Encoding.UTF8.GetByteCount(kv.Value);
                hbs += 4 + kSizes[i] + 4 + vSizes[i]; i++;
            }
        }
        int bodyLen = 1 + 4 + 4 + 4 + hbs + data.Length;
        var buf = new byte[bodyLen + 4];
        BinaryPrimitives.WriteInt32LittleEndian(buf.AsSpan(0, 4), bodyLen);
        buf[4] = (byte)(statusCode == 200 ? 2 : 3);
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(5, 4), requestId);
        BinaryPrimitives.WriteInt32LittleEndian(buf.AsSpan(9, 4), statusCode);
        BinaryPrimitives.WriteInt32LittleEndian(buf.AsSpan(13, 4), hc);
        int offset = 17;
        if (hc > 0)
        {
            for (int i = 0; i < hc; i++)
            {
                BinaryPrimitives.WriteInt32LittleEndian(buf.AsSpan(offset, 4), kSizes![i]); offset += 4;
                Encoding.UTF8.GetBytes(ks![i], buf.AsSpan(offset, kSizes[i])); offset += kSizes[i];
                BinaryPrimitives.WriteInt32LittleEndian(buf.AsSpan(offset, 4), vSizes![i]); offset += 4;
                Encoding.UTF8.GetBytes(vs![i], buf.AsSpan(offset, vSizes[i])); offset += vSizes[i];
            }
        }
        if (data.Length > 0) data.CopyTo(buf.AsSpan(offset));
        return buf;
    }

    private static byte[] BuildCancelFrame(uint requestId)
    {
        var buf = new byte[13];
        BinaryPrimitives.WriteInt32LittleEndian(buf.AsSpan(0, 4), 5);
        buf[4] = 8; // Cancel type
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(5, 4), requestId);
        return buf;
    }

    private static bool TryParseRequest(byte[] frame, out uint requestId, out string serviceName, out string methodName, out IReadOnlyDictionary<string, string>? headers, out ReadOnlySequence<byte> payload)
    {
        requestId = 0; serviceName = ""; methodName = ""; headers = null; payload = default;
        var buffer = new ReadOnlySequence<byte>(frame);
        return TryParseRequest(ref buffer, 0, out var header, out payload) &&
               TryParseRequestExtract(header, out requestId, out serviceName, out methodName, out headers);
    }

    private static bool TryParseRequestExtract((uint RequestId, string ServiceName, string MethodName, IReadOnlyDictionary<string, string>? Headers) header,
        out uint requestId, out string serviceName, out string methodName, out IReadOnlyDictionary<string, string>? headers)
    {
        requestId = header.RequestId;
        serviceName = header.ServiceName;
        methodName = header.MethodName;
        headers = header.Headers;
        return true;
    }

    

    private static bool TryParseRequest(ref ReadOnlySequence<byte> buffer, long connectionId,
        out (uint RequestId, string ServiceName, string MethodName, IReadOnlyDictionary<string, string>? Headers) header,
        out ReadOnlySequence<byte> payload)
    {
        header = default; payload = default;
        if (buffer.Length < 9) return false;

        Span<byte> headSpan = stackalloc byte[9];
        buffer.Slice(0, 9).CopyTo(headSpan);
        int totalLen = BinaryPrimitives.ReadInt32LittleEndian(headSpan[..4]);
        const int MaxFrameSize = 64 * 1024 * 1024;
        if (totalLen < 5 || totalLen > MaxFrameSize) return false;
        if (buffer.Length < totalLen + 4U) return false;

        byte type = headSpan[4];
        uint reqId = BinaryPrimitives.ReadUInt32LittleEndian(headSpan.Slice(5, 4));

        if (type == 8) { buffer = buffer.Slice(totalLen + 4); return false; }
        if (type != 1) { buffer = buffer.Slice(totalLen + 4); return false; }
        if (totalLen < 13) return false;

        var reader = new SequenceReader<byte>(buffer.Slice(9, totalLen - 5));
        const int MaxServiceNameLength = 256;
        const int MaxMethodNameLength = 256;
        const int MaxHeaderCount = 64;
        const int MaxHeaderKeyLength = 256;
        const int MaxHeaderValueLength = 4096;

        if (!TryReadString(ref reader, out var svc, MaxServiceNameLength)) return false;
        if (!TryReadString(ref reader, out var met, MaxMethodNameLength)) return false;
        if (!reader.TryReadLittleEndian(out int headerCount)) return false;
        if (headerCount < 0 || headerCount > MaxHeaderCount) return false;

        IReadOnlyDictionary<string, string>? hdrs = null;
        if (headerCount > 0)
        {
            var dict = new Dictionary<string, string>(headerCount);
            for (int i = 0; i < headerCount; i++)
            {
                if (!TryReadString(ref reader, out var key, MaxHeaderKeyLength)) return false;
                if (!TryReadString(ref reader, out var val, MaxHeaderValueLength)) return false;
                dict[key] = val;
            }
            hdrs = dict;
        }

        header = (reqId, svc, met, hdrs);
        payload = reader.UnreadSequence;
        buffer = buffer.Slice(totalLen + 4);
        return true;
    }

    private static bool TryParseResponse(byte[] frame, out uint requestId, out int type, out int statusCode, out IReadOnlyDictionary<string, string>? headers, out ReadOnlySequence<byte> payload)
    {
        requestId = 0; type = 0; statusCode = 0; headers = null; payload = default;
        var buffer = new ReadOnlySequence<byte>(frame);
        return TryParseResponse(ref buffer, out requestId, out type, out statusCode, out headers, out payload);
    }

    private static bool TryParseResponse(ref ReadOnlySequence<byte> buffer, out uint requestId, out int type, out int statusCode, out IReadOnlyDictionary<string, string>? headers, out ReadOnlySequence<byte> payload)
    {
        requestId = 0; type = 0; statusCode = 0; headers = null; payload = default;
        if (buffer.Length < 17) return false;

        Span<byte> headSpan = stackalloc byte[17];
        buffer.Slice(0, 17).CopyTo(headSpan);

        int totalLen = BinaryPrimitives.ReadInt32LittleEndian(headSpan[..4]);
        const int MaxFrameSize = 64 * 1024 * 1024;
        if (buffer.Length < totalLen + 4) return false;

        type = headSpan[4];
        requestId = BinaryPrimitives.ReadUInt32LittleEndian(headSpan.Slice(5, 4));
        statusCode = BinaryPrimitives.ReadInt32LittleEndian(headSpan.Slice(9, 4));
        int headerCount = BinaryPrimitives.ReadInt32LittleEndian(headSpan.Slice(13, 4));
        const int MaxHeaderCount = 64;
        const int MaxHeaderKeyLength = 256;
        const int MaxHeaderValueLength = 4096;
        if (headerCount < 0 || headerCount > MaxHeaderCount) return false;

        var remaining = buffer.Slice(17);
        if (headerCount > 0)
        {
            var dict = new Dictionary<string, string>(headerCount);
            var reader = new SequenceReader<byte>(remaining);
            for (int i = 0; i < headerCount; i++)
            {
                if (!TryReadString(ref reader, out var key, MaxHeaderKeyLength)) return false;
                if (!TryReadString(ref reader, out var val, MaxHeaderValueLength)) return false;
                dict[key] = val;
            }
            headers = dict;
            payload = remaining.Slice(reader.Consumed);
        }
        else
        {
            payload = remaining;
        }

        buffer = buffer.Slice(totalLen + 4);
        return true;
    }

    private static bool TryReadString(ref SequenceReader<byte> reader, out string result, int maxLength = int.MaxValue)
    {
        result = string.Empty;
        if (!reader.TryReadLittleEndian(out int len)) return false;
        if (len <= 0 || len > maxLength) return false;
        if (reader.Remaining < len) return false;

        if (reader.UnreadSequence.IsSingleSegment)
            result = Encoding.UTF8.GetString(reader.UnreadSpan.Slice(0, len));
        else
            result = Encoding.UTF8.GetString(reader.UnreadSequence.Slice(0, len));
        reader.Advance(len);
        return true;
    }
}