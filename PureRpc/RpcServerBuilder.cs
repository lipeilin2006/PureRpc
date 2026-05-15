using Microsoft.Extensions.DependencyInjection;
using PureRpc.Abstractions;
using System.Collections.Generic;

namespace PureRpc
{
    public class RpcServerBuilder : IServerBuilder
    {
        public IServiceCollection Services { get; }

        public RpcServerBuilder(IServiceCollection services)
        {
            Services = services;
        }
    }
}