using Microsoft.Extensions.DependencyInjection;
using PureRpc.Abstractions;
using PureRpc.Serialization.MessagePack;

namespace PureRpc;

public static class MessagePackSerializationExtensions
{
    public static IServerBuilder WithMessagePackSerializer(this IServerBuilder builder)
    {
        builder.Services.AddSingleton<ISerializer, MessagePackPureSerializer>();
        return builder;
    }

    public static IClientBuilder WithMessagePackSerializer(this IClientBuilder builder)
    {
        builder.Services.AddSingleton<ISerializer, MessagePackPureSerializer>();
        return builder;
    }
}
