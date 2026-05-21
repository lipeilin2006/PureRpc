using System.Diagnostics;
using MemoryPack;
using MessagePack;
using ProtoBuf;
using System.Text.Json;
using PureRpc.BenchMark.Serializer;

const int TotalRequests = 100_000;
const int Concurrency = 500;

if (args.Length > 0)
{
    await RunSingle(args[0]);
    return;
}

var results = new List<(string Name, double Qps)>();

foreach (var name in new[] { "MemoryPack", "MessagePack", "Json", "Protobuf" })
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
Console.WriteLine("  Serializer Benchmark Summary");
Console.WriteLine("═══════════════════════════════════════");
foreach (var (name, qps) in results.OrderByDescending(r => r.Qps))
    Console.WriteLine($"  {name,-12} {qps,10:F0} QPS");
Console.WriteLine("═══════════════════════════════════════");

async Task RunSingle(string name)
{
    switch (name)
    {
        case "MemoryPack":
            await RunMemoryPack();
            break;
        case "MessagePack":
            await RunMessagePack();
            break;
        case "Json":
            await RunJson();
            break;
        case "Protobuf":
            await RunProtobuf();
            break;
    }
}

async Task RunMemoryPack()
{
    long errors = 0, ok = 0;
    int sent = 0;
    var sw = Stopwatch.StartNew();
    var workers = new Task[Concurrency];
    for (int i = 0; i < Concurrency; i++)
    {
        int wid = i;
        workers[i] = Task.Run(() =>
        {
            var rng = new Random(unchecked(Environment.TickCount * 31 + wid));
            byte[]? bytes = null;
            while (true)
            {
                int cur = Interlocked.Increment(ref sent);
                if (cur > TotalRequests) break;
                int a = rng.Next(0, 1_000_000), b = rng.Next(0, 1_000_000);
                try
                {
                    var req = new TestRequest { A = a, B = b };
                    bytes = MemoryPackSerializer.Serialize(req);
                    var back = MemoryPackSerializer.Deserialize<TestRequest>(bytes);
                    if (back.A == a && back.B == b) Interlocked.Increment(ref ok);
                    else Interlocked.Increment(ref errors);
                }
                catch { Interlocked.Increment(ref errors); }
            }
        });
    }
    await Task.WhenAll(workers);
    sw.Stop();
    Console.WriteLine($"{ok / sw.Elapsed.TotalSeconds:F0}");
}

async Task RunMessagePack()
{
    var opts = MessagePackSerializerOptions.Standard;
    long errors = 0, ok = 0;
    int sent = 0;
    var sw = Stopwatch.StartNew();
    var workers = new Task[Concurrency];
    for (int i = 0; i < Concurrency; i++)
    {
        int wid = i;
        workers[i] = Task.Run(() =>
        {
            var rng = new Random(unchecked(Environment.TickCount * 31 + wid));
            while (true)
            {
                int cur = Interlocked.Increment(ref sent);
                if (cur > TotalRequests) break;
                int a = rng.Next(0, 1_000_000), b = rng.Next(0, 1_000_000);
                try
                {
                    var req = new TestRequest { A = a, B = b };
                    var bytes = MessagePackSerializer.Serialize(req, opts);
                    var back = MessagePackSerializer.Deserialize<TestRequest>(bytes, opts);
                    if (back.A == a && back.B == b) Interlocked.Increment(ref ok);
                    else Interlocked.Increment(ref errors);
                }
                catch { Interlocked.Increment(ref errors); }
            }
        });
    }
    await Task.WhenAll(workers);
    sw.Stop();
    Console.WriteLine($"{ok / sw.Elapsed.TotalSeconds:F0}");
}

async Task RunJson()
{
    var opts = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    long errors = 0, ok = 0;
    int sent = 0;
    var sw = Stopwatch.StartNew();
    var workers = new Task[Concurrency];
    for (int i = 0; i < Concurrency; i++)
    {
        int wid = i;
        workers[i] = Task.Run(() =>
        {
            var rng = new Random(unchecked(Environment.TickCount * 31 + wid));
            while (true)
            {
                int cur = Interlocked.Increment(ref sent);
                if (cur > TotalRequests) break;
                int a = rng.Next(0, 1_000_000), b = rng.Next(0, 1_000_000);
                try
                {
                    var req = new TestRequest { A = a, B = b };
                    var bytes = JsonSerializer.SerializeToUtf8Bytes(req, opts);
                    var back = JsonSerializer.Deserialize<TestRequest>(bytes, opts);
                    if (back.A == a && back.B == b) Interlocked.Increment(ref ok);
                    else Interlocked.Increment(ref errors);
                }
                catch { Interlocked.Increment(ref errors); }
            }
        });
    }
    await Task.WhenAll(workers);
    sw.Stop();
    Console.WriteLine($"{ok / sw.Elapsed.TotalSeconds:F0}");
}

async Task RunProtobuf()
{
    long errors = 0, ok = 0;
    int sent = 0;
    var sw = Stopwatch.StartNew();
    var workers = new Task[Concurrency];
    for (int i = 0; i < Concurrency; i++)
    {
        int wid = i;
        workers[i] = Task.Run(() =>
        {
            var rng = new Random(unchecked(Environment.TickCount * 31 + wid));
            while (true)
            {
                int cur = Interlocked.Increment(ref sent);
                if (cur > TotalRequests) break;
                int a = rng.Next(0, 1_000_000), b = rng.Next(0, 1_000_000);
                try
                {
                    var req = new TestRequest { A = a, B = b };
                    using var ms = new MemoryStream();
                    Serializer.Serialize(ms, req);
                    ms.Position = 0;
                    var back = Serializer.Deserialize<TestRequest>(ms);
                    if (back.A == a && back.B == b) Interlocked.Increment(ref ok);
                    else Interlocked.Increment(ref errors);
                }
                catch { Interlocked.Increment(ref errors); }
            }
        });
    }
    await Task.WhenAll(workers);
    sw.Stop();
    Console.WriteLine($"{ok / sw.Elapsed.TotalSeconds:F0}");
}
