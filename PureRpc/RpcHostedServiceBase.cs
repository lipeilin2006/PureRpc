using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace PureRpc;

/// <summary>
/// RPC 托管服务基类 / Base class for RPC hosted services.
/// 提供 <see cref="IHostedService"/> 的通用日志记录能力 / 
/// Provides common logging capability for <see cref="IHostedService"/> implementations.
/// </summary>
internal abstract class RpcHostedServiceBase : IHostedService
{
    /// <summary>
    /// 日志记录器 / The logger instance.
    /// </summary>
    protected ILogger Logger { get; }

    /// <summary>
    /// 初始化托管服务基类 / Initializes the hosted service base.
    /// </summary>
    /// <param name="logger">日志记录器 / The logger instance.</param>
    /// <exception cref="ArgumentNullException">当 <paramref name="logger"/> 为 null 时抛出 / 
    /// Thrown when <paramref name="logger"/> is null.</exception>
    protected RpcHostedServiceBase(ILogger logger)
    {
        Logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// 启动托管服务 / Starts the hosted service.
    /// </summary>
    /// <param name="cancellationToken">取消令牌 / Cancellation token.</param>
    /// <returns>表示启动过程的 Task / A Task representing the startup process.</returns>
    public abstract Task StartAsync(CancellationToken cancellationToken);

    /// <summary>
    /// 停止托管服务 / Stops the hosted service.
    /// </summary>
    /// <param name="cancellationToken">取消令牌 / Cancellation token.</param>
    /// <returns>表示停止过程的 Task / A Task representing the stop process.</returns>
    public abstract Task StopAsync(CancellationToken cancellationToken);
}
