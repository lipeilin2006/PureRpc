using System.Net;

namespace PureRpc.Abstractions;

/// <summary>
/// 端点解析辅助工具 / Endpoint resolution helper utility.
/// 根据主机名字符串自动判断并创建 IPEndPoint 或 DnsEndPoint / 
/// Automatically determines and creates IPEndPoint or DnsEndPoint based on the host string.
/// </summary>
internal static class EndPointHelper
{
    /// <summary>
    /// 解析主机名和端口为 EndPoint 实例 / Resolves a host string and port into an EndPoint instance.
    /// 如果 host 是合法 IP 地址则返回 <see cref="IPEndPoint"/>，否则返回 <see cref="DnsEndPoint"/> / 
    /// Returns <see cref="IPEndPoint"/> if host is a valid IP address; otherwise returns <see cref="DnsEndPoint"/>.
    /// </summary>
    /// <param name="host">主机名或 IP 地址字符串 / Hostname or IP address string.</param>
    /// <param name="port">端口号 / Port number.</param>
    /// <returns>解析后的 EndPoint 实例 / The resolved EndPoint instance.</returns>
    public static EndPoint ResolveEndPoint(string host, int port) =>
        IPAddress.TryParse(host, out var ip) ? new IPEndPoint(ip, port) : new DnsEndPoint(host, port);
}
