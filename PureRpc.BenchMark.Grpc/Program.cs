using System.Diagnostics;
using Grpc.Net.Client;
using PureRpc.BenchMark.Grpc;

// ==========================================
// 1. Start gRPC server (HTTP/2 cleartext)
// ==========================================
AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);

var serverBuilder = WebApplication.CreateBuilder(args);
serverBuilder.Logging.ClearProviders();
serverBuilder.WebHost.UseKestrel(o => o.ListenLocalhost(5020, listen =>
{
    listen.Protocols = Microsoft.AspNetCore.Server.Kestrel.Core.HttpProtocols.Http2;
}));
serverBuilder.Services.AddGrpc();
var serverApp = serverBuilder.Build();
serverApp.MapGrpcService<BenchmarkServiceImpl>();
_ = serverApp.RunAsync();

// Wait for server to start
await Task.Delay(500);

// ==========================================
// 2. Create gRPC client channel
// ==========================================
var channel = GrpcChannel.ForAddress("http://127.0.0.1:5020");
var client = new Benchmark.BenchmarkClient(channel);

// ==========================================
// 3. Warmup
// ==========================================
Console.WriteLine("[gRPC Bench] Warming up...");
for (int i = 0; i < 1000; i++)
{
    int a = Random.Shared.Next(0, 10_000);
    int b = Random.Shared.Next(0, 10_000);
    var response = await client.UnaryCallAsync(new BenchmarkRequest { A = a, B = b });
    if (response.Result != a + b)
    {
        throw new InvalidOperationException($"Warmup validation failed: {a} + {b} != {response.Result}");
    }
}

// ==========================================
// 4. Benchmark
// ==========================================
const int TotalRequests = 100_000;
const int Concurrency = 500;

Console.WriteLine($"[gRPC Bench] Starting: {TotalRequests} requests, Concurrency={Concurrency}");

var sw = Stopwatch.StartNew();
long completedCount = 0;
long faultCount = 0;
long wrongResultCount = 0;
int sentCount = 0;
int nextProgress = 20_000;

var workers = new Task[Concurrency];
for (int workerId = 0; workerId < Concurrency; workerId++)
{
    int localWorkerId = workerId;
    workers[workerId] = RunWorkerAsync(localWorkerId);
}

await Task.WhenAll(workers);
sw.Stop();

// ==========================================
// 5. Results
// ==========================================
var elapsedS = sw.Elapsed.TotalSeconds;
Console.WriteLine("--------------------------------------------------");
Console.WriteLine($"  gRPC Test Results:");
Console.WriteLine($"  Duration:     {sw.Elapsed.TotalMilliseconds:F2} ms");
Console.WriteLine($"  Completed:    {completedCount}");
Console.WriteLine($"  Wrong Result: {wrongResultCount}");
Console.WriteLine($"  Faulted:      {faultCount}");
Console.WriteLine($"  Throughput:   {completedCount / elapsedS:F2} QPS");
Console.WriteLine($"  Avg Latency:  {sw.Elapsed.TotalMilliseconds / completedCount:F4} ms");
Console.WriteLine("--------------------------------------------------");

await serverApp.StopAsync();

async Task RunWorkerAsync(int workerId)
{
    var random = new Random(unchecked(Environment.TickCount * 31 + workerId));

    while (true)
    {
        int current = Interlocked.Increment(ref sentCount);
        if (current > TotalRequests) break;

        int a = random.Next(0, 1_000_000);
        int b = random.Next(0, 1_000_000);

        try
        {
            var response = await client.UnaryCallAsync(new BenchmarkRequest { A = a, B = b });
            if (response.Result == a + b)
            {
                Interlocked.Increment(ref completedCount);
            }
            else
            {
                Interlocked.Increment(ref wrongResultCount);
            }
        }
        catch
        {
            Interlocked.Increment(ref faultCount);
        }

        while (true)
        {
            int snapshot = Volatile.Read(ref nextProgress);
            if (current < snapshot) break;
            if (Interlocked.CompareExchange(ref nextProgress, snapshot + 20_000, snapshot) == snapshot)
            {
                Console.WriteLine($"[gRPC Bench] Progress: {snapshot} requests sent...");
                break;
            }
        }
    }
}
