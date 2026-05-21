using System.Net;
using PureRpc.Abstractions;

namespace PureRpc.Test;

public sealed class EndPointHelperTests
{
    [Fact]
    public void ResolveEndPoint_IPAddressString_ReturnsIPEndPoint()
    {
        var ep = EndPointHelper.ResolveEndPoint("127.0.0.1", 5000);

        Assert.IsType<IPEndPoint>(ep);
        var ipEp = (IPEndPoint)ep;
        Assert.Equal(IPAddress.Loopback, ipEp.Address);
        Assert.Equal(5000, ipEp.Port);
    }

    [Fact]
    public void ResolveEndPoint_HostnameString_ReturnsDnsEndPoint()
    {
        var ep = EndPointHelper.ResolveEndPoint("example.com", 8080);

        Assert.IsType<DnsEndPoint>(ep);
        var dnsEp = (DnsEndPoint)ep;
        Assert.Equal("example.com", dnsEp.Host);
        Assert.Equal(8080, dnsEp.Port);
    }

    [Fact]
    public void ResolveEndPoint_IPv6Address_ReturnsIPEndPoint()
    {
        var ep = EndPointHelper.ResolveEndPoint("::1", 3000);

        Assert.IsType<IPEndPoint>(ep);
        var ipEp = (IPEndPoint)ep;
        Assert.Equal(IPAddress.IPv6Loopback, ipEp.Address);
        Assert.Equal(3000, ipEp.Port);
    }

    [Fact]
    public void ResolveEndPoint_Localhost_ReturnsDnsEndPoint()
    {
        var ep = EndPointHelper.ResolveEndPoint("localhost", 5001);

        Assert.IsType<DnsEndPoint>(ep);
        var dnsEp = (DnsEndPoint)ep;
        Assert.Equal("localhost", dnsEp.Host);
        Assert.Equal(5001, dnsEp.Port);
    }

    [Fact]
    public void ResolveEndPoint_AnyIP_ReturnsIPEndPoint()
    {
        var ep = EndPointHelper.ResolveEndPoint("0.0.0.0", 0);

        Assert.IsType<IPEndPoint>(ep);
        var ipEp = (IPEndPoint)ep;
        Assert.Equal(IPAddress.Any, ipEp.Address);
        Assert.Equal(0, ipEp.Port);
    }
}
