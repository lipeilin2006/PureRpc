using MemoryPack;
using Microsoft.Extensions.DependencyInjection;
using PureRpc.Abstractions;
using PureRpc.Serialization.MemoryPack;

namespace PureRpc;

/// <summary>
/// MemoryPack 序列化器注册扩展方法 / MemoryPack serializer registration extension methods.
/// 提供简洁的方法将 MemoryPack 序列化器注册到 RPC 客户端或服务端 / 
/// Provides concise methods for registering the MemoryPack serializer with RPC client or server.
/// </summary>
public static class MemoryPackSerializationExtensions
{
    /// <summary>
    /// 为服务端配置 MemoryPack 序列化器 / Configures the MemoryPack serializer for the server.
    /// </summary>
    /// <param name="builder">服务端构建器 / The server builder.</param>
    /// <returns>服务端构建器（支持链式调用） / The server builder (supports fluent chaining).</returns>
    public static IServerBuilder WithMemoryPackSerializer(this IServerBuilder builder)
    {
        builder.Services.AddSingleton<ISerializer, MemoryPackPureSerializer>();
        return builder;
    }

    /// <summary>
    /// 为客户端配置 MemoryPack 序列化器 / Configures the MemoryPack serializer for the client.
    /// </summary>
    /// <param name="builder">客户端构建器 / The client builder.</param>
    /// <returns>客户端构建器（支持链式调用） / The client builder (supports fluent chaining).</returns>
    public static IClientBuilder WithMemoryPackSerializer(this IClientBuilder builder)
    {
        builder.Services.AddSingleton<ISerializer, MemoryPackPureSerializer>();
        return builder;
    }
}