using System.Buffers;
using System.Collections.Concurrent;
using System.Buffers.Binary;
using System.IO.Pipelines;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.ObjectPool;
using Microsoft.Extensions.Options;
using PureRpc.Abstractions;

namespace PureRpc.Transport.Tcp;

internal sealed partial class TcpServerTransport : IServerTransport
{
    private const int InitialPendingRequestCapacity = 1024;
    private const int MaxFrameSize = 64 * 1024 * 1024; // 64MB
    private const int MaxServiceNameLength = 256;
    private const int MaxMethodNameLength = 256;
    private const int MaxHeaderCount = 64;
    private const int MaxHeaderKeyLength = 256;
    private const int MaxHeaderValueLength = 4096;
    private const int MaxPayloadSize = 64 * 1024 * 1024; // 64MB

    private readonly TcpServerOptions _options;
    private readonly ILogger<TcpServerTransport> _logger;
    // RpcContext 是每个请求都会用到的对象，使用对象池可以降低高并发请求下的重复分配。
    // 注意：池化对象必须在发送响应后归还，且归还前不能再被异步逻辑引用。
    private readonly ObjectPool<RpcContext> _contextPool;
    private Socket? _listenSocket;
    private bool _disposed;
    private long _nextConnectionId;
    private int _activeConnectionCount;
    private readonly SemaphoreSlim _requestThrottle = new(512, 512); // C-04: 限制并发请求数


    // 保存连接级写入器和写锁。SemaphoreSlim 为每条连接分配一次，避免同一连接上的响应帧交错写入。
    private readonly ConcurrentDictionary<long, (PipeWriter Writer, SemaphoreSlim Lock)> _connections =
        new(Environment.ProcessorCount, 256);

    // 跟踪活跃请求的可取消令牌，支持服务端取消传播
    private readonly ConcurrentDictionary<(long ConnectionId, uint RequestId), CancellationTokenSource> _activeRequests =
        new(Environment.ProcessorCount, InitialPendingRequestCapacity);

    public TcpServerTransport(IOptions<TcpServerOptions> options, ILogger<TcpServerTransport>? logger = null)
    {
        _options = options.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? NullLogger<TcpServerTransport>.Instance;

        // 初始化 RpcContext 对象池。MaximumRetained 只限制池中最多保留的对象数量，不限制峰值创建数量。
        // 改进建议：如果请求响应体很大，池化的 RpcContext 可能长期持有扩容后的 ArrayBufferWriter 内部数组；
        // 可在 Return 策略中检测容量，超过阈值时丢弃该 Context 或替换 ResponseBuffer。
        var provider = new DefaultObjectPoolProvider { MaximumRetained = 1024 };
        _contextPool = provider.Create(new RpcContextPolicy());
    }

    #region Source Generated Logging
    [LoggerMessage(EventId = 101, Level = LogLevel.Information, Message = "[TcpServer] Listening on {EndPoint}")]
    private static partial void LogListening(ILogger logger, System.Net.EndPoint endPoint);

    [LoggerMessage(EventId = 102, Level = LogLevel.Error, Message = "[TcpServer] Error in accept loop.")]
    private static partial void LogAcceptError(ILogger logger, Exception ex);

    [LoggerMessage(EventId = 103, Level = LogLevel.Debug, Message = "[TcpServer] Conn {Id} closed: {Msg}")]
    private static partial void LogConnectionClosed(ILogger logger, long id, string msg);

    [LoggerMessage(EventId = 104, Level = LogLevel.Error, Message = "[TcpServer] Failed to send response for Request {Id}")]
    private static partial void LogSendError(ILogger logger, Exception ex, uint id);
    #endregion

