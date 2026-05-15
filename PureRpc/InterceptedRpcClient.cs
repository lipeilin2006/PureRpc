using System.Buffers;
using PureRpc.Abstractions;

namespace PureRpc;

internal sealed class InterceptedRpcClient : IRpcClient
{
    private readonly IRpcClient _inner;
    private readonly RpcCallDelegate _pipeline;

    public bool IsAvailable => _inner.IsAvailable;

    public InterceptedRpcClient(IRpcClient inner, IEnumerable<IRpcClientInterceptor>? interceptors)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));

        var interceptorList = interceptors?.ToList();
        if (interceptorList is { Count: > 0 })
        {
            RpcCallDelegate pipeline = (svc, mtd, payload, ct, h) => inner.CallAsync(svc, mtd, payload, ct, h);
            for (int i = interceptorList.Count - 1; i >= 0; i--)
            {
                var interceptor = interceptorList[i];
                var next = pipeline;
                pipeline = (svc, mtd, payload, ct, h) => interceptor.InvokeAsync(svc, mtd, payload, ct, h, next);
            }
            _pipeline = pipeline;
        }
        else
        {
            _pipeline = (svc, mtd, payload, ct, h) => inner.CallAsync(svc, mtd, payload, ct, h);
        }
    }

    public Task StartAsync(CancellationToken ct = default) => _inner.StartAsync(ct);

    public ValueTask<ReadOnlySequence<byte>> CallAsync(
        string serviceName, string methodName, ReadOnlySequence<byte> requestPayload,
        CancellationToken ct, IDictionary<string, string>? headers = null)
        => _pipeline(serviceName, methodName, requestPayload, ct, headers);

    public void SetDefaultHeader(string key, string value) => _inner.SetDefaultHeader(key, value);

    public bool RemoveDefaultHeader(string key) => _inner.RemoveDefaultHeader(key);

    public void ClearDefaultHeaders() => _inner.ClearDefaultHeaders();

    public ValueTask DisposeAsync() => _inner.DisposeAsync();
}
