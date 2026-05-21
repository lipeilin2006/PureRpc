using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace PureRpc;

/// <summary>
/// PureRpc 框架内建 Metrics 仪表，通过 System.Diagnostics.Metrics 对外暴露 / 
/// Built-in metrics instrument for the PureRpc framework, exposed via System.Diagnostics.Metrics.
/// 注册为 Singleton 后自动被 OpenTelemetry 等观测框架收集 / 
/// Registered as Singleton, automatically collected by OpenTelemetry and other observability frameworks.
/// </summary>
public sealed class RpcMetrics
{
    /// <summary>
    /// 全局 Meter 实例 / The global Meter instance.
    /// </summary>
    private static readonly Meter Meter = new("PureRpc", "1.0.0");

    /// <summary>
    /// 服务端接收到的请求总数计数器 / Server-side total request count counter.
    /// </summary>
    public Counter<int> ServerRequests { get; }

    /// <summary>
    /// 服务端请求耗时分布直方图（毫秒） / Server-side request duration histogram (milliseconds).
    /// </summary>
    public Histogram<double> ServerRequestDuration { get; }

    /// <summary>
    /// 服务端错误总数计数器 / Server-side total error count counter.
    /// </summary>
    public Counter<int> ServerErrors { get; }

    /// <summary>
    /// 服务端当前在途请求数上下计数器 / Server-side current in-flight request up-down counter.
    /// </summary>
    public UpDownCounter<int> ServerActiveRequests { get; }

    /// <summary>
    /// 客户端发起的请求总数计数器 / Client-side total request count counter.
    /// </summary>
    public Counter<int> ClientRequests { get; }

    /// <summary>
    /// 客户端请求耗时分布直方图（毫秒） / Client-side request duration histogram (milliseconds).
    /// </summary>
    public Histogram<double> ClientRequestDuration { get; }

    /// <summary>
    /// 客户端错误总数计数器 / Client-side total error count counter.
    /// </summary>
    public Counter<int> ClientErrors { get; }

    /// <summary>
    /// 初始化 RpcMetrics 实例并注册所有指标 / 
    /// Initializes the RpcMetrics instance and registers all instruments.
    /// </summary>
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
