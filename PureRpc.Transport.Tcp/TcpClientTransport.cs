using System.Buffers;
using System.Buffers.Binary;
using System.IO.Pipelines;
using System.Net.Security;
using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PureRpc.Abstractions;

namespace PureRpc.Transport.Tcp;

internal sealed partial class TcpClientTransport : IClientTransport
{
    private const int MaxHeaderCount = 64;
    private const int MaxHeaderKeyLength = 256;
    private const int MaxHeaderValueLength = 4096;

    private readonly TcpClientOptions _options;
    private readonly ILogger<TcpClientTransport> _logger;
    // PipeWriter 不是多写入者并发安全的，因此所有请求写入共用一个发送锁。
    // 这里会分配一个 SemaphoreSlim；它是连接级对象，生命周期较长，成本可接受。
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private readonly CancellationTokenSource _transportCts = new();

    private Socket? _socket;
    private Stream? _stream;
    private PipeWriter? _writer;
    private Task? _receiveTask;
    private bool _disposed;
    private bool _isConnected;

    public bool IsConnected => _isConnected && (_socket?.Connected ?? false);

    public TcpClientTransport(IOptions<TcpClientOptions> options, ILogger<TcpClientTransport>? logger = null)
    {
        _options = options.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? NullLogger<TcpClientTransport>.Instance;
    }

    #region Source Generated Logging
    [LoggerMessage(EventId = 501, Level = LogLevel.Information, Message = "[TcpClient] Connecting to {Target}")]
    private static partial void LogConnecting(ILogger logger, System.Net.EndPoint target);

    [LoggerMessage(EventId = 502, Level = LogLevel.Error, Message = "[TcpClient] Failed to connect to {Target}.")]
    private static partial void LogConnectError(ILogger logger, Exception ex, System.Net.EndPoint target);

    [LoggerMessage(EventId = 503, Level = LogLevel.Error, Message = "[TcpClient] Receive loop error.")]
    private static partial void LogReceiveError(ILogger logger, Exception ex);

    [LoggerMessage(EventId = 504, Level = LogLevel.Warning, Message = "[TcpClient] Connection lost.")]
    private static partial void LogConnectionLost(ILogger logger);

    [LoggerMessage(EventId = 505, Level = LogLevel.Debug, Message = "[TcpClient] Request {RequestId} cancelled.")]
    private static partial void LogRequestCancelled(ILogger logger, uint requestId);
    #endregion

    public async Task ConnectAsync(Action<uint, ReadOnlySequence<byte>, bool, string?, IReadOnlyDictionary<string, string>?> onResponseReceived, CancellationToken ct)
    {
        if (_isConnected) return;

        var target = _options.RemoteEndPoint;
        LogConnecting(_logger, target);

        _socket = new Socket(target.AddressFamily, SocketType.Stream, ProtocolType.Tcp)
        {
            NoDelay = _options.NoDelay,
            SendBufferSize = _options.SendBufferSize,
            ReceiveBufferSize = _options.ReceiveBufferSize
        };

        try
        {
            // 内存分配点：CreateLinkedTokenSource 会创建新的 CTS 和回调注册。
            // 改进建议：如果连接建立非常频繁，可以改成外层传入带超时的 CancellationToken，
            // 或封装复用策略；当前连接通常只建立一次，可读性优先。
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(_options.ConnectTimeout);

            await _socket.ConnectAsync(target, timeoutCts.Token).ConfigureAwait(false);

            _isConnected = true;

            var netStream = new NetworkStream(_socket, ownsSocket: true);
            Stream stream = netStream;
            if (_options.TargetHost != null)
            {
                var sslStream = new SslStream(netStream, leaveInnerStreamOpen: false);
                try
                {
                    await sslStream.AuthenticateAsClientAsync(
                        new SslClientAuthenticationOptions { TargetHost = _options.TargetHost },
                        ct).ConfigureAwait(false);
                }
                catch
                {
                    sslStream.Dispose();
                    throw;
                }
                stream = sslStream;
            }

            _stream = stream;

            // PipeReader/PipeWriter 内部会管理缓冲区，避免每次 Socket 读写都直接分配 byte[]。
            // 改进建议：如需进一步控制内存，可使用 StreamPipeReaderOptions/StreamPipeWriterOptions
            // 配置 MemoryPool、最小缓冲大小和 leaveOpen 等参数。
            var reader = PipeReader.Create(stream);
            _writer = PipeWriter.Create(stream);

            _receiveTask = RunReceiveLoopAsync(reader, onResponseReceived);
        }
        catch (Exception ex)
        {
            _isConnected = false;
            _socket?.Dispose();
            LogConnectError(_logger, ex, target);
            throw;
        }
    }

