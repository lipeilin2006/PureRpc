using System;

namespace PureRpc;

[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class AllowAnonymousAttribute : Attribute { }