    public async Task StartAsync(Func<RpcContext, ReadOnlySequence<byte>, Task> onRequestReceived, CancellationToken ct)
    {
        if (_listenSocket != null) throw new InvalidOperationException("Server already running.");

        _listenSocket = new Socket(_options.EndPoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
        if (_options.ReuseAddress) _listenSocket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);

        try
        {
            _listenSocket.Bind(_options.EndPoint);
            _listenSocket.Listen(_options.Backlog);

            LogListening(_logger, _options.EndPoint);

            while (!ct.IsCancellationRequested)
            {
                // 限制最大并发连接数，防止资源耗尽
                if (Volatile.Read(ref _activeConnectionCount) >= _options.MaxConnections)
                {
                    await Task.Delay(100, ct).ConfigureAwait(false);
                    continue;
                }

                Socket clientSocket = await _listenSocket.AcceptAsync(ct).ConfigureAwait(false);
                clientSocket.NoDelay = true;

                long connId = Interlocked.Increment(ref _nextConnectionId);
                Interlocked.Increment(ref _activeConnectionCount);
                _ = HandleClientAsync(clientSocket, connId, onRequestReceived, ct);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (!_disposed)
        {
            LogAcceptError(_logger, ex);
        }
    }

    private async Task HandleClientAsync(Socket socket, long connId, Func<RpcContext, ReadOnlySequence<byte>, Task> onRequest, CancellationToken ct)
    {
        var remoteEP = socket.RemoteEndPoint;
        var netStream = new NetworkStream(socket, ownsSocket: true);
        Stream stream = netStream;
        if (_options.ServerCertificate != null)
        {
            var sslStream = new SslStream(netStream, leaveInnerStreamOpen: false);
            try
            {
                await sslStream.AuthenticateAsServerAsync(
                    new SslServerAuthenticationOptions { ServerCertificate = _options.ServerCertificate },
                    ct).ConfigureAwait(false);
            }
            catch
            {
                sslStream.Dispose();
                throw;
            }
            stream = sslStream;
        }

        // PipeReader/PipeWriter 负责复用读写缓冲，降低每次收包/发包的 byte[] 分配。
        // 改进建议：可以传入 PipeOptions/StreamPipeReaderOptions 使用自定义 MemoryPool，统一管理大块内存。
        var reader = PipeReader.Create(stream);
        var writer = PipeWriter.Create(stream);
        // 每个连接一个写锁，保证同一连接上的多个请求并发响应时，响应帧不会交叉写入。
        var writeLock = new SemaphoreSlim(1, 1);

        _connections[connId] = (writer, writeLock);

        try
        {
            while (!ct.IsCancellationRequested)
            {
                ReadResult result = await reader.ReadAsync(ct).ConfigureAwait(false);
                ReadOnlySequence<byte> buffer = result.Buffer;

                while (TryParseRequest(ref buffer, connId, out var header, out var payload))
                {
                    // 从对象池租借 Context，避免每个请求都 new RpcContext 和响应缓冲区。
                    // 注意：payload 仍然引用当前 PipeReader 的缓冲；onRequest 异步执行期间不能过早 Advance 到已消费位置。
                    var context = _contextPool.Get();
                    context.ConnectionId = connId;
                    context.RequestId = header.RequestId;
                    context.ServiceName = header.ServiceName;
                    context.MethodName = header.MethodName;
                    context.RemoteEndPoint = remoteEP;

                    // 填充请求头部元数据（如认证令牌），供服务端拦截器使用
                    if (header.Headers is { Count: > 0 })
                    {
                        foreach (var kv in header.Headers)
                        {
                            context.Headers[kv.Key] = kv.Value;
                        }
                    }

                    // 创建可取消标记，接收 Cancel 帧时中断请求处理
                    var requestCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    var key = (connId, header.RequestId);
                    _activeRequests[key] = requestCts;
                    context.CancellationToken = requestCts.Token;

                    // 这里采用 fire-and-forget，使同一连接可以继续读取后续请求。
                    _ = ProcessRequestAsync(onRequest, context, payload, requestCts, key);
                }

                reader.AdvanceTo(buffer.Start, buffer.End);
                if (result.IsCompleted) break;
            }
        }
        catch (Exception ex)
        {
            LogConnectionClosed(_logger, connId, ex.Message);
        }
        finally
        {
            Interlocked.Decrement(ref _activeConnectionCount);
            _connections.TryRemove(connId, out _);
            await reader.CompleteAsync().ConfigureAwait(false);
            await writer.CompleteAsync().ConfigureAwait(false);
            writeLock.Dispose();
            stream.Dispose();
        }
    }

    private async Task ProcessRequestAsync(
        Func<RpcContext, ReadOnlySequence<byte>, Task> handler, RpcContext context,
        ReadOnlySequence<byte> payload, CancellationTokenSource requestCts,
        (long ConnectionId, uint RequestId) key)
    {
        await _requestThrottle.WaitAsync().ConfigureAwait(false);
        try
        {
            await handler(context, payload).ConfigureAwait(false);
        }
        finally
        {
            _requestThrottle.Release();
            if (_activeRequests.TryRemove(key, out var removed))
                removed.Dispose();
        }
    }

    public async ValueTask SendResponseAsync(RpcContext context, CancellationToken ct)
    {
        try
        {
            if (_connections.TryGetValue(context.ConnectionId, out var conn))
            {
                await SendInternalAsync(context, conn.Writer, conn.Lock, ct).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            LogSendError(_logger, ex, context.RequestId);
        }
        finally
        {
            // --- 归还池 ---
            _contextPool.Return(context);
        }
    }

    private async ValueTask SendInternalAsync(RpcContext ctx, PipeWriter writer, SemaphoreSlim locker, CancellationToken ct)
    {
        // 响应体来自 RpcContext 中池化复用的 ArrayBufferWriter。
        // WrittenMemory 不会复制数据；真正的复制发生在 writer.Write(data.Span) 写入 PipeWriter 时。
        var data = ((ArrayBufferWriter<byte>)ctx.ResponseBuffer).WrittenMemory;

        // 预计算响应头部元数据大小，同时缓存 key/value 避免二次遍历
        var responseHeaders = ctx.Headers;
        int headerCount = responseHeaders.Count;
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
            foreach (var kv in responseHeaders)
            {
                keys[i] = kv.Key;
                values[i] = kv.Value;
                keySizes[i] = Encoding.UTF8.GetByteCount(kv.Key);
                valSizes[i] = Encoding.UTF8.GetByteCount(kv.Value);
                headersBlockSize += 4 + keySizes[i] + 4 + valSizes[i];
                i++;
            }
        }

        await locker.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            int bodyLen = 9 + 4 + headersBlockSize + data.Length;
            int frameHeaderSize = 13 + 4 + headersBlockSize;
            // 固定响应头 + 头部元数据直接写入 PipeWriter 缓冲区，避免分配临时数组。
            var span = writer.GetSpan(frameHeaderSize);

            BinaryPrimitives.WriteInt32LittleEndian(span[..4], bodyLen);
            span[4] = (byte)(ctx.IsAborted ? 2 : 1);
            BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(5, 4), ctx.RequestId);
            BinaryPrimitives.WriteInt32LittleEndian(span.Slice(9, 4), ctx.IsAborted ? 500 : 200);

            // 序列化响应头部元数据
            int headerOffset = 13;
            BinaryPrimitives.WriteInt32LittleEndian(span.Slice(headerOffset, 4), headerCount);
            headerOffset += 4;

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

            writer.Advance(frameHeaderSize);
            if (data.Length > 0)
            {
                writer.Write(data.Span);
            }

            await writer.FlushAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            locker.Release();
        }
    }

