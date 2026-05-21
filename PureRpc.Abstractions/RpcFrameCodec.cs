using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Text;
using System.Threading;

namespace PureRpc.Abstractions;

/// <summary>
/// RPC 协议帧编解码器 / RPC protocol frame codec.
/// 提供请求帧、响应帧和取消帧的序列化与反序列化工具方法 / 
/// Provides utility methods for serialization and deserialization of request, response, and cancel frames.
/// </summary>
public static class RpcFrameCodec
{
    /// <summary>
    /// 解析后的请求数据结构 / Parsed request data structure.
    /// 包含请求标识、服务名、方法名和头部信息 / Contains request ID, service name, method name, and headers.
    /// </summary>
    public struct ParsedRequest
    {
        /// <summary>
        /// 请求唯一标识符 / Unique request identifier.
        /// </summary>
        public uint RequestId;

        /// <summary>
        /// 目标服务名称 / Target service name.
        /// </summary>
        public string ServiceName;

        /// <summary>
        /// 目标方法名称 / Target method name.
        /// </summary>
        public string MethodName;

        /// <summary>
        /// 请求头部字典（可为 null） / Request headers dictionary (may be null).
        /// </summary>
        public IReadOnlyDictionary<string, string>? Headers;
    }

    /// <summary>
    /// 解析后的响应数据结构 / Parsed response data structure.
    /// 包含请求标识、消息类型、状态码、头部和负载 / Contains request ID, message type, status code, headers, and payload.
    /// </summary>
    public struct ParsedResponse
    {
        /// <summary>
        /// 请求唯一标识符 / Unique request identifier.
        /// </summary>
        public uint RequestId;

        /// <summary>
        /// 消息类型字节值 / Message type byte value.
        /// </summary>
        public byte Type;

        /// <summary>
        /// 响应状态码（200 表示成功，500 表示错误） / Response status code (200 for success, 500 for error).
        /// </summary>
        public int StatusCode;

        /// <summary>
        /// 响应头部字典（可为 null） / Response headers dictionary (may be null).
        /// </summary>
        public IReadOnlyDictionary<string, string>? Headers;

        /// <summary>
        /// 响应负载原始字节序列 / Response payload raw byte sequence.
        /// </summary>
        public ReadOnlySequence<byte> Payload;
    }

    /// <summary>
    /// 尝试从缓冲区解析 RPC 请求帧 / Attempts to parse an RPC request frame from the buffer.
    /// 如果遇到取消帧，将自动取消对应的 CancellationTokenSource / 
    /// If a cancel frame is encountered, the corresponding CancellationTokenSource is automatically cancelled.
    /// </summary>
    /// <param name="buffer">可变的字节序列缓冲区，解析成功后会被推进 / Mutable byte sequence buffer; advanced after successful parse.</param>
    /// <param name="connectionId">连接唯一标识符 / Unique connection identifier.</param>
    /// <param name="activeRequests">活跃请求的并发字典 / Concurrent dictionary of active requests.</param>
    /// <param name="request">解析后的请求数据 / The parsed request data.</param>
    /// <param name="payload">请求负载的字节序列 / The request payload byte sequence.</param>
    /// <returns>如果成功解析请求帧返回 true；遇到取消帧或数据不足返回 false / True if a request frame was successfully parsed; false for cancel frames or insufficient data.</returns>
    public static bool TryParseRequest(ref ReadOnlySequence<byte> buffer, long connectionId,
        ConcurrentDictionary<(long, uint), CancellationTokenSource> activeRequests,
        out ParsedRequest request, out ReadOnlySequence<byte> payload)
    {
        request = default;
        payload = default;
        if (buffer.Length < 9) return false;

        Span<byte> headSpan = stackalloc byte[9];
        buffer.Slice(0, 9).CopyTo(headSpan);
        int totalLen = BinaryPrimitives.ReadInt32LittleEndian(headSpan[..4]);

        if (totalLen < 5 || totalLen > RpcProtocolConstants.MaxFrameSize) return false;
        if (buffer.Length < totalLen + 4U) return false;

        byte type = headSpan[4];
        uint reqId = BinaryPrimitives.ReadUInt32LittleEndian(headSpan.Slice(5, 4));

        if (type == (byte)RpcMessageType.Cancel)
        {
            var key = (connectionId, reqId);
            if (activeRequests.TryRemove(key, out var cts))
            {
                cts.Cancel();
                cts.Dispose();
            }
            buffer = buffer.Slice(totalLen + 4);
            return false;
        }

        if (type != (byte)RpcMessageType.Request)
        {
            buffer = buffer.Slice(totalLen + 4);
            return false;
        }

        if (totalLen < 13) return false;

        var reader = new SequenceReader<byte>(buffer.Slice(9, totalLen - 5));

        if (!TryReadString(ref reader, out var svc, RpcProtocolConstants.MaxServiceNameLength)) return false;
        if (!TryReadString(ref reader, out var met, RpcProtocolConstants.MaxMethodNameLength)) return false;

        if (!reader.TryReadLittleEndian(out int headerCount)) return false;
        if (!TryParseHeadersSequence(ref reader, headerCount, out var headers)) return false;

        request = new ParsedRequest { RequestId = reqId, ServiceName = svc, MethodName = met, Headers = headers };
        payload = reader.UnreadSequence;
        buffer = buffer.Slice(totalLen + 4);
        return true;
    }

