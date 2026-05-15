using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PureRpc.Abstractions;

namespace PureRpc.Test;

public enum TransportType { Tcp, Kcp, WebSocket }

public sealed class RpcTestFixture : IAsyncLifetime
{
    private IHost? _serverHost;
    private IHost? _clientHost;

    public TransportType Transport { get; }

    public RpcTestFixture(TransportType transport = TransportType.Tcp) => Transport = transport;

    public ICalcService Client { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        var port = GetAvailablePort();
        using var startCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        var serverBuilder = Host.CreateApplicationBuilder();
        serverBuilder.Logging.ClearProviders();
        var server = serverBuilder.Services.AddPureRpcServer()
            .WithMemoryPackSerializer()
            .WithCalcService<CalcService>();
        AddServerTransport(server, port);
        _serverHost = serverBuilder.Build();
        await _serverHost.StartAsync(startCts.Token);

        var clientBuilder = Host.CreateApplicationBuilder();
        clientBuilder.Logging.ClearProviders();
        var client = clientBuilder.Services.AddPureRpcClient()
            .WithMemoryPackSerializer()
            .WithCalcServiceProxy();
        AddClientTransport(client, port);
        _clientHost = clientBuilder.Build();
        await _clientHost.StartAsync(startCts.Token);

        if (Transport == TransportType.Kcp)
            await Task.Delay(300);
        Client = _clientHost.Services.GetRequiredService<ICalcService>();
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
                builder.WithWebSocketTransport($"ws://127.0.0.1:{port}/rpc");
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

    private static ushort GetAvailablePort()
    {
        using var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        var port = (ushort)((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}
