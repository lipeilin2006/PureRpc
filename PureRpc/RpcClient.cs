using System.Buffers;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.ObjectPool;
using PureRpc.Abstractions;

namespace PureRpc;

internal sealed partial class RpcClient : IRpcClient
{
    private const int InitialPendingRequestCapacity = 1024;

    private readonly IClientTransport _transport;
    private readonly ILogger<RpcClient> _logger;
    private readonly RpcMetrics _metrics;

    private readonly ConcurrentDictionary<uint, PendingRequest> _pendingRequests =
        new(Environment.ProcessorCount, InitialPendingRequestCapacity);

    private int _nextRequestId = 0;
    private int _connectionStarted = 0;
    private bool _isDisposed;
    private readonly Dictionary<string, string> _defaultHeaders = new();

    private readonly DefaultObjectPoolProvider _poolProvider = new() { MaximumRetained = 1024 };
    private readonly ObjectPool<PendingRequest> _pendingPool;

    private static readonly Action<object?, CancellationToken> CancelPendingRequestCallback = static (state, token) =>
    {
        if (state is not CancelRegistrationState s) return;
        if (s.Client._pendingRequests.TryRemove(s.RequestId, out var pending))
        {
            pending.SetCanceled(token);
            var client = s.Client;
            var reqId = s.RequestId;
            var logger = client._logger;
            _ = Task.Run(async () =>
            {
                try
                {
                    using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                    await client._transport.CancelRequestAsync(reqId).AsTask().WaitAsync(timeoutCts.Token);
                }
                catch (Exception ex)
                {
                    RpcClient.LogCancelError(logger, ex);
                }
            });
        }
    };

    public bool IsAvailable => _transport.IsConnected && !_isDisposed;

    public RpcClient(IClientTransport transport, ILogger<RpcClient>? logger = null, RpcMetrics? metrics = null)
    {
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        _logger = logger ?? NullLogger<RpcClient>.Instance;
        _metrics = metrics ?? new RpcMetrics();
        _pendingPool = _poolProvider.Create(new PendingRequestPolicy());
    }

    private sealed class PendingRequestPolicy : IPooledObjectPolicy<PendingRequest>
    {
        public PendingRequest Create() => new();
        public bool Return(PendingRequest obj)
        {
            obj.Reset();
            return true;
        }
    }

    #region Source Generated Logging
    [LoggerMessage(EventId = 201, Level = LogLevel.Information, Message = "[PureRpcClient] Starting transport connection...")]
    private static partial void LogStarting(ILogger logger);

    [LoggerMessage(EventId = 202, Level = LogLevel.Information, Message = "[PureRpcClient] Transport connected successfully.")]
    private static partial void LogConnected(ILogger logger);

    [LoggerMessage(EventId = 203, Level = LogLevel.Warning, Message = "[PureRpcClient] StartAsync was already called.")]
    private static partial void LogAlreadyStarted(ILogger logger);

    [LoggerMessage(EventId = 204, Level = LogLevel.Error, Message = "[PureRpcClient] Failed to start transport.")]
    private static partial void LogStartError(ILogger logger, Exception ex);

    [LoggerMessage(EventId = 205, Level = LogLevel.Debug, Message = "[PureRpcClient] Invoking {serviceName}.{methodName} (RequestId: {requestId})")]
    private static partial void LogInvoking(ILogger logger, string serviceName, string methodName, uint requestId);

    [LoggerMessage(EventId = 206, Level = LogLevel.Error, Message = "[PureRpcClient] Execution error for Request {requestId}.")]
    private static partial void LogExecutionError(ILogger logger, Exception ex, uint requestId);

    [LoggerMessage(EventId = 207, Level = LogLevel.Warning, Message = "[PureRpcClient] Cancel frame send failed.")]
    internal static partial void LogCancelError(ILogger logger, Exception ex);
    #endregion