    /// <summary>
    /// 尝试从缓冲区解析 RPC 响应帧 / Attempts to parse an RPC response frame from the buffer.
    /// </summary>
    /// <param name="buffer">可变的字节序列缓冲区，解析成功后会被推进 / Mutable byte sequence buffer; advanced after successful parse.</param>
    /// <param name="response">解析后的响应数据 / The parsed response data.</param>
    /// <returns>如果成功解析响应帧返回 true；数据不足返回 false / True if a response frame was successfully parsed; false for insufficient data.</returns>
    public static bool TryParseResponse(ref ReadOnlySequence<byte> buffer, out ParsedResponse response)
    {
        response = default;
        if (buffer.Length < 17) return false;

        Span<byte> headSpan = stackalloc byte[17];
        buffer.Slice(0, 17).CopyTo(headSpan);

        int totalLen = BinaryPrimitives.ReadInt32LittleEndian(headSpan[..4]);
        if (totalLen < 13 || buffer.Length < totalLen + 4) return false;

        byte type = headSpan[4];
        uint requestId = BinaryPrimitives.ReadUInt32LittleEndian(headSpan.Slice(5, 4));
        int statusCode = BinaryPrimitives.ReadInt32LittleEndian(headSpan.Slice(9, 4));
        int headerCount = BinaryPrimitives.ReadInt32LittleEndian(headSpan.Slice(13, 4));
        if (headerCount < 0 || headerCount > RpcProtocolConstants.MaxHeaderCount) return false;

        var remaining = buffer.Slice(17, totalLen - 13);
        IReadOnlyDictionary<string, string>? headers = null;
        ReadOnlySequence<byte> payload;

        if (headerCount > 0)
        {
            var seqReader = new SequenceReader<byte>(remaining);
            if (!TryParseHeadersSequence(ref seqReader, headerCount, out var dict)) return false;
            headers = dict;
            payload = remaining.Slice(seqReader.Consumed);
        }
        else
        {
            payload = remaining;
        }

        response = new ParsedResponse
        {
            RequestId = requestId,
            Type = type,
            StatusCode = statusCode,
            Headers = headers,
            Payload = payload
        };
        buffer = buffer.Slice(totalLen + 4);
        return true;
    }

    /// <summary>
    /// 从序列读取器中尝试读取 UTF-8 编码的字符串 / Attempts to read a length-prefixed UTF-8 string from the sequence reader.
    /// </summary>
    /// <param name="reader">字节序列读取器 / Byte sequence reader.</param>
    /// <param name="result">读取到的字符串 / The read string.</param>
    /// <param name="maxLength">字符串最大字节长度限制 / Maximum byte length limit for the string.</param>
    /// <returns>读取成功返回 true；数据不足或长度无效返回 false / True if the string was read successfully; false if insufficient data or invalid length.</returns>
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

    /// <summary>
    /// 将 UTF-8 编码的字节序列解码为字符串 / Decodes a UTF-8 encoded byte sequence to a string.
    /// </summary>
    /// <param name="payload">要解码的字节序列 / The byte sequence to decode.</param>
    /// <returns>解码后的字符串 / The decoded string.</returns>
    public static string DecodeUtf8(ReadOnlySequence<byte> payload)
    {
        if (payload.IsSingleSegment)
        {
            return Encoding.UTF8.GetString(payload.FirstSpan);
        }

        return Encoding.UTF8.GetString(payload.ToArray());
    }

    /// <summary>
    /// 头部写入预计算信息 / Header write pre-computation info.
    /// 用于在写入帧之前预计算头部键值大小，避免多次遍历 / 
    /// Used to pre-compute header key/value sizes before writing the frame, avoiding multiple traversals.
    /// </summary>
    public struct HeaderWriteInfo
    {
        /// <summary>
        /// 头部键数组 / Header keys array.
        /// </summary>
        public string[]? Keys;

        /// <summary>
        /// 头部值数组 / Header values array.
        /// </summary>
        public string[]? Values;

        /// <summary>
        /// 每个键的字节大小数组 / Byte size array for each key.
        /// </summary>
        public int[]? KeySizes;

        /// <summary>
        /// 每个值的字节大小数组 / Byte size array for each value.
        /// </summary>
        public int[]? ValSizes;

        /// <summary>
        /// 头部数量 / Number of headers.
        /// </summary>
        public int HeaderCount;

