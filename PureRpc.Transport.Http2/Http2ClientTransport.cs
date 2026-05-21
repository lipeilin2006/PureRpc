using System.Buffers;
using System.Buffers.Binary;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PureRpc.Abstractions;

namespace PureRpc.Transport.Http2;

internal sealed partial class Http2ClientTransport : IClientTransport
{
    private readonly Http2ClientOptions _options;
    private readonly ILogger<Http2ClientTransport> _logger;
    private HttpClient? _httpClient;
    private bool _disposed;
    private Action<uint, ReadOnlySequence<byte>, bool, string?, IReadOnlyDictionary<string, string>?>? _onResponse;

    public bool IsConnected => _httpClient != null;

    public Http2ClientTransport(IOptions<Http2ClientOptions> options, ILogger<Http2ClientTransport>? logger = null)
    {
        _options = options.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? NullLogger<Http2ClientTransport>.Instance;
    }

    public Task ConnectAsync(
        Action<uint, ReadOnlySequence<byte>, bool, string?, IReadOnlyDictionary<string, string>?> onResponseReceived,
        CancellationToken ct)
    {
        _onResponse = onResponseReceived;
        _httpClient?.Dispose();

        var handler = new HttpClientHandler();
        if (_options.ServerCertificateValidation != null)
            handler.ServerCertificateCustomValidationCallback = _options.ServerCertificateValidation;
        _httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri(_options.Url),
            Timeout = _options.Timeout
        };
        _httpClient.DefaultRequestVersion = new Version(2, 0);
        _httpClient.DefaultVersionPolicy = HttpVersionPolicy.RequestVersionExact;
        LogConnected(_logger, _options.Url);
        return Task.CompletedTask;
    }

    public async ValueTask SendAsync(
        uint requestId, string serviceName, string methodName,
        ReadOnlySequence<byte> data, CancellationToken ct, IDictionary<string, string>? headers = null)
    {
        if (_httpClient == null) throw new IOException("HTTP/2 client is not connected.");

        int svcByteCount = Encoding.UTF8.GetByteCount(serviceName);
        int metByteCount = Encoding.UTF8.GetByteCount(methodName);
        var headerInfo = RpcFrameCodec.PrepareHeaders(headers as IReadOnlyDictionary<string, string>);

        int bodyLen = 1 + 4 + 4 + svcByteCount + 4 + metByteCount + 4 + headerInfo.HeadersBlockSize + (int)data.Length;
        byte[] rented = ArrayPool<byte>.Shared.Rent(bodyLen);
        try
        {
            int written = RpcFrameCodec.WriteRequestSpan(rented, requestId, serviceName, methodName, in headerInfo, svcByteCount, metByteCount);
            if (data.Length > 0)
            {
                if (data.IsSingleSegment) data.FirstSpan.CopyTo(rented.AsSpan(written));
                else foreach (var seg in data) { seg.Span.CopyTo(rented.AsSpan(written)); written += seg.Length; }
            }

            using var content = new ByteArrayContent(rented, 0, bodyLen);
            content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");

            var response = await _httpClient.PostAsync("", content, ct).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            var responseBytes = await response.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
            ParseResponse(responseBytes, _onResponse);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented);
        }
    }

    public async ValueTask CancelRequestAsync(uint requestId, CancellationToken ct = default)
    {
        if (_httpClient == null) return;
        byte[] rented = ArrayPool<byte>.Shared.Rent(5);
        try
        {
            RpcFrameCodec.WriteCancelSpan(rented, requestId);
            using var content = new ByteArrayContent(rented, 0, 5);
            content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
            try { await _httpClient.PostAsync("", content, ct).ConfigureAwait(false); }
            catch { }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented);
        }
    }

    private static void ParseResponse(byte[] responseBytes,
        Action<uint, ReadOnlySequence<byte>, bool, string?, IReadOnlyDictionary<string, string>?>? cb)
    {
        if (responseBytes.Length < 13 || cb == null) return;
        var span = responseBytes.AsSpan();
        byte type = span[0];
        uint respId = BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(1, 4));
        int statusCode = BinaryPrimitives.ReadInt32LittleEndian(span.Slice(5, 4));
        int headerCount = BinaryPrimitives.ReadInt32LittleEndian(span.Slice(9, 4));
        int offset = 13;
        IReadOnlyDictionary<string, string>? respHeaders = null;
        if (headerCount > 0)
        {
            if (!RpcFrameCodec.TryParseHeadersSpan(span, ref offset, headerCount, out var dict)) return;
            respHeaders = dict;
        }
        bool isSuccess = type != (byte)RpcMessageType.Error && statusCode == 200;
        var payload = new ReadOnlySequence<byte>(responseBytes, offset, responseBytes.Length - offset);
        cb(respId, payload, isSuccess, isSuccess ? null : Encoding.UTF8.GetString(span.Slice(offset)), respHeaders);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        if (_httpClient != null) { _httpClient.Dispose(); _httpClient = null; }
    }

    #region Logging
    [LoggerMessage(EventId = 1001, Level = LogLevel.Information, Message = "[HTTP2] Client ready at {Url}")]
    private static partial void LogConnected(ILogger logger, string url);
    #endregion
}
