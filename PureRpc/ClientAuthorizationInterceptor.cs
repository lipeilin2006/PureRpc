using System;
using System.Buffers;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using PureRpc.Abstractions;

namespace PureRpc;

/// <summary>
/// 客户端认证拦截器，自动在出站请求中注入 Authorization header。
/// 通过委托函数动态获取 token，支持 Bearer / 自定义 scheme。
/// </summary>
public sealed class ClientAuthorizationInterceptor : IRpcClientInterceptor
{
    private readonly Func<string> _tokenFactory;
    private readonly string _scheme;

    public ClientAuthorizationInterceptor(Func<string> tokenFactory, string scheme = "Bearer")
    {
        _tokenFactory = tokenFactory ?? throw new ArgumentNullException(nameof(tokenFactory));
        _scheme = scheme;
    }

    public ValueTask<ReadOnlySequence<byte>> InvokeAsync(
        string serviceName, string methodName, ReadOnlySequence<byte> requestPayload,
        CancellationToken ct, IDictionary<string, string>? headers, RpcCallDelegate next)
    {
        headers ??= new Dictionary<string, string>();
        headers["Authorization"] = $"{_scheme} {_tokenFactory()}";
        return next(serviceName, methodName, requestPayload, ct, headers);
    }
}
