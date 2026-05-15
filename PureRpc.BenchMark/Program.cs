using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PureRpc;
using PureRpc.BenchMark;
using System.Diagnostics;

// ==========================================
// 1. 初始化 (日志设为 None 以排除干扰)
// ==========================================
var serverBuilder = Host.CreateApplicationBuilder(args);
// 压测默认关闭日志，避免 Console I/O 干扰吞吐和时延结果。
serverBuilder.Logging.ClearProviders();
serverBuilder.Services.AddPureRpcServer()
    .WithKcpTransport(5010)
    .WithMemoryPackSerializer()
    .WithTestRpcService<TestRpcService>();
using var serverHost = serverBuilder.Build();
await serverHost.StartAsync();

var clientBuilder = Host.CreateApplicationBuilder(args);
clientBuilder.Logging.ClearProviders();
clientBuilder.Services.AddPureRpcClient()
    .WithKcpTransport("127.0.0.1", 5010)
    .WithMemoryPackSerializer()
    .WithTestRpcServiceProxy();
using var clientHost = clientBuilder.Build();
await clientHost.StartAsync();

var testService = clientHost.Services.GetRequiredService<ITestRpcService>();

// ==========================================
// 2. 预热 (Warmup) - 非常关键，让 JIT 完成内联优化
// ==========================================
Console.WriteLine("[Bench] Warming up...");
for (int i = 0; i < 1000; i++)
{
    int a = Random.Shared.Next(0, 10_000);
    int b = Random.Shared.Next(0, 10_000);
    int result = await testService.TestAsync(new TestRequest(a, b));
    if (result != a + b)
    {
        throw new InvalidOperationException($"Warmup validation failed: {a} + {b} != {result}");
    }
}

// ==========================================
// 3. 高性能压测核心
// ==========================================
const int TotalRequests = 100_000;
const int Concurrency = 500; // 增加并发深度以填满 Pipeline

Console.WriteLine($"[Bench] Starting: {TotalRequests} requests, Concurrency={Concurrency}");

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
// 4. 结果统计
// ==========================================
var elapsedS = sw.Elapsed.TotalSeconds;
Console.WriteLine("--------------------------------------------------");
Console.WriteLine($"  Test Results:");
Console.WriteLine($"  Duration:     {sw.Elapsed.TotalMilliseconds:F2} ms");
Console.WriteLine($"  Completed:    {completedCount}");
Console.WriteLine($"  Wrong Result: {wrongResultCount}");
Console.WriteLine($"  Faulted:      {faultCount}");
Console.WriteLine($"  Throughput:   {completedCount / elapsedS:F2} QPS");
Console.WriteLine($"  Avg Latency:  {sw.Elapsed.TotalMilliseconds / completedCount:F4} ms");
Console.WriteLine("--------------------------------------------------");

await clientHost.StopAsync();
await serverHost.StopAsync();

static async Task<bool> ValidateCallAsync(ITestRpcService testService, int a, int b)
{
    int result = await testService.TestAsync(new TestRequest(a, b));
    return result == a + b;
}

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
            if (await ValidateCallAsync(testService, a, b))
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
                Console.WriteLine($"[Bench] Progress: {snapshot} requests sent...");
                break;
            }
        }
    }
}
