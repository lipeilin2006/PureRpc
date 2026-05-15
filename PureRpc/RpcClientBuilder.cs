using Microsoft.Extensions.DependencyInjection;
using PureRpc.Abstractions;

namespace PureRpc
{
    public sealed class RpcClientBuilder : IClientBuilder
    {
        public IServiceCollection Services { get; }

        public RpcClientBuilder(IServiceCollection services)
        {
            Services = services;
        }
    }
}