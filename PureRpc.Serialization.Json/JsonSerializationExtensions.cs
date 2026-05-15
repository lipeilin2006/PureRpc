using Microsoft.Extensions.DependencyInjection;
using PureRpc.Abstractions;
using PureRpc.Serialization.Json;

namespace PureRpc;

public static class JsonSerializationExtensions
{
    public static IServerBuilder WithJsonSerializer(this IServerBuilder builder)
    {
        builder.Services.AddSingleton<ISerializer, JsonPureSerializer>();
        return builder;
    }

    public static IClientBuilder WithJsonSerializer(this IClientBuilder builder)
    {
        builder.Services.AddSingleton<ISerializer, JsonPureSerializer>();
        return builder;
    }
}
