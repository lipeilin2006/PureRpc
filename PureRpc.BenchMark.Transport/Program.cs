using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PureRpc;
using PureRpc.Abstractions;
using PureRpc.BenchMark.Transport;

const int TotalRequests = 100_000;
const int Concurrency = 500;

if (args.Length > 0)
{
    await RunSingle(args[0]);
    return;
}

var results = new List<(string Name, double Qps)>();

var transports = new[] { "TCP", "KCP", "WebSocket", "HTTP/2", "QUIC" };
foreach (var name in transports)
{
    var psi = new ProcessStartInfo
    {
        FileName = Environment.ProcessPath,
        Arguments = name,
        UseShellExecute = false,
        RedirectStandardOutput = true,
    };
    using var proc = Process.Start(psi)!;
    var line = await proc.StandardOutput.ReadLineAsync();
    await proc.WaitForExitAsync();

    double qps;
    if (line != null && double.TryParse(line, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out qps))
    {
        Console.WriteLine($"  [{name}] {qps,10:F0} QPS");
        results.Add((name, qps));
    }
    else
    {
        Console.WriteLine($"  [{name}]     N/A");
        if (line != null) Console.Error.WriteLine(line);
    }
}

Console.WriteLine();
Console.WriteLine("═══════════════════════════════════════");
Console.WriteLine("  Transport Benchmark Summary");
Console.WriteLine("═══════════════════════════════════════");
foreach (var (name, qps) in results.OrderByDescending(r => r.Qps))
    Console.WriteLine($"  {name,-10} {qps,10:F0} QPS");
Console.WriteLine("═══════════════════════════════════════");

async Task RunSingle(string name)
{
    var (setupServer, setupClient) = name switch
    {
        "TCP" => ((Func<IServerBuilder, IServerBuilder>)(s => s.WithTcpTransport(5031)),
                  (Func<IClientBuilder, IClientBuilder>)(c => c.WithTcpTransport("127.0.0.1", 5031))),
        "KCP" => (s => s.WithKcpTransport(5032),
                  c => c.WithKcpTransport("127.0.0.1", (ushort)5032)),
        "WebSocket" => (s => s.WithWebSocketTransport(5033),
                        c => c.WithWebSocketTransport("ws://127.0.0.1:5033/rpc")),
        "HTTP/2" => (s => s.WithHttp2Transport(5034),
                     c => c.WithHttp2Transport("http://127.0.0.1:5034/rpc")),
        "QUIC" => (s => s.WithQuicTransport(5035, o => o.ServerCertificatePath = "/tmp/devcert.pfx"),
                    c => c.WithQuicTransport("127.0.0.1", 5035)),
        _ => throw new ArgumentException($"Unknown transport: {name}"),
    };

    var serverBuilder = Host.CreateApplicationBuilder();
    serverBuilder.Logging.ClearProviders();
    setupServer(serverBuilder.Services.AddPureRpcServer())
        .WithMemoryPackSerializer()
        .WithTestRpcService<TestRpcService>();
    var serverHost = serverBuilder.Build();
    await serverHost.StartAsync();

    var clientBuilder = Host.CreateApplicationBuilder();
    clientBuilder.Logging.ClearProviders();
    setupClient(clientBuilder.Services.AddPureRpcClient())
        .WithMemoryPackSerializer()
        .WithTestRpcServiceProxy();
    var clientHost = clientBuilder.Build();
    await clientHost.StartAsync();

    var svc = clientHost.Services.GetRequiredService<ITestRpcService>();

    Console.Error.WriteLine($"[{name}] Warming up...");
    for (int i = 0; i < 1000; i++)
    {
        int a = Random.Shared.Next(0, 10_000);
        int b = Random.Shared.Next(0, 10_000);
        int result = await svc.TestAsync(new TestRequest { A = a, B = b });
        if (result != a + b) throw new InvalidOperationException($"Warmup failed: {a}+{b}!={result}");
    }

    Console.Error.WriteLine($"[{name}] Benchmarking: {TotalRequests} requests, concurrency={Concurrency}...");
    var sw = Stopwatch.StartNew();
    long completed = 0, wrong = 0, faulted = 0;
    int sent = 0;

    var workers = new Task[Concurrency];
    for (int i = 0; i < Concurrency; i++)
    {
        int wid = i;
        workers[i] = Task.Run(async () =>
        {
            var rng = new Random(unchecked(Environment.TickCount * 31 + wid));
            while (true)
            {
                int cur = Interlocked.Increment(ref sent);
                if (cur > TotalRequests) break;
                int a = rng.Next(0, 1_000_000), b = rng.Next(0, 1_000_000);
                try
                {
                    var res = await svc.TestAsync(new TestRequest { A = a, B = b });
                    if (res == a + b) Interlocked.Increment(ref completed);
                    else Interlocked.Increment(ref wrong);
                }
                catch { Interlocked.Increment(ref faulted); }
            }
        });
    }
    await Task.WhenAll(workers);
    sw.Stop();

    await clientHost.StopAsync();
    await serverHost.StopAsync();

    var elapsed = sw.Elapsed.TotalSeconds;
    Console.WriteLine($"{completed / elapsed:F0}");
}
