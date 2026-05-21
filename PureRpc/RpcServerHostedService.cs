using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PureRpc.Abstractions;

namespace PureRpc;

/// <summary>
/// RPC 服务端托管服务 / RPC server hosted service.
/// 在 <see cref="IHostedService"/> 生命周期内自动启动和停止 RPC 服务器 / 
/// Automatically starts and stops the RPC server within the <see cref="IHostedService"/> lifecycle.
/// </summary>
internal sealed partial class RpcServerHostedService : RpcHostedServiceBase
{
    /// <summary>
    /// RPC 服务器实例 / The RPC server instance.
    /// </summary>
    private readonly IRpcServer _server;

    /// <summary>
    /// 初始化 RpcServerHostedService / Initializes the RpcServerHostedService.
    /// </summary>
    /// <param name="server">RPC 服务器实例 / The RPC server instance.</param>
    /// <param name="logger">日志记录器 / The logger instance.</param>
    /// <exception cref="ArgumentNullException">当 <paramref name="server"/> 为 null 时抛出 / 
    /// Thrown when <paramref name="server"/> is null.</exception>
    public RpcServerHostedService(
        IRpcServer server,
        ILogger<RpcServerHostedService> logger)
        : base(logger)
    {
        _server = server ?? throw new ArgumentNullException(nameof(server));
    }

    #region Source Generated Logging
    /// <summary>
    /// 日志：服务端托管服务正在启动 / Log: server hosted service is starting.
    /// </summary>
    [LoggerMessage(EventId = 301, Level = LogLevel.Information, Message = "[PureRpc] Server Hosted Service is starting...")]
    private static partial void LogServiceStarting(ILogger logger);

    /// <summary>
    /// 日志：服务端托管服务正在停止 / Log: server hosted service is stopping.
    /// </summary>
    [LoggerMessage(EventId = 302, Level = LogLevel.Information, Message = "[PureRpc] Server Hosted Service is stopping...")]
    private static partial void LogServiceStopping(ILogger logger);

    /// <summary>
    /// 日志：服务端监听循环被取消 / Log: server listening loop was canceled.
    /// </summary>
    [LoggerMessage(EventId = 303, Level = LogLevel.Information, Message = "[PureRpc] Server listening loop canceled.")]
    private static partial void LogListeningCanceled(ILogger logger);

    /// <summary>
    /// 日志：服务端后台任务崩溃 / Log: server background task crashed.
    /// </summary>
    [LoggerMessage(EventId = 304, Level = LogLevel.Critical, Message = "[PureRpc] Server background task crashed unexpectedly.")]
    private static partial void LogCriticalCrash(ILogger logger, Exception ex);
    #endregion

    /// <summary>
    /// 启动服务端托管服务 / Starts the server hosted service.
    /// 在后台线程中启动服务器，等待 2 秒超时 / 
    /// Starts the server on a background thread with a 2-second timeout wait.
    /// </summary>
    /// <param name="cancellationToken">取消令牌 / Cancellation token.</param>
    /// <returns>表示启动过程的 Task / A Task representing the startup process.</returns>
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

    /// <summary>
    /// 停止服务端托管服务 / Stops the server hosted service.
    /// 异步释放服务器资源 / Asynchronously disposes server resources.
    /// </summary>
    /// <param name="cancellationToken">取消令牌 / Cancellation token.</param>
    /// <returns>表示停止过程的 Task / A Task representing the stop process.</returns>
    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        LogServiceStopping(Logger);
        await _server.DisposeAsync();
    }
}