    public async ValueTask SendAsync(uint requestId, string serviceName, string methodName, ReadOnlySequence<byte> data, CancellationToken ct, IDictionary<string, string>? headers = null)
    {
        if (!IsConnected || _writer == null)
            throw new IOException("Transport is not connected.");

        // GetByteCount 只计算 UTF8 字节数，不分配中间 byte[]。
        // 后续 GetBytes 直接写入 PipeWriter 提供的 Span，避免为服务名/方法名单独分配数组。
        int svcByteCount = Encoding.UTF8.GetByteCount(serviceName);
        int metByteCount = Encoding.UTF8.GetByteCount(methodName);

        // 预计算头部序列化大小，同时缓存 key/value 避免二次遍历
        int headerCount = headers?.Count ?? 0;
        int headersBlockSize = 0;
        string[]? keys = null, values = null;
        int[]? keySizes = null, valSizes = null;
        if (headerCount > 0)
        {
            keys = new string[headerCount];
            values = new string[headerCount];
            keySizes = new int[headerCount];
            valSizes = new int[headerCount];
            int i = 0;
            foreach (var kv in headers!)
            {
                keys[i] = kv.Key;
                values[i] = kv.Value;
                keySizes[i] = Encoding.UTF8.GetByteCount(kv.Key);
                valSizes[i] = Encoding.UTF8.GetByteCount(kv.Value);
                headersBlockSize += 4 + keySizes[i] + 4 + valSizes[i];
                i++;
            }
        }

        int bodyLen = 1 + 4 + 4 + svcByteCount + 4 + metByteCount + 4 + headersBlockSize + (int)data.Length;
        int headerTotal = 4 + 1 + 4 + 4 + svcByteCount + 4 + metByteCount + 4 + headersBlockSize;

        await _sendLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            // 从 PipeWriter 获取连续写入空间。空间由 PipeWriter/内存池管理，通常不会为每个请求新建托管数组。
            var span = _writer.GetSpan(headerTotal);

            BinaryPrimitives.WriteInt32LittleEndian(span[..4], bodyLen);
            span[4] = (byte)RpcMessageType.Request;
            BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(5, 4), requestId);
            BinaryPrimitives.WriteInt32LittleEndian(span.Slice(9, 4), svcByteCount);
            int svcWritten = Encoding.UTF8.GetBytes(serviceName, span.Slice(13, svcByteCount));

            int metLenOffset = 13 + svcWritten;
            BinaryPrimitives.WriteInt32LittleEndian(span.Slice(metLenOffset, 4), metByteCount);
            Encoding.UTF8.GetBytes(methodName, span.Slice(metLenOffset + 4, metByteCount));

            // 写入头部元数据
            int headerOffset = metLenOffset + 4 + metByteCount;
            BinaryPrimitives.WriteInt32LittleEndian(span.Slice(headerOffset, 4), headerCount);
            headerOffset += 4;

            if (headerCount > 0)
            {
                for (int i = 0; i < headerCount; i++)
                {
                    BinaryPrimitives.WriteInt32LittleEndian(span.Slice(headerOffset, 4), keySizes![i]);
                    headerOffset += 4;
                    Encoding.UTF8.GetBytes(keys![i], span.Slice(headerOffset, keySizes[i]));
                    headerOffset += keySizes[i];

                    BinaryPrimitives.WriteInt32LittleEndian(span.Slice(headerOffset, 4), valSizes![i]);
                    headerOffset += 4;
                    Encoding.UTF8.GetBytes(values![i], span.Slice(headerOffset, valSizes[i]));
                    headerOffset += valSizes[i];
                }
            }

            _writer.Advance(headerTotal);

            if (data.Length > 0)
            {
                if (data.IsSingleSegment)
                {
                    _writer.Write(data.FirstSpan);
                }
                else
                {
                    foreach (var segment in data)
                    {
                        _writer.Write(segment.Span);
                    }
                }
            }

