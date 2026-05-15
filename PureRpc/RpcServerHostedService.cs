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

    public Task StartAsync(CancellationToken cancellationToken)
    {
        LogServiceStarting(_logger);

        // 启动服务器处理循环。
        // 由于 StartAsync 内部是长连接监听（While 循环），
        // 必须在后台任务中运行，以防阻塞 Host 的启动流水线。
        _ = Task.Run(async () =>
        {
            try
            {
                // 此时 Server 内部的 Transport 会自动应用通过 IOptions 配置的监听地址
                await _server.StartAsync(cancellationToken);
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

        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        LogServiceStopping(_logger);

        // 触发优雅退出，关闭所有活跃连接并停止监听 Socket
        await _server.DisposeAsync();
    }
}