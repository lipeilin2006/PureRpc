using System.Buffers;
using System.Buffers.Binary;
using System.IO.Pipelines;
using System.Net;
using System.Net.Quic;
using System.Net.Security;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PureRpc.Abstractions;

namespace PureRpc.Transport.Quic;

internal sealed partial class QuicClientTransport : IClientTransport
{
    private readonly QuicClientOptions _options;
    private readonly ILogger<QuicClientTransport> _logger;
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private readonly CancellationTokenSource _transportCts = new();

    private QuicConnection? _connection;
    private QuicStream? _stream;
    private PipeReader? _reader;
    private PipeWriter? _writer;
    private Task? _receiveTask;
    private bool _disposed;
    private bool _isConnected;

    public bool IsConnected => _isConnected && _connection != null && !_disposed;

    public QuicClientTransport(IOptions<QuicClientOptions> options, ILogger<QuicClientTransport>? logger = null)
    {
        _options = options.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? NullLogger<QuicClientTransport>.Instance;
    }

    public async Task ConnectAsync(
        Action<uint, ReadOnlySequence<byte>, bool, string?, IReadOnlyDictionary<string, string>?> onResponseReceived,
        CancellationToken ct)
    {
        var validate = _options.CertificateValidationCallback
            ?? new RemoteCertificateValidationCallback((_, _, _, _) => true);

        var clientOptions = new QuicClientConnectionOptions
        {
            RemoteEndPoint = _options.RemoteEndPoint,
            DefaultCloseErrorCode = 0,
            DefaultStreamErrorCode = 0,
            ClientAuthenticationOptions = new SslClientAuthenticationOptions
            {
                ApplicationProtocols = [new SslApplicationProtocol("purerpc")],
                RemoteCertificateValidationCallback = validate,
            },
        };

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(_options.ConnectTimeout);

        _connection = await QuicConnection.ConnectAsync(clientOptions, timeoutCts.Token).ConfigureAwait(false);
        _stream = await _connection.OpenOutboundStreamAsync(QuicStreamType.Bidirectional, ct).ConfigureAwait(false);
        _reader = PipeReader.Create(_stream);
        _writer = PipeWriter.Create(_stream);
        _isConnected = true;

        _receiveTask = RunReceiveLoopAsync(_reader, onResponseReceived);
        LogConnected(_logger, _options.RemoteEndPoint);
    }

    public async ValueTask SendAsync(
        uint requestId, string serviceName, string methodName,
        ReadOnlySequence<byte> data, CancellationToken ct, IDictionary<string, string>? headers = null)
    {
        if (!IsConnected || _writer == null)
            throw new IOException("QUIC client is not connected.");

        int svcByteCount = Encoding.UTF8.GetByteCount(serviceName);
        int metByteCount = Encoding.UTF8.GetByteCount(methodName);
        var headerInfo = RpcFrameCodec.PrepareHeaders(headers as IReadOnlyDictionary<string, string>);

        int bodyLen = 1 + 4 + 4 + svcByteCount + 4 + metByteCount + 4 + headerInfo.HeadersBlockSize + (int)data.Length;
        int headerTotal = 4 + 1 + 4 + 4 + svcByteCount + 4 + metByteCount + 4 + headerInfo.HeadersBlockSize;

        await _sendLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var span = _writer.GetSpan(headerTotal);

            BinaryPrimitives.WriteInt32LittleEndian(span[..4], bodyLen);
            RpcFrameCodec.WriteRequestSpan(span.Slice(4), requestId, serviceName, methodName, in headerInfo, svcByteCount, metByteCount);

            _writer.Advance(headerTotal);

            if (data.Length > 0)
            {
                if (data.IsSingleSegment) _writer.Write(data.FirstSpan);
                else foreach (var segment in data) _writer.Write(segment.Span);
            }

            var result = await _writer.FlushAsync(ct).ConfigureAwait(false);
            if (result.IsCompleted) throw new IOException("Connection closed during flush.");
        }
        finally
        {
            _sendLock.Release();
        }
    }

    private async Task RunReceiveLoopAsync(PipeReader reader,
        Action<uint, ReadOnlySequence<byte>, bool, string?, IReadOnlyDictionary<string, string>?> onResponseReceived)
    {
        try
        {
            while (!_transportCts.Token.IsCancellationRequested)
            {
                ReadResult result = await reader.ReadAsync(_transportCts.Token).ConfigureAwait(false);
                var buffer = result.Buffer;

                while (RpcFrameCodec.TryParseResponse(ref buffer, out var response))
                {
                    bool isSuccess = response.Type != (byte)RpcMessageType.Error && response.StatusCode == 200;
                    string? errorMsg = isSuccess ? null : RpcFrameCodec.DecodeUtf8(response.Payload);
                    onResponseReceived(response.RequestId, response.Payload, isSuccess, errorMsg, response.Headers);
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

    public async ValueTask CancelRequestAsync(uint requestId, CancellationToken ct = default)
    {
        if (!IsConnected || _writer == null) return;

        await _sendLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            LogRequestCancelled(_logger, requestId);
            var span = _writer.GetSpan(9);
            BinaryPrimitives.WriteInt32LittleEndian(span[..4], 5);
            RpcFrameCodec.WriteCancelSpan(span.Slice(5), requestId);
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
        if (_connection != null) await _connection.DisposeAsync().ConfigureAwait(false);

        if (_receiveTask != null)
        {
            try { await _receiveTask.ConfigureAwait(false); } catch { }
        }

        _sendLock.Dispose();
        _transportCts.Dispose();
    }

    #region Logging
    [LoggerMessage(EventId = 1306, Level = LogLevel.Information, Message = "[QUIC] Client connected to {EndPoint}")]
    private static partial void LogConnected(ILogger logger, System.Net.EndPoint endPoint);
    [LoggerMessage(EventId = 1307, Level = LogLevel.Error, Message = "[QUIC] Receive loop error")]
    private static partial void LogReceiveError(ILogger logger, Exception ex);
    [LoggerMessage(EventId = 1308, Level = LogLevel.Warning, Message = "[QUIC] Connection lost")]
    private static partial void LogConnectionLost(ILogger logger);
    [LoggerMessage(EventId = 1309, Level = LogLevel.Debug, Message = "[QUIC] Request {RequestId} cancelled")]
    private static partial void LogRequestCancelled(ILogger logger, uint requestId);
    #endregion
}