    public async Task StartAsync(CancellationToken ct = default)
    {
        if (Interlocked.CompareExchange(ref _connectionStarted, 1, 0) != 0)
        {
            LogAlreadyStarted(_logger);
            return;
        }

        try
        {
            LogStarting(_logger);
            await _transport.ConnectAsync(OnResponseReceived, ct).ConfigureAwait(false);
            LogConnected(_logger);
        }
        catch (Exception ex)
        {
            Interlocked.Exchange(ref _connectionStarted, 0);
            LogStartError(_logger, ex);
            throw;
        }
    }

    public async ValueTask<ReadOnlySequence<byte>> CallAsync(
        string serviceName,
        string methodName,
        ReadOnlySequence<byte> requestPayload,
        CancellationToken ct,
        IDictionary<string, string>? headers = null)
    {
        if (_isDisposed) throw new ObjectDisposedException(nameof(RpcClient));

        if (!IsAvailable)
        {
            throw new IOException("RPC Client is not available (not connected or disposed).");
        }

        var startTimestamp = Stopwatch.GetTimestamp();
        var tags = new TagList { { "rpc.service", serviceName }, { "rpc.method", methodName } };

        _metrics.ClientRequests.Add(1, tags);

        uint requestId = (uint)Interlocked.Increment(ref _nextRequestId);

        var pending = _pendingPool.Get();
        _pendingRequests[requestId] = pending;

        CancellationTokenSource? timeoutCts = null;
        CancellationTokenSource? linkedCts = null;
        CancellationToken effectiveCt;
        CancellationTokenRegistration registration = default;
        try
        {
            if (ct.CanBeCanceled)
            {
                timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);
                effectiveCt = linkedCts.Token;
            }
            else
            {
                timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                effectiveCt = timeoutCts.Token;
            }

            registration = effectiveCt.UnsafeRegister(CancelPendingRequestCallback, new CancelRegistrationState { Client = this, RequestId = requestId });

            LogInvoking(_logger, serviceName, methodName, requestId);

            var mergedHeaders = MergeHeaders(headers);

            await _transport.SendAsync(requestId, serviceName, methodName, requestPayload, effectiveCt, mergedHeaders).ConfigureAwait(false);

            return await pending.AsValueTask().ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _metrics.ClientErrors.Add(1, tags);
            LogExecutionError(_logger, ex, requestId);
            throw;
        }
        finally
        {
            _metrics.ClientRequestDuration.Record(
                Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds, tags);
            registration.Dispose();
            linkedCts?.Dispose();
            timeoutCts?.Dispose();
            _pendingRequests.TryRemove(requestId, out _);
            _pendingPool.Return(pending);
        }
    }

    private IDictionary<string, string>? MergeHeaders(IDictionary<string, string>? callHeaders)
    {
        if (_defaultHeaders.Count == 0) return callHeaders;
        if (callHeaders == null) return new Dictionary<string, string>(_defaultHeaders);

        var merged = new Dictionary<string, string>(callHeaders);
        foreach (var kv in _defaultHeaders)
        {
            if (!merged.ContainsKey(kv.Key))
                merged[kv.Key] = kv.Value;
        }
        return merged;
    }

    internal void OnResponseReceived(uint requestId, ReadOnlySequence<byte> payload, bool success, string? error, IReadOnlyDictionary<string, string>? headers = null)
    {
        if (_pendingRequests.TryRemove(requestId, out var pending))
        {
            if (success)
            {
                pending.SetResult(new ReadOnlySequence<byte>(payload.ToArray()));
            }
            else
            {
                pending.SetException(new RpcException(error ?? "Unknown remote error."));
            }
        }
    }

    public void SetDefaultHeader(string key, string value)
    {
        _defaultHeaders[key] = value ?? throw new ArgumentNullException(nameof(value));
    }

    public bool RemoveDefaultHeader(string key) => _defaultHeaders.Remove(key);

    public void ClearDefaultHeaders() => _defaultHeaders.Clear();

    public async ValueTask DisposeAsync()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        await _transport.DisposeAsync().ConfigureAwait(false);

        var exception = new IOException("Client has been disposed.");
        foreach (var pending in _pendingRequests.Values)
        {
            pending.SetException(exception);
        }
        _pendingRequests.Clear();
    }

    private sealed class CancelRegistrationState
    {
        public RpcClient Client { get; set; } = null!;
        public uint RequestId { get; set; }
    }
}