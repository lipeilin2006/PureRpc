using System.Buffers;
using System.Buffers.Binary;
using System.Text;

namespace PureRpc.Abstractions;

public static class RpcFrameCodec
{
    public static bool TryReadString(ref SequenceReader<byte> reader, out string result, int maxLength = int.MaxValue)
    {
        result = string.Empty;
        if (!reader.TryReadLittleEndian(out int len)) return false;
        if (len <= 0 || len > maxLength) return false;
        if (reader.Remaining < len) return false;

        if (reader.UnreadSequence.IsSingleSegment)
        {
            result = Encoding.UTF8.GetString(reader.UnreadSpan.Slice(0, len));
        }
        else
        {
            result = Encoding.UTF8.GetString(reader.UnreadSequence.Slice(0, len));
        }
        reader.Advance(len);
        return true;
    }

    public static string DecodeUtf8(ReadOnlySequence<byte> payload)
    {
        if (payload.IsSingleSegment)
        {
            return Encoding.UTF8.GetString(payload.FirstSpan);
        }

        return Encoding.UTF8.GetString(payload.ToArray());
    }

    public struct HeaderWriteInfo
    {
        public string[]? Keys;
        public string[]? Values;
        public int[]? KeySizes;
        public int[]? ValSizes;
        public int HeaderCount;
        public int HeadersBlockSize;
    }

    public static HeaderWriteInfo PrepareHeaders(IReadOnlyDictionary<string, string>? headers)
    {
        var info = new HeaderWriteInfo();
        int headerCount = headers?.Count ?? 0;
        info.HeaderCount = headerCount;
        if (headerCount <= 0) return info;

        info.Keys = new string[headerCount];
        info.Values = new string[headerCount];
        info.KeySizes = new int[headerCount];
        info.ValSizes = new int[headerCount];
        int i = 0;
        foreach (var kv in headers!)
        {
            info.Keys[i] = kv.Key;
            info.Values[i] = kv.Value;
            info.KeySizes[i] = Encoding.UTF8.GetByteCount(kv.Key);
            info.ValSizes[i] = Encoding.UTF8.GetByteCount(kv.Value);
            info.HeadersBlockSize += 4 + info.KeySizes[i] + 4 + info.ValSizes[i];
            i++;
        }
        return info;
    }

    public static int WriteHeaders(Span<byte> span, int offset, in HeaderWriteInfo info)
    {
        BinaryPrimitives.WriteInt32LittleEndian(span.Slice(offset, 4), info.HeaderCount);
        offset += 4;
        for (int i = 0; i < info.HeaderCount; i++)
        {
            BinaryPrimitives.WriteInt32LittleEndian(span.Slice(offset, 4), info.KeySizes![i]);
            offset += 4;
            Encoding.UTF8.GetBytes(info.Keys![i], span.Slice(offset, info.KeySizes[i]));
            offset += info.KeySizes[i];
            BinaryPrimitives.WriteInt32LittleEndian(span.Slice(offset, 4), info.ValSizes![i]);
            offset += 4;
            Encoding.UTF8.GetBytes(info.Values![i], span.Slice(offset, info.ValSizes[i]));
            offset += info.ValSizes[i];
        }
        return offset;
    }

    public static bool TryParseHeadersSpan(ReadOnlySpan<byte> span, ref int offset, int headerCount, out Dictionary<string, string>? headers)
    {
        headers = null;
        if (headerCount < 0 || headerCount > RpcProtocolConstants.MaxHeaderCount)
            return false;
        if (headerCount == 0)
            return true;

        var dict = new Dictionary<string, string>(headerCount);
        for (int i = 0; i < headerCount; i++)
        {
            if (offset + 4 > span.Length) return false;
            int keyLen = BinaryPrimitives.ReadInt32LittleEndian(span.Slice(offset, 4));
            if (keyLen <= 0 || keyLen > RpcProtocolConstants.MaxHeaderKeyLength) return false;
            offset += 4;
            if (offset + keyLen > span.Length) return false;
            string key = Encoding.UTF8.GetString(span.Slice(offset, keyLen));
            offset += keyLen;
            if (offset + 4 > span.Length) return false;
            int valLen = BinaryPrimitives.ReadInt32LittleEndian(span.Slice(offset, 4));
            if (valLen <= 0 || valLen > RpcProtocolConstants.MaxHeaderValueLength) return false;
            offset += 4;
            if (offset + valLen > span.Length) return false;
            string val = Encoding.UTF8.GetString(span.Slice(offset, valLen));
            offset += valLen;
            dict[key] = val;
        }
        headers = dict;
        return true;
    }

    public static bool TryParseHeadersSequence(ref SequenceReader<byte> reader, int headerCount, out Dictionary<string, string>? headers)
    {
        headers = null;
        if (headerCount < 0 || headerCount > RpcProtocolConstants.MaxHeaderCount)
            return false;
        if (headerCount == 0)
            return true;

        var dict = new Dictionary<string, string>(headerCount);
        for (int i = 0; i < headerCount; i++)
        {
            if (!TryReadString(ref reader, out var key, RpcProtocolConstants.MaxHeaderKeyLength)) return false;
            if (!TryReadString(ref reader, out var val, RpcProtocolConstants.MaxHeaderValueLength)) return false;
            dict[key] = val;
        }
        headers = dict;
        return true;
    }

    public static int WriteRequestSpan(Span<byte> span, uint requestId, string serviceName, string methodName, in HeaderWriteInfo info, int svcByteCount, int metByteCount)
    {
        span[0] = (byte)RpcMessageType.Request;
        BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(1, 4), requestId);
        BinaryPrimitives.WriteInt32LittleEndian(span.Slice(5, 4), svcByteCount);
        int offset = 9;
        Encoding.UTF8.GetBytes(serviceName, span.Slice(offset, svcByteCount));
        offset += svcByteCount;
        BinaryPrimitives.WriteInt32LittleEndian(span.Slice(offset, 4), metByteCount);
        offset += 4;
        Encoding.UTF8.GetBytes(methodName, span.Slice(offset, metByteCount));
        offset += metByteCount;
        offset = WriteHeaders(span, offset, in info);
        return offset;
    }

    public static int WriteResponseSpan(Span<byte> span, uint requestId, bool isAborted, in HeaderWriteInfo info, int dataLength)
    {
        span[0] = (byte)(isAborted ? RpcMessageType.Error : RpcMessageType.Response);
        BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(1, 4), requestId);
        BinaryPrimitives.WriteInt32LittleEndian(span.Slice(5, 4), isAborted ? 500 : 200);
        int offset = 9;
        offset = WriteHeaders(span, offset, in info);
        return offset;
    }

    public static void WriteCancelSpan(Span<byte> span, uint requestId)
    {
        span[0] = (byte)RpcMessageType.Cancel;
        BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(1, 4), requestId);
    }
}
