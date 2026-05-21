using System;
using System.Buffers;
using System.Net;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;

namespace PureRpc.Abstractions;

public sealed class RpcContext
{
    public long ConnectionId { get; set; }
    public uint RequestId { get; set; }
    public string ServiceName { get; set; } = string.Empty;
    public string MethodName { get; set; } = string.Empty;
    public EndPoint? RemoteEndPoint { get; set; }

    public IBufferWriter<byte> ResponseBuffer { get; }

    public bool IsAborted { get; private set; }

    public CancellationToken CancellationToken { get; set; }

    public ClaimsPrincipal? User { get; set; }

    private Dictionary<string, string>? _headers;
    public Dictionary<string, string> Headers => _headers ??= new Dictionary<string, string>();
    internal Dictionary<string, string>? HeadersOrNull => _headers;

    private Dictionary<object, object?>? _items;
    public Dictionary<object, object?> Items => _items ??= new Dictionary<object, object?>();

    public RpcContext(IBufferWriter<byte> responseBuffer)
    {
        ResponseBuffer = responseBuffer ?? throw new ArgumentNullException(nameof(responseBuffer));
    }

    public void Abort()
    {
        IsAborted = true;
    }

    public void PopulateRequest(long connId, uint reqId, string svc, string met, EndPoint? remoteEP, IReadOnlyDictionary<string, string>? headers)
    {
        ConnectionId = connId;
        RequestId = reqId;
        ServiceName = svc;
        MethodName = met;
        RemoteEndPoint = remoteEP;
        if (headers is { Count: > 0 })
        {
            var h = Headers;
            foreach (var kv in headers)
                h[kv.Key] = kv.Value;
        }
    }

    public void Reset()
    {
        ConnectionId = 0;
        RequestId = 0;
        ServiceName = string.Empty;
        MethodName = string.Empty;
        RemoteEndPoint = null;
        User = null;
        IsAborted = false;
        CancellationToken = CancellationToken.None;

        if (_headers is { Count: > 0 })
            _headers.Clear();

        if (_items is { Count: > 0 })
            _items.Clear();

        if (ResponseBuffer is ArrayBufferWriter<byte> writer)
        {
            writer.Clear();
        }
    }
}