            var result = await _writer.FlushAsync(ct).ConfigureAwait(false);
            if (result.IsCompleted) throw new IOException("Connection closed during flush.");
        }
        finally
        {
            _sendLock.Release();
        }
    }

    private async Task RunReceiveLoopAsync(PipeReader reader, Action<uint, ReadOnlySequence<byte>, bool, string?, IReadOnlyDictionary<string, string>?> onResponseReceived)
    {
        try
        {
            while (!_transportCts.Token.IsCancellationRequested)
            {
                ReadResult result = await reader.ReadAsync(_transportCts.Token).ConfigureAwait(false);
                ReadOnlySequence<byte> buffer = result.Buffer;

                while (TryParseResponse(ref buffer, out var requestId, out var type, out var statusCode, out var headers, out var payload))
                {
                    bool isSuccess = type != RpcMessageType.Error && statusCode == 200;
                    // 错误响应需要转成字符串。这里 ToArray() + GetString 会产生 byte[] 和 string 两次分配。
                    // 改进建议：错误包通常较少，当前写法简单可靠；如果错误也处于热路径，
                    // 可使用 Encoding.UTF8.GetString(ReadOnlySequence) 的分段解码工具或自定义 decoder，避免 ToArray()。
                    string? errorMsg = isSuccess ? null : DecodeUtf8(payload);

                    onResponseReceived(requestId, payload, isSuccess, errorMsg, headers);
                }

                reader.AdvanceTo(buffer.Start, buffer.End);
                if (result.IsCompleted) break;
            }
        }
        catch (Exception ex) when (!_disposed)
        {
            LogReceiveError(_logger, ex);
        }
        finally
        {
            _isConnected = false;
            await reader.CompleteAsync().ConfigureAwait(false);
            LogConnectionLost(_logger);
        }
    }

    private bool TryParseResponse(ref ReadOnlySequence<byte> buffer, out uint requestId, out RpcMessageType type, out int statusCode, out IReadOnlyDictionary<string, string>? headers, out ReadOnlySequence<byte> payload)
    {
        requestId = 0;
        type = default;
        statusCode = 0;
        headers = null;
        payload = default;

        if (buffer.Length < 17) return false;

        // 固定长度协议头使用 stackalloc，避免为每个响应头分配 byte[]。
        Span<byte> headSpan = stackalloc byte[17];
        buffer.Slice(0, 17).CopyTo(headSpan);

        int totalLen = BinaryPrimitives.ReadInt32LittleEndian(headSpan[..4]);
        if (buffer.Length < totalLen + 4) return false;

        type = (RpcMessageType)headSpan[4];
        requestId = BinaryPrimitives.ReadUInt32LittleEndian(headSpan.Slice(5, 4));
        statusCode = BinaryPrimitives.ReadInt32LittleEndian(headSpan.Slice(9, 4));
        int headerCount = BinaryPrimitives.ReadInt32LittleEndian(headSpan.Slice(13, 4));
        // C-02: 限制 header 数量
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

    private static string DecodeUtf8(ReadOnlySequence<byte> payload)
    {
        if (payload.IsSingleSegment)
        {
            return Encoding.UTF8.GetString(payload.FirstSpan);
        }

        return Encoding.UTF8.GetString(payload.ToArray());
    }

    public async ValueTask CancelRequestAsync(uint requestId, CancellationToken ct = default)
    {
        if (!IsConnected || _writer == null) return;

        await _sendLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            LogRequestCancelled(_logger, requestId);
            // Cancel 帧只有 9 字节，直接写入 PipeWriter 的缓冲区，不产生临时数组。
            var span = _writer.GetSpan(9);
            BinaryPrimitives.WriteInt32LittleEndian(span[..4], 5);
            span[4] = (byte)RpcMessageType.Cancel;
            BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(5, 4), requestId);
            _writer.Advance(9);
            await _writer.FlushAsync().ConfigureAwait(false);
        }
        finally { _sendLock.Release(); }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        _isConnected = false;

        _transportCts.Cancel();

        if (_writer != null) await _writer.CompleteAsync().ConfigureAwait(false);
        _stream?.Dispose();
        _socket?.Dispose();

        if (_receiveTask != null)
        {
            try { await _receiveTask.ConfigureAwait(false); } catch { /* Ignore */ }
        }

        _sendLock.Dispose();
        _transportCts.Dispose();
    }
}
