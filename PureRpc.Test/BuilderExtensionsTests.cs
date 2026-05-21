using System.Buffers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using PureRpc.Abstractions;

namespace PureRpc.Test;

public sealed class BuilderExtensionsTests
{
    private static IServiceCollection AddMockServer(IServiceCollection services)
    {
        services.AddSingleton<IServerTransport>(_ => new MockServerTransport());
        services.AddSingleton<ISerializer>(_ => new MockSerializer());
        return services;
    }

    private static IServiceCollection AddMockClient(IServiceCollection services)
    {
        services.AddSingleton<IClientTransport>(_ => new MockClientTransport());
        services.AddSingleton<ISerializer>(_ => new MockSerializer());
        return services;
    }

    [Fact]
    public void AddPureRpcServer_RegistersRpcServer()
    {
        var services = new ServiceCollection();
        AddMockServer(services);
        services.AddPureRpcServer();

        var sp = services.BuildServiceProvider();
        var server = sp.GetService<IRpcServer>();
        Assert.NotNull(server);
    }

    [Fact]
    public void AddPureRpcServer_ReturnsServerBuilder()
    {
        var services = new ServiceCollection();
        AddMockServer(services);
        var builder = services.AddPureRpcServer();
        Assert.NotNull(builder);
        Assert.IsAssignableFrom<IServerBuilder>(builder);
    }

    [Fact]
    public void AddPureRpcClient_RegistersRpcClient()
    {
        var services = new ServiceCollection();
        AddMockClient(services);
        services.AddPureRpcClient();

        var sp = services.BuildServiceProvider();
        var client = sp.GetService<IRpcClient>();
        Assert.NotNull(client);
    }

    [Fact]
    public void AddPureRpcClient_ReturnsClientBuilder()
    {
        var services = new ServiceCollection();
        AddMockClient(services);
        var builder = services.AddPureRpcClient();
        Assert.NotNull(builder);
        Assert.IsAssignableFrom<IClientBuilder>(builder);
    }

    [Fact]
    public void ClientBuilder_Services_IsSameCollection()
    {
        var services = new ServiceCollection();
        var builder = services.AddPureRpcClient();
        Assert.Same(services, builder.Services);
    }

    [Fact]
    public void ServerBuilder_Services_IsSameCollection()
    {
        var services = new ServiceCollection();
        var builder = services.AddPureRpcServer();
        Assert.Same(services, builder.Services);
    }

    [Fact]
    public void AddPureRpcClient_RegistersHostedService()
    {
        var services = new ServiceCollection();
        services.AddPureRpcClient();
        var hasHostedService = services.Any(sd =>
            sd.ServiceType == typeof(IHostedService) &&
            sd.ImplementationType?.Name == "RpcClientHostedService");
        Assert.True(hasHostedService, "RpcClientHostedService should be registered");
    }

    [Fact]
    public void AddPureRpcServer_RegistersHostedService()
    {
        var services = new ServiceCollection();
        services.AddPureRpcServer();
        var hasHostedService = services.Any(sd =>
            sd.ServiceType == typeof(IHostedService) &&
            sd.ImplementationType?.Name == "RpcServerHostedService");
        Assert.True(hasHostedService, "RpcServerHostedService should be registered");
    }
}

public sealed class InterceptorRegistrationTests
{
    [Fact]
    public void AddServerInterceptor_RegistersInterceptor()
    {
        var services = new ServiceCollection();
        var builder = services.AddPureRpcServer();
        builder.AddServerInterceptor<NoopServerInterceptor>();
        var sp = services.BuildServiceProvider();
        var interceptors = sp.GetServices<IRpcServerInterceptor>().ToList();
        Assert.Single(interceptors);
        Assert.IsType<NoopServerInterceptor>(interceptors[0]);
    }

    [Fact]
    public void AddClientInterceptor_RegistersInterceptor()
    {
        var services = new ServiceCollection();
        var builder = services.AddPureRpcClient();
        builder.AddClientInterceptor<NoopClientInterceptor>();
        var sp = services.BuildServiceProvider();
        var interceptors = sp.GetServices<IRpcClientInterceptor>().ToList();
        Assert.Single(interceptors);
        Assert.IsType<NoopClientInterceptor>(interceptors[0]);
    }

    [Fact]
    public void AddServerInterceptor_ReturnsBuilder()
    {
        var services = new ServiceCollection();
        var builder = services.AddPureRpcServer();
        var result = builder.AddServerInterceptor<NoopServerInterceptor>();
        Assert.Same(builder, result);
    }

    [Fact]
    public void AddClientInterceptor_ReturnsBuilder()
    {
        var services = new ServiceCollection();
        var builder = services.AddPureRpcClient();
        var result = builder.AddClientInterceptor<NoopClientInterceptor>();
        Assert.Same(builder, result);
    }
}
