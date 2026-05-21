using System.Net;

namespace PureRpc.Abstractions;

internal static class EndPointHelper
{
    public static EndPoint ResolveEndPoint(string host, int port) =>
        IPAddress.TryParse(host, out var ip) ? new IPEndPoint(ip, port) : new DnsEndPoint(host, port);
}
