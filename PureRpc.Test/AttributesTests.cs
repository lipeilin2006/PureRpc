namespace PureRpc.Test;

public sealed class RpcServiceAttributeTests
{
    [Fact]
    public void Constructor_NoArgs_ServiceNameIsNull()
    {
        var attr = new RpcServiceAttribute();
        Assert.Null(attr.ServiceName);
    }

    [Fact]
    public void Constructor_WithServiceName_SetsServiceName()
    {
        var attr = new RpcServiceAttribute("MyService");
        Assert.Equal("MyService", attr.ServiceName);
    }

    [Fact]
    public void Constructor_WithEmptyString_ServiceNameIsEmpty()
    {
        var attr = new RpcServiceAttribute("");
        Assert.Equal("", attr.ServiceName);
    }
}

public sealed class RpcMethodAttributeTests
{
    [Fact]
    public void Constructor_NoArgs_MethodNameIsNull()
    {
        var attr = new RpcMethodAttribute();
        Assert.Null(attr.MethodName);
        Assert.False(attr.IsOneWay);
    }

    [Fact]
    public void Constructor_WithMethodName_SetsMethodName()
    {
        var attr = new RpcMethodAttribute("DoSomething");
        Assert.Equal("DoSomething", attr.MethodName);
        Assert.False(attr.IsOneWay);
    }

    [Fact]
    public void IsOneWay_CanBeSet()
    {
        var attr = new RpcMethodAttribute("FireAndForget") { IsOneWay = true };
        Assert.True(attr.IsOneWay);
    }

    [Fact]
    public void IsOneWay_DefaultsToFalse()
    {
        var attr = new RpcMethodAttribute();
        Assert.False(attr.IsOneWay);
    }

    [Fact]
    public void AllowMultiple_IsFalse()
    {
        var attr = typeof(RpcMethodAttribute);
        var usage = (AttributeUsageAttribute)attr.GetCustomAttributes(typeof(AttributeUsageAttribute), false)[0];
        Assert.False(usage.AllowMultiple);
    }
}