        /// <summary>
        /// 头部块的总字节大小 / Total byte size of the header block.
        /// </summary>
        public int HeadersBlockSize;
    }

    /// <summary>
    /// 预计算头部写入信息 / Pre-computes header write information.
    /// 计算键值的字节大小，为帧缓冲区分配提供准确的偏移量 / 
    /// Computes byte sizes of keys and values, providing exact offsets for frame buffer allocation.
    /// </summary>
    /// <param name="headers">头部字典（可为 null） / Headers dictionary (may be null).</param>
    /// <returns>预计算的头部写入信息 / Pre-computed header write info.</returns>
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

    /// <summary>
    /// 将头部信息写入 Span 缓冲区 / Writes header information to a Span buffer.
    /// </summary>
    /// <param name="span">目标字节缓冲区 / Target byte buffer.</param>
    /// <param name="offset">起始偏移量 / Starting offset.</param>
    /// <param name="info">预计算的头部写入信息 / Pre-computed header write info.</param>
    /// <returns>写入后的偏移量 / The offset after writing.</returns>
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

    /// <summary>
    /// 从 Span 缓冲区解析头部信息（用于非管道场景） / Parses header information from a Span buffer (for non-pipeline scenarios).
    /// </summary>
    /// <param name="span">源字节缓冲区 / Source byte buffer.</param>
    /// <param name="offset">起始偏移量，解析后会被推进 / Starting offset; advanced after parsing.</param>
    /// <param name="headerCount">头部数量 / Number of headers.</param>
    /// <param name="headers">解析后的头部字典 / The parsed headers dictionary.</param>
    /// <returns>解析成功返回 true；格式无效返回 false / True if parsing succeeded; false if the format is invalid.</returns>
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

    /// <summary>
    /// 从序列读取器中解析头部信息 / Parses header information from a sequence reader.
    /// </summary>
    /// <param name="reader">字节序列读取器 / Byte sequence reader.</param>
    /// <param name="headerCount">头部数量 / Number of headers.</param>
    /// <param name="headers">解析后的头部字典 / The parsed headers dictionary.</param>
    /// <returns>解析成功返回 true；格式无效返回 false / True if parsing succeeded; false if the format is invalid.</returns>
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

    /// <summary>
    /// 将请求帧内容写入 Span 缓冲区（不含总长度前缀） / Writes request frame content to a Span buffer (without total length prefix).
    /// </summary>
    /// <param name="span">目标字节缓冲区 / Target byte buffer.</param>
    /// <param name="requestId">请求标识符 / Request identifier.</param>
    /// <param name="serviceName">目标服务名称 / Target service name.</param>
    /// <param name="methodName">目标方法名称 / Target method name.</param>
    /// <param name="info">预计算的头部写入信息 / Pre-computed header write info.</param>
    /// <param name="svcByteCount">服务名的 UTF-8 字节长度 / UTF-8 byte length of the service name.</param>
    /// <param name="metByteCount">方法名的 UTF-8 字节长度 / UTF-8 byte length of the method name.</param>
    /// <returns>写入后的偏移量 / The offset after writing.</returns>
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

    /// <summary>
    /// 将响应帧内容写入 Span 缓冲区（不含总长度前缀） / Writes response frame content to a Span buffer (without total length prefix).
    /// </summary>
    /// <param name="span">目标字节缓冲区 / Target byte buffer.</param>
    /// <param name="requestId">请求标识符 / Request identifier.</param>
    /// <param name="isAborted">请求是否被中止 / Whether the request was aborted.</param>
    /// <param name="info">预计算的头部写入信息 / Pre-computed header write info.</param>
    /// <param name="dataLength">响应负载的字节长度 / Byte length of the response payload.</param>
    /// <returns>写入后的偏移量 / The offset after writing.</returns>
    public static int WriteResponseSpan(Span<byte> span, uint requestId, bool isAborted, in HeaderWriteInfo info, int dataLength)
    {
        span[0] = (byte)(isAborted ? RpcMessageType.Error : RpcMessageType.Response);
        BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(1, 4), requestId);
        BinaryPrimitives.WriteInt32LittleEndian(span.Slice(5, 4), isAborted ? 500 : 200);
        int offset = 9;
        offset = WriteHeaders(span, offset, in info);
        return offset;
    }

    /// <summary>
    /// 将取消请求帧写入 Span 缓冲区 / Writes a cancel request frame to a Span buffer.
    /// </summary>
    /// <param name="span">目标字节缓冲区（至少 5 字节） / Target byte buffer (at least 5 bytes).</param>
    /// <param name="requestId">要取消的请求标识符 / The request identifier to cancel.</param>
    public static void WriteCancelSpan(Span<byte> span, uint requestId)
    {
        span[0] = (byte)RpcMessageType.Cancel;
        BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(1, 4), requestId);
    }
}
