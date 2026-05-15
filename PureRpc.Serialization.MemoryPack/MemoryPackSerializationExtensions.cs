using MemoryPack;
using Microsoft.Extensions.DependencyInjection;
using PureRpc.Abstractions;
using PureRpc.Serialization.MemoryPack;

namespace PureRpc;

public static class MemoryPackSerializationExtensions
{
    /// <summary>
    /// 为服务端配置 MemoryPack 序列化器。
    /// </summary>
    public static IServerBuilder WithMemoryPackSerializer(this IServerBuilder builder)
    {
        builder.Services.AddSingleton<ISerializer, MemoryPackPureSerializer>();
        return builder;
    }

    /// <summary>
    /// 为客户端配置 MemoryPack 序列化器。
    /// </summary>
    public static IClientBuilder WithMemoryPackSerializer(this IClientBuilder builder)
    {
        builder.Services.AddSingleton<ISerializer, MemoryPackPureSerializer>();
        return builder;
    }
}