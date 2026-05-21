using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PureRpc.Abstractions;

namespace PureRpc;

internal sealed partial class RpcServerHostedService : RpcHostedServiceBase
{
    private readonly IRpcServer _server;

    public RpcServerHostedService(
        IRpcServer server,
        ILogger<RpcServerHostedService> logger)
        : base(logger)
    {
        _server = server ?? throw new ArgumentNullException(nameof(server));
    }

    #region Source Generated Logging
    [LoggerMessage(EventId = 301, Level = LogLevel.Information, Message = "[PureRpc] Server Hosted Service is starting...")]
    private static partial void LogServiceStarting(ILogger logger);

    [LoggerMessage(EventId = 302, Level = LogLevel.Information, Message = "[PureRpc] Server Hosted Service is stopping...")]
    private static partial void LogServiceStopping(ILogger logger);

    [LoggerMessage(EventId = 303, Level = LogLevel.Information, Message = "[PureRpc] Server listening loop canceled.")]
    private static partial void LogListeningCanceled(ILogger logger);

    [LoggerMessage(EventId = 304, Level = LogLevel.Critical, Message = "[PureRpc] Server background task crashed unexpectedly.")]
    private static partial void LogCriticalCrash(ILogger logger, Exception ex);
    #endregion

    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        LogServiceStarting(Logger);

        var serverTask = Task.Run(async () =>
        {
            try
            {
                await _server.StartAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                LogListeningCanceled(Logger);
            }
            catch (Exception ex)
            {
                LogCriticalCrash(Logger, ex);
            }
        }, cancellationToken);

        var timeout = Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
        var completed = await Task.WhenAny(serverTask, timeout).ConfigureAwait(false);
        if (completed == serverTask)
        {
            await serverTask.ConfigureAwait(false);
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        LogServiceStopping(Logger);
        await _server.DisposeAsync();
    }
}
