using System;

namespace PureRpc;

[AttributeUsage(AttributeTargets.Interface | AttributeTargets.Method, AllowMultiple = true, Inherited = true)]
public sealed class AuthorizeAttribute : Attribute
{
    public string? Policy { get; }
    public string? Roles { get; init; }

    public AuthorizeAttribute() { }

    public AuthorizeAttribute(string policy) => Policy = policy;
}
