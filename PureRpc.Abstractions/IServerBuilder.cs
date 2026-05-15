using Microsoft.Extensions.DependencyInjection;

namespace PureRpc.Abstractions
{
    public interface IServerBuilder
    {
        IServiceCollection Services { get; }

    }
}