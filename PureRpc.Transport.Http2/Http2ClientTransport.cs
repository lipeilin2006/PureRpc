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
        // H9: 断开旧连接
        _httpClient?.Dispose();

        var handler = new HttpClientHandler();
        if (_options.ServerCertificateValidation != null)
            handler.ServerCertificateCustomValidationCallback = _options.ServerCertificateValidation;
        // 默认不验证（开发环境）；生产环境应设置 options.ServerCertificateValidation
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

        var requestBytes = BuildFrame(requestId, serviceName, methodName, data, headers);
        using var content = new ByteArrayContent(requestBytes);
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");

        var response = await _httpClient.PostAsync("", content, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var responseBytes = await response.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
        ParseResponse(responseBytes, _onResponse);
    }

    public async ValueTask CancelRequestAsync(uint requestId, CancellationToken ct = default)
    {
        if (_httpClient == null) return;
        var cancelFrame = new byte[5];
        cancelFrame[0] = 8;
        BinaryPrimitives.WriteUInt32LittleEndian(cancelFrame.AsSpan(1), requestId);
        using var content = new ByteArrayContent(cancelFrame);
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
        try { await _httpClient.PostAsync("", content, ct).ConfigureAwait(false); }
        catch { /* best effort */ }
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
            var dict = new Dictionary<string, string>(headerCount);
            for (int i = 0; i < headerCount; i++)
            {
                int keyLen = BinaryPrimitives.ReadInt32LittleEndian(span.Slice(offset, 4));
                offset += 4;
                string key = Encoding.UTF8.GetString(span.Slice(offset, keyLen));
                offset += keyLen;
                int valLen = BinaryPrimitives.ReadInt32LittleEndian(span.Slice(offset, 4));
                offset += 4;
                string val = Encoding.UTF8.GetString(span.Slice(offset, valLen));
                offset += valLen;
                dict[key] = val;
            }
            respHeaders = dict;
        }
        bool isSuccess = type != 3 && statusCode == 200;
        var payload = new ReadOnlySequence<byte>(span.Slice(offset).ToArray());
        cb(respId, payload, isSuccess, isSuccess ? null : Encoding.UTF8.GetString(span.Slice(offset)), respHeaders);
    }

    private static byte[] BuildFrame(uint requestId, string serviceName, string methodName,
        ReadOnlySequence<byte> data, IDictionary<string, string>? headers)
    {
        int svc = Encoding.UTF8.GetByteCount(serviceName);
        int met = Encoding.UTF8.GetByteCount(methodName);
        int hc = headers?.Count ?? 0;
        int hbs = 0;
        string[]? k = null, v = null;
        int[]? ks = null, vs = null;
        if (hc > 0)
        {
            k = new string[hc]; v = new string[hc]; ks = new int[hc]; vs = new int[hc];
            int i = 0;
            foreach (var kv in headers!)
            {
                k[i] = kv.Key; v[i] = kv.Value;
                ks[i] = Encoding.UTF8.GetByteCount(kv.Key);
                vs[i] = Encoding.UTF8.GetByteCount(kv.Value);
                hbs += 4 + ks[i] + 4 + vs[i]; i++;
            }
        }
        int len = 1 + 4 + 4 + svc + 4 + met + 4 + hbs + (int)data.Length;
        var buf = new byte[len];
        buf[0] = 1;
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(1, 4), requestId);
        BinaryPrimitives.WriteInt32LittleEndian(buf.AsSpan(5, 4), svc);
        int p = 9;
        Encoding.UTF8.GetBytes(serviceName, buf.AsSpan(p, svc)); p += svc;
        BinaryPrimitives.WriteInt32LittleEndian(buf.AsSpan(p, 4), met); p += 4;
        Encoding.UTF8.GetBytes(methodName, buf.AsSpan(p, met)); p += met;
        BinaryPrimitives.WriteInt32LittleEndian(buf.AsSpan(p, 4), hc); p += 4;
        for (int i = 0; i < hc; i++)
        {
            BinaryPrimitives.WriteInt32LittleEndian(buf.AsSpan(p, 4), ks![i]); p += 4;
            Encoding.UTF8.GetBytes(k![i], buf.AsSpan(p, ks[i])); p += ks[i];
            BinaryPrimitives.WriteInt32LittleEndian(buf.AsSpan(p, 4), vs![i]); p += 4;
            Encoding.UTF8.GetBytes(v![i], buf.AsSpan(p, vs[i])); p += vs[i];
        }
        if (data.Length > 0)
        {
            if (data.IsSingleSegment) data.FirstSpan.CopyTo(buf.AsSpan(p));
            else foreach (var seg in data) { seg.Span.CopyTo(buf.AsSpan(p)); p += seg.Length; }
        }
        return buf;
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