    private bool TryParseRequest(ref ReadOnlySequence<byte> buffer, long connectionId, out (uint RequestId, string ServiceName, string MethodName, IReadOnlyDictionary<string, string>? Headers) header, out ReadOnlySequence<byte> payload)
    {
        header = default; payload = default;
        if (buffer.Length < 9) return false;

        // 固定长度请求头使用栈内存，避免每个请求头产生托管堆分配。
        Span<byte> headSpan = stackalloc byte[9];
        buffer.Slice(0, 9).CopyTo(headSpan);
        int totalLen = BinaryPrimitives.ReadInt32LittleEndian(headSpan[..4]);

        // C-01: 防止 int 上溢绕过长度检查；最小 5 字节(type + requestId)
        if (totalLen < 5 || totalLen > MaxFrameSize) return false;
        if (buffer.Length < totalLen + 4U) return false;

        byte type = headSpan[4];
        uint reqId = BinaryPrimitives.ReadUInt32LittleEndian(headSpan.Slice(5, 4));

        // Cancel 帧：取消正在处理的请求，然后跳过
        if (type == (byte)RpcMessageType.Cancel)
        {
            var key = (connectionId, reqId);
            if (_activeRequests.TryRemove(key, out var cts))
            {
                cts.Cancel();
                cts.Dispose();
            }
            buffer = buffer.Slice(totalLen + 4);
            return false;
        }

        // 非 Request 类型跳过
        if (type != (byte)RpcMessageType.Request)
        {
            buffer = buffer.Slice(totalLen + 4);
            return false;
        }

        // M3: 最小请求帧至少 13 字节(type + requestId + svcLen + metLen + headerCount)
        if (totalLen < 13) return false;

        var reader = new SequenceReader<byte>(buffer.Slice(9, totalLen - 5));

        // C-02: 限制服务名/方法名长度，防内存耗尽
        if (!TryReadString(ref reader, out var svc, MaxServiceNameLength)) return false;
        if (!TryReadString(ref reader, out var met, MaxMethodNameLength)) return false;

        // 解析头部元数据
        if (!reader.TryReadLittleEndian(out int headerCount)) return false;
        // C-02: 限制 header 数量
        if (headerCount < 0 || headerCount > MaxHeaderCount) return false;
        IReadOnlyDictionary<string, string>? headers = null;
        if (headerCount > 0)
        {
            var dict = new Dictionary<string, string>(headerCount);
            for (int i = 0; i < headerCount; i++)
            {
                if (!TryReadString(ref reader, out var key, MaxHeaderKeyLength)) return false;
                if (!TryReadString(ref reader, out var val, MaxHeaderValueLength)) return false;
                dict[key] = val;
            }
            headers = dict;
        }

        header = (reqId, svc, met, headers);
        payload = reader.UnreadSequence;
        buffer = buffer.Slice(totalLen + 4);
        return true;
    }

    private static bool TryReadString(ref SequenceReader<byte> reader, out string result, int maxLength = int.MaxValue)
    {
        result = string.Empty;
        if (!reader.TryReadLittleEndian(out int len)) return false;
        // H-01: 拒绝负长度；C-02: 限制最大长度
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

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        _listenSocket?.Close();
        _connections.Clear();
        _requestThrottle.Dispose();
        await Task.CompletedTask;
    }
}
