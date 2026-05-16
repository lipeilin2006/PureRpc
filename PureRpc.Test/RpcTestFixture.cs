using System.Buffers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PureRpc.Abstractions;

namespace PureRpc.Test;

public enum TransportType { Tcp, Kcp, WebSocket }
public enum SerializerType { MemoryPack, Json, MessagePack, Protobuf }

public sealed class RpcTestFixture : IAsyncLifetime
{
    private IHost? _serverHost;
    private IHost? _clientHost;

    public TransportType Transport { get; }
    public SerializerType Serializer { get; }

    public RpcTestFixture(TransportType transport = TransportType.Tcp, SerializerType serializer = SerializerType.MemoryPack)
    {
        Transport = transport;
        Serializer = serializer;
    }

    public ICalcService Client { get; private set; } = null!;
    public IAdvancedService AdvancedClient { get; private set; } = null!;
    public IRpcClient RawClient => _clientHost!.Services.GetRequiredService<IRpcClient>();

    public async Task InitializeAsync()
    {
        var port = GetAvailablePort();
        using var startCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        var serverBuilder = Host.CreateApplicationBuilder();
        serverBuilder.Logging.ClearProviders();
        var server = serverBuilder.Services.AddPureRpcServer()
            .WithSerializer(Serializer)
            .WithCalcService<CalcService>()
            .WithAdvancedService<AdvancedService>();
        AddServerTransport(server, port);
        _serverHost = serverBuilder.Build();
        await _serverHost.StartAsync(startCts.Token);

        // WebSocket (HttpListener) 比 TCP Socket 需要更多初始化时间
        int startupDelay = Transport == TransportType.WebSocket ? 1000 : 200;
        await Task.Delay(startupDelay);

        var clientBuilder = Host.CreateApplicationBuilder();
        clientBuilder.Logging.ClearProviders();
        var client = clientBuilder.Services.AddPureRpcClient()
            .WithSerializer(Serializer)
            .WithCalcServiceProxy()
            .WithAdvancedServiceProxy();
        AddClientTransport(client, port);
        _clientHost = clientBuilder.Build();
        await _clientHost.StartAsync(startCts.Token);

        int connectDelay = Transport == TransportType.WebSocket ? 500 : 200;
        await Task.Delay(connectDelay);
        Client = _clientHost.Services.GetRequiredService<ICalcService>();
        AdvancedClient = _clientHost.Services.GetRequiredService<IAdvancedService>();
    }

    public async Task<RpcTestFixture> WithInterceptor<TClient, TServer>()
        where TClient : class, IRpcClientInterceptor
        where TServer : class, IRpcServerInterceptor
    {
        if (_serverHost != null) await _serverHost.StopAsync();
        if (_clientHost != null) await _clientHost.StopAsync();

        var port = GetAvailablePort();
        using var startCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        var serverBuilder = Host.CreateApplicationBuilder();
        serverBuilder.Logging.ClearProviders();
        var server = serverBuilder.Services.AddPureRpcServer()
            .WithSerializer(Serializer)
            .WithCalcService<CalcService>()
            .WithAdvancedService<AdvancedService>()
            .AddServerInterceptor<TServer>();
        AddServerTransport(server, port);
        _serverHost = serverBuilder.Build();
        await _serverHost.StartAsync(startCts.Token);
        int startupDelay = Transport == TransportType.WebSocket ? 1000 : 200;
        await Task.Delay(startupDelay);

        var clientBuilder = Host.CreateApplicationBuilder();
        clientBuilder.Logging.ClearProviders();
        var client = clientBuilder.Services.AddPureRpcClient()
            .WithSerializer(Serializer)
            .WithCalcServiceProxy()
            .WithAdvancedServiceProxy()
            .AddClientInterceptor<TClient>();
        AddClientTransport(client, port);
        _clientHost = clientBuilder.Build();
        await _clientHost.StartAsync(startCts.Token);
        int connectDelay = Transport == TransportType.WebSocket ? 500 : 200;
        await Task.Delay(connectDelay);

        Client = _clientHost.Services.GetRequiredService<ICalcService>();
        AdvancedClient = _clientHost.Services.GetRequiredService<IAdvancedService>();
        return this;
    }

    private void AddServerTransport(IServerBuilder builder, int port)
    {
        switch (Transport)
        {
            case TransportType.Tcp:
                builder.WithTcpTransport(port);
                break;
            case TransportType.Kcp:
                builder.WithKcpTransport((ushort)port);
                break;
            case TransportType.WebSocket:
                builder.WithWebSocketTransport(port);
                break;
        }
    }

    private void AddClientTransport(IClientBuilder builder, int port)
    {
        switch (Transport)
        {
            case TransportType.Tcp:
                builder.WithTcpTransport("127.0.0.1", port);
                break;
            case TransportType.Kcp:
                builder.WithKcpTransport("127.0.0.1", (ushort)port);
                break;
            case TransportType.WebSocket:
                builder.WithWebSocketTransport($"ws://127.0.0.1:{port}/rpc/");
                break;
        }
    }

    public async Task DisposeAsync()
    {
        if (_clientHost != null)
        {
            using var cts = new CancellationTokenSource(5000);
            try { await _clientHost.StopAsync(cts.Token); } catch { }
            _clientHost.Dispose();
        }
        if (_serverHost != null)
        {
            using var cts = new CancellationTokenSource(5000);
            try { await _serverHost.StopAsync(cts.Token); } catch { }
            _serverHost.Dispose();
        }
    }

    private static readonly object _portLock = new();
    private static ushort GetAvailablePort()
    {
        lock (_portLock)
        {
            using var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
            listener.Start();
            var port = (ushort)((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }
    }
}

internal static class TestSerializerExtensions
{
    public static IServerBuilder WithSerializer(this IServerBuilder builder, SerializerType type)
    {
        return type switch
        {
            SerializerType.Json => builder.WithJsonSerializer(),
            SerializerType.MessagePack => builder.WithMessagePackSerializer(),
            SerializerType.Protobuf => builder.WithProtobufSerializer(),
            _ => builder.WithMemoryPackSerializer(),
        };
    }

    public static IClientBuilder WithSerializer(this IClientBuilder builder, SerializerType type)
    {
        return type switch
        {
            SerializerType.Json => builder.WithJsonSerializer(),
            SerializerType.MessagePack => builder.WithMessagePackSerializer(),
            SerializerType.Protobuf => builder.WithProtobufSerializer(),
            _ => builder.WithMemoryPackSerializer(),
        };
    }
}

internal static class CallCounter
{
    public static int ClientCount;
    public static int ServerCount;
    public static void Reset() { ClientCount = 0; ServerCount = 0; }
}

internal class TestClientInterceptor : IRpcClientInterceptor
{
    public ValueTask<ReadOnlySequence<byte>> InvokeAsync(
        string serviceName, string methodName, ReadOnlySequence<byte> requestPayload,
        CancellationToken ct, IDictionary<string, string>? headers, RpcCallDelegate next)
    {
        Interlocked.Increment(ref CallCounter.ClientCount);
        return next(serviceName, methodName, requestPayload, ct, headers);
    }
}

internal class TestServerInterceptor : IRpcServerInterceptor
{
    public ValueTask InvokeAsync(RpcContext context, ReadOnlySequence<byte> payload, RpcRequestDelegate next)
    {
        Interlocked.Increment(ref CallCounter.ServerCount);
        return next(context, payload);
    }
}