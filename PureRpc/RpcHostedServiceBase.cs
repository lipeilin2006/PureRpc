using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace PureRpc;

internal abstract class RpcHostedServiceBase : IHostedService
{
    protected ILogger Logger { get; }

    protected RpcHostedServiceBase(ILogger logger)
    {
        Logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public abstract Task StartAsync(CancellationToken cancellationToken);
    public abstract Task StopAsync(CancellationToken cancellationToken);
}
