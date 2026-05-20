using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace PureRpc;

/// <summary>
/// PureRpc 框架内建 Metrics 仪表，通过 System.Diagnostics.Metrics 对外暴露。
/// 注册为 Singleton 后自动被 OpenTelemetry 等观测框架收集。
/// </summary>
public sealed class RpcMetrics
{
    private static readonly Meter Meter = new("PureRpc", "1.0.0");

    // ── Server-side ──
    public Counter<int> ServerRequests { get; }
    public Histogram<double> ServerRequestDuration { get; }
    public Counter<int> ServerErrors { get; }
    public UpDownCounter<int> ServerActiveRequests { get; }

    // ── Client-side ──
    public Counter<int> ClientRequests { get; }
    public Histogram<double> ClientRequestDuration { get; }
    public Counter<int> ClientErrors { get; }

    public RpcMetrics()
    {
        ServerRequests = Meter.CreateCounter<int>("rpc.server.requests", "requests", "Total number of RPC requests received");
        ServerRequestDuration = Meter.CreateHistogram<double>("rpc.server.request_duration", "ms", "Request duration in milliseconds");
        ServerErrors = Meter.CreateCounter<int>("rpc.server.errors", "errors", "Total number of RPC errors on server");
        ServerActiveRequests = Meter.CreateUpDownCounter<int>("rpc.server.active_requests", "requests", "Current number of in-flight requests");

        ClientRequests = Meter.CreateCounter<int>("rpc.client.requests", "requests", "Total number of RPC calls made by client");
        ClientRequestDuration = Meter.CreateHistogram<double>("rpc.client.request_duration", "ms", "Call duration in milliseconds");
        ClientErrors = Meter.CreateCounter<int>("rpc.client.errors", "errors", "Total number of RPC errors on client");
    }
}
