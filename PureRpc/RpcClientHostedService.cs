using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PureRpc.Abstractions;

namespace PureRpc;

/// <summary>
/// RPC 客户端托管服务 / RPC client hosted service.
/// 在 <see cref="IHostedService"/> 生命周期内自动启动和停止 RPC 客户端 / 
/// Automatically starts and stops the RPC client within the <see cref="IHostedService"/> lifecycle.
/// </summary>
internal sealed partial class RpcClientHostedService : RpcHostedServiceBase
{
    /// <summary>
    /// RPC 客户端实例 / The RPC client instance.
    /// </summary>
    private readonly IRpcClient _client;

    /// <summary>
    /// 初始化 RpcClientHostedService / Initializes the RpcClientHostedService.
    /// </summary>
    /// <param name="client">RPC 客户端实例 / The RPC client instance.</param>
    /// <param name="logger">日志记录器 / The logger instance.</param>
    /// <exception cref="ArgumentNullException">当 <paramref name="client"/> 为 null 时抛出 / 
    /// Thrown when <paramref name="client"/> is null.</exception>
    public RpcClientHostedService(
        IRpcClient client,
        ILogger<RpcClientHostedService> logger)
        : base(logger)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
    }

    #region Source Generated Logging
    /// <summary>
    /// 日志：客户端托管服务正在启动 / Log: client hosted service is starting.
    /// </summary>
    [LoggerMessage(EventId = 401, Level = LogLevel.Information, Message = "[PureRpcClient] Hosted Service is starting...")]
    private static partial void LogServiceStarting(ILogger logger);

    /// <summary>
    /// 日志：客户端托管服务正在停止 / Log: client hosted service is stopping.
    /// </summary>
    [LoggerMessage(EventId = 402, Level = LogLevel.Information, Message = "[PureRpcClient] Hosted Service is stopping...")]
    private static partial void LogServiceStopping(ILogger logger);

    /// <summary>
    /// 日志：客户端启动失败 / Log: client failed to start.
    /// </summary>
    [LoggerMessage(EventId = 403, Level = LogLevel.Critical, Message = "[PureRpcClient] Failed to start client during host startup.")]
    private static partial void LogStartError(ILogger logger, Exception ex);
    #endregion

    /// <summary>
    /// 启动客户端托管服务 / Starts the client hosted service.
    /// 捕获启动异常并记录日志，不传播以避免宿主进程崩溃 / 
    /// Catches startup exceptions and logs them without propagating to avoid host process crash.
    /// </summary>
    /// <param name="cancellationToken">取消令牌 / Cancellation token.</param>
    /// <returns>表示启动过程的 Task / A Task representing the startup process.</returns>
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

    /// <summary>
    /// 停止客户端托管服务 / Stops the client hosted service.
    /// 异步释放客户端连接 / Asynchronously disposes the client connection.
    /// </summary>
    /// <param name="cancellationToken">取消令牌 / Cancellation token.</param>
    /// <returns>表示停止过程的 Task / A Task representing the stop process.</returns>
    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        LogServiceStopping(Logger);

        if (_client is IAsyncDisposable asyncDisposable)
        {
            await asyncDisposable.DisposeAsync();
        }
    }
}
