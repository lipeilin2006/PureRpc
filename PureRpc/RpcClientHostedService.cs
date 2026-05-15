using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PureRpc.Abstractions;

namespace PureRpc;

/// <summary>
/// 负责在 .NET 通用主机启动时，自动启动 RPC 客户端的后台服务。
/// </summary>
internal sealed partial class RpcClientHostedService : IHostedService
{
    private readonly IRpcClient _client;
    private readonly ILogger<RpcClientHostedService> _logger;

    public RpcClientHostedService(
        IRpcClient client,
        ILogger<RpcClientHostedService> logger)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _logger = logger ?? NullLogger<RpcClientHostedService>.Instance;
    }

    #region Source Generated Logging
    [LoggerMessage(EventId = 401, Level = LogLevel.Information, Message = "[PureRpcClient] Hosted Service is starting...")]
    private static partial void LogServiceStarting(ILogger logger);

    [LoggerMessage(EventId = 402, Level = LogLevel.Information, Message = "[PureRpcClient] Hosted Service is stopping...")]
    private static partial void LogServiceStopping(ILogger logger);

    [LoggerMessage(EventId = 403, Level = LogLevel.Critical, Message = "[PureRpcClient] Failed to start client during host startup.")]
    private static partial void LogStartError(ILogger logger, Exception ex);
    #endregion

    /// <summary>
    /// 当程序启动时，自动调用客户端的 StartAsync。
    /// 此时 Transport 层会根据 Options 中的配置自动发起连接。
    /// </summary>
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        LogServiceStarting(_logger);
        try
        {
            // 触发连接逻辑
            await _client.StartAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            // 如果此处抛出异常且没有被捕获，可能会导致整个 .NET Host 启动失败（取决于主机配置）
            LogStartError(_logger, ex);
        }
    }

    /// <summary>
    /// 当程序停止时，优雅关闭连接。
    /// </summary>
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        LogServiceStopping(_logger);

        if (_client is IAsyncDisposable asyncDisposable)
        {
            await asyncDisposable.DisposeAsync();
        }
    }
}