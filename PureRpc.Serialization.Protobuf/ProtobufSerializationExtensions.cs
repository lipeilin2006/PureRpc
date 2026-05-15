using Microsoft.Extensions.DependencyInjection;
using PureRpc.Abstractions;
using PureRpc.Serialization.Protobuf;

namespace PureRpc;

public static class ProtobufSerializationExtensions
{
    public static IServerBuilder WithProtobufSerializer(this IServerBuilder builder)
    {
        builder.Services.AddSingleton<ISerializer, ProtobufPureSerializer>();
        return builder;
    }

    public static IClientBuilder WithProtobufSerializer(this IClientBuilder builder)
    {
        builder.Services.AddSingleton<ISerializer, ProtobufPureSerializer>();
        return builder;
    }
}
