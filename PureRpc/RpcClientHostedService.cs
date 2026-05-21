using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PureRpc.Abstractions;

namespace PureRpc;

internal sealed partial class RpcClientHostedService : RpcHostedServiceBase
{
    private readonly IRpcClient _client;

    public RpcClientHostedService(
        IRpcClient client,
        ILogger<RpcClientHostedService> logger)
        : base(logger)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
    }

    #region Source Generated Logging
    [LoggerMessage(EventId = 401, Level = LogLevel.Information, Message = "[PureRpcClient] Hosted Service is starting...")]
    private static partial void LogServiceStarting(ILogger logger);

    [LoggerMessage(EventId = 402, Level = LogLevel.Information, Message = "[PureRpcClient] Hosted Service is stopping...")]
    private static partial void LogServiceStopping(ILogger logger);

    [LoggerMessage(EventId = 403, Level = LogLevel.Critical, Message = "[PureRpcClient] Failed to start client during host startup.")]
    private static partial void LogStartError(ILogger logger, Exception ex);
    #endregion

    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        LogServiceStarting(Logger);
        try
        {
            await _client.StartAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            LogStartError(Logger, ex);
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        LogServiceStopping(Logger);

        if (_client is IAsyncDisposable asyncDisposable)
        {
            await asyncDisposable.DisposeAsync();
        }
    }
}
