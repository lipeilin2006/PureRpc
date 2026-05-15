using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Text;

namespace PureRpc.Abstractions
{
    public interface IClientBuilder
    {
        IServiceCollection Services { get; }
    }
}
