using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PureRpc.Abstractions;

namespace PureRpc;

/// <summary>
/// 将 PureRpcServer 集成到 .NET 通用主机的托管服务。
/// 负责在应用启动时开启 RPC 监听，并在应用停止时释放资源。
/// </summary>
internal sealed partial class RpcServerHostedService : IHostedService
{
    private readonly IRpcServer _server;
    private readonly ILogger<RpcServerHostedService> _logger;

    public RpcServerHostedService(
        IRpcServer server,
        ILogger<RpcServerHostedService> logger)
    {
        _server = server ?? throw new ArgumentNullException(nameof(server));
        _logger = logger ?? NullLogger<RpcServerHostedService>.Instance;
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

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        LogServiceStarting(_logger);

        var serverTask = Task.Run(async () =>
        {
            try
            {
                await _server.StartAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                LogListeningCanceled(_logger);
            }
            catch (Exception ex)
            {
                LogCriticalCrash(_logger, ex);
            }
        }, cancellationToken);

        // Non-blocking transports (HTTP/2 via WebApplication) start quickly.
        // Blocking transports (TCP loop, KCP tick) run indefinitely — don't wait for them.
        var timeout = Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
        var completed = await Task.WhenAny(serverTask, timeout).ConfigureAwait(false);
        if (completed == serverTask)
        {
            await serverTask.ConfigureAwait(false);
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        LogServiceStopping(_logger);

        // 触发优雅退出，关闭所有活跃连接并停止监听 Socket
        await _server.DisposeAsync();
    }
}