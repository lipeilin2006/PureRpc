using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace PureRpc.Generator;

[Generator]
public class PureRpcGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var serviceDeclarations = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: (s, _) => s is InterfaceDeclarationSyntax { AttributeLists.Count: > 0 },
                transform: (ctx, _) => GetServiceMetadata(ctx))
            .Where(m => m is not null);

        context.RegisterSourceOutput(serviceDeclarations, (spc, metadata) =>
        {
            if (metadata == null) return;
            // 生成器运行在编译期，StringBuilder/SourceText 的分配不会进入业务运行时热路径。
            // 改进建议：若接口数量很多，可继续保持增量生成输入足够精细，避免无关文件变更触发大量重新生成。
            spc.AddSource($"{metadata.InterfaceName}.g.cs", SourceText.From(GenerateSource(metadata), Encoding.UTF8));
        });
    }

    private ServiceMetadata? GetServiceMetadata(GeneratorSyntaxContext ctx)
    {
        if (ctx.Node is not InterfaceDeclarationSyntax interfaceDecl) return null;
        var symbol = ctx.SemanticModel.GetDeclaredSymbol(interfaceDecl) as INamedTypeSymbol;
        if (symbol == null) return null;

        // 匹配简化后的属性名 RpcServiceAttribute
        var serviceAttr = symbol.GetAttributes().FirstOrDefault(a => a.AttributeClass?.Name == "RpcServiceAttribute");
        if (serviceAttr == null) return null;

        var metadata = new ServiceMetadata
        {
            Namespace = symbol.ContainingNamespace.ToDisplayString(),
            InterfaceName = symbol.Name,
            ServiceName = serviceAttr.ConstructorArguments.FirstOrDefault().Value?.ToString() ?? symbol.Name,
            Methods = new List<MethodMetadata>()
        };

        // Read service-level [Authorize] (multiple allowed)
        var serviceAuthAttrs = symbol.GetAttributes().Where(a => a.AttributeClass?.Name == "AuthorizeAttribute");
        foreach (var authAttr in serviceAuthAttrs)
        {
            string? roles = null;
            foreach (var namedArg in authAttr.NamedArguments)
            {
                if (namedArg.Key == "Roles")
                    roles = namedArg.Value.Value?.ToString();
            }
            metadata.AuthorizeEntries.Add(new AuthEntry
            {
                Policy = authAttr.ConstructorArguments.FirstOrDefault().Value?.ToString(),
                Roles = roles
            });
        }

        foreach (var member in symbol.GetMembers().OfType<IMethodSymbol>())
        {
            // 匹配简化后的属性名 RpcMethodAttribute
            var methodAttr = member.GetAttributes().FirstOrDefault(a => a.AttributeClass?.Name == "RpcMethodAttribute");
            if (methodAttr == null) continue;

            var attrMethodName = methodAttr.ConstructorArguments.FirstOrDefault().Value?.ToString();
            var protocolMethodName = string.IsNullOrEmpty(attrMethodName) ? member.Name : attrMethodName;

            // Read IsOneWay from named argument
            bool isOneWay = false;
            foreach (var namedArg in methodAttr.NamedArguments)
            {
                if (namedArg.Key == "IsOneWay" && namedArg.Value.Value is bool b)
                {
                    isOneWay = b;
                }
            }

            var payloadParam = member.Parameters.FirstOrDefault(p =>
                p.Type.ToDisplayString() != "System.Threading.CancellationToken" &&
                p.Type.ToDisplayString() != "CancellationToken");

            bool hasCt = member.Parameters.Any(p =>
                p.Type.ToDisplayString() == "System.Threading.CancellationToken" ||
                p.Type.ToDisplayString() == "CancellationToken");

            bool isVoid = member.ReturnType.ToDisplayString() == "System.Threading.Tasks.Task" ||
                          member.ReturnType.ToDisplayString() == "System.Threading.Tasks.ValueTask" ||
                          member.ReturnType.ToDisplayString() == "void";

            string responseType = "object";
            bool isRpcResult = false;
            if (member.ReturnType is INamedTypeSymbol namedType && namedType.IsGenericType)
            {
                var typeArg = namedType.TypeArguments[0];
                var typeArgName = typeArg.ToDisplayString();

                // Detect RpcResult<T> — unwrap to inner type for serialization
                if (typeArg is INamedTypeSymbol resultType &&
                    resultType.Name == "RpcResult" &&
                    resultType.IsGenericType)
                {
                    isRpcResult = true;
                    responseType = resultType.TypeArguments[0].ToDisplayString();
                }
                else if (typeArg is INamedTypeSymbol resultType2 &&
                         resultType2.Name == "RpcResult" &&
                         !resultType2.IsGenericType)
                {
                    isRpcResult = true;
                    isVoid = true;
                    responseType = "object";
                }
                else
                {
                    responseType = typeArgName;
                }
            }

            // Read method-level [Authorize] (multiple) and [AllowAnonymous]
            var methodAuthAttrs = member.GetAttributes().Where(a => a.AttributeClass?.Name == "AuthorizeAttribute");
            var allowAnonAttr = member.GetAttributes().FirstOrDefault(a => a.AttributeClass?.Name == "AllowAnonymousAttribute");

            var methodAuthEntries = new List<AuthEntry>();
            foreach (var ma in methodAuthAttrs)
            {
                string? roles = null;
                foreach (var namedArg in ma.NamedArguments)
                {
                    if (namedArg.Key == "Roles")
                        roles = namedArg.Value.Value?.ToString();
                }
                methodAuthEntries.Add(new AuthEntry
                {
                    Policy = ma.ConstructorArguments.FirstOrDefault().Value?.ToString(),
                    Roles = roles
                });
            }

            metadata.Methods.Add(new MethodMetadata
            {
                ProtocolMethodName = protocolMethodName!,
                SourceMethodName = member.Name,
                FullReturnType = member.ReturnType.ToDisplayString(),
                ResponseType = responseType,
                RequestType = payloadParam?.Type.ToDisplayString() ?? "bool",
                IsOneWay = isOneWay,
                IsRpcResult = isRpcResult,
                IsVoid = isVoid,
                HasPayload = payloadParam != null,
                HasCancellationToken = hasCt,
                AuthorizeEntries = methodAuthEntries,
                AllowAnonymous = allowAnonAttr != null
            });
        }
        return metadata;
    }

    private string GenerateSource(ServiceMetadata m)
    {
        var proxyName = $"{m.InterfaceName}Proxy";
        var dispatcherName = $"{m.InterfaceName}Dispatcher";
        var serviceShortName = m.InterfaceName.StartsWith("I") ? m.InterfaceName.Substring(1) : m.InterfaceName;

        // 编译期拼接生成代码使用 StringBuilder，避免大量字符串 + 带来的中间字符串分配。
        // 这些分配发生在编译期，不影响 RPC 调用时延。
        var sb = new StringBuilder();
        sb.AppendLine($$"""
// <auto-generated/>
#pragma warning disable CS1591
using System;
using System.Buffers;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using PureRpc;
using PureRpc.Abstractions;

namespace {{m.Namespace}}
{
    internal sealed class {{proxyName}} : {{m.InterfaceName}}
    {
        private readonly IRpcClient _client;
        private readonly ISerializer _serializer;

        public {{proxyName}}(IRpcClient client, ISerializer serializer) 
        {
            _client = client;
            _serializer = serializer;
        }

""");

        foreach (var method in m.Methods)
        {
            var args = new List<string>();
            if (method.HasPayload) args.Add($"{method.RequestType} request");
            if (method.HasCancellationToken) args.Add("CancellationToken ct = default");
            string paramsStr = string.Join(", ", args);

            sb.AppendLine($$"""
        public async {{method.FullReturnType}} {{method.SourceMethodName}}({{paramsStr}})
        {
            var argWriter = new ArrayBufferWriter<byte>(128);
            {{(method.HasPayload ? "_serializer.Serialize(argWriter, request);" : "// No payload")}}

            {{(method.IsOneWay
                ? $@"// IsOneWay: fire-and-forget, no response expected
            _ = _client.CallAsync(""{m.ServiceName}"", ""{method.ProtocolMethodName}"", 
                new ReadOnlySequence<byte>(argWriter.WrittenMemory), {(method.HasCancellationToken ? "ct" : "default")});
            {(method.IsVoid ? "return;" : "return default;")}"
                : $@"var responseBytes = await _client.CallAsync(""{m.ServiceName}"", ""{method.ProtocolMethodName}"", 
                new ReadOnlySequence<byte>(argWriter.WrittenMemory), {(method.HasCancellationToken ? "ct" : "default")});
            
            {(method.IsVoid ? "return;" : $"return _serializer.Deserialize<{method.ResponseType}>(responseBytes);")}")}}
        }
""");
        }

        bool hasAnyAuth = m.AuthorizeEntries.Count > 0 || m.Methods.Any(mtd => mtd.AuthorizeEntries.Count > 0 || mtd.AllowAnonymous);
        bool hasMethodOverrides = hasAnyAuth && m.Methods.Any(mtd => mtd.AuthorizeEntries.Count > 0 || mtd.AllowAnonymous);
        bool hasServiceAuth = m.AuthorizeEntries.Count > 0;

        // Helper to produce a C# string literal (or "null")
        static string Lit(string? s) => s != null ? $"\"{s.Replace("\\", "\\\\").Replace("\"", "\\\"")}\"" : "null";

        // Emit multiple auth handler calls for a list of entries
        static void EmitAuthCalls(StringBuilder sb, List<AuthEntry> entries, bool ifGuard = false)
        {
            if (entries.Count == 0) return;
            if (ifGuard) sb.AppendLine("                if (__methodAuth)");
            foreach (var entry in entries)
            {
                sb.AppendLine($"                    await _authHandler.AuthorizeAsync(context, {Lit(entry.Policy)}, {Lit(entry.Roles)}, context.CancellationToken);");
            }
        }

        sb.AppendLine($$"""
    }

    internal sealed class {{dispatcherName}} : IServiceDispatcher
    {
        public string ServiceName => "{{m.ServiceName}}";
        
        private readonly {{m.InterfaceName}} _service;
        private readonly ISerializer _serializer;
""");

        if (hasAnyAuth)
        {
            sb.AppendLine("        private readonly IAuthorizationHandler? _authHandler;");
        }

        sb.Append($"        public {dispatcherName}({m.InterfaceName} service, ISerializer serializer");

        if (hasAnyAuth)
        {
            sb.Append(", IAuthorizationHandler? authHandler = null");
        }

        sb.AppendLine(")");
        sb.AppendLine("        {");
        sb.AppendLine("            _service = service;");
        sb.AppendLine("            _serializer = serializer;");

        if (hasAnyAuth)
        {
            sb.AppendLine("            _authHandler = authHandler;");
        }

        sb.AppendLine($$"""
        }

        public async ValueTask DispatchAsync(string methodName, ReadOnlySequence<byte> payload, RpcContext context)
        {
            var __baseService = _service as ServiceBase;
            if (__baseService != null) __baseService.Context = context;

            try
            {
""");

        // Auth check code (before the main dispatch switch)
        if (hasAnyAuth)
        {
            if (hasMethodOverrides)
            {
                // --- Per-method auth resolution via switch ---
                // Service-level [Authorize] and method-level [Authorize] are COMBINED (AND logic),
                // matching ASP.NET Core behavior: method-level auth ADDS to service-level auth.
                // [AllowAnonymous] on a method skips all auth.
                // Multiple [Authorize] attributes each produce a separate AuthorizeAsync call.
                sb.AppendLine("                bool __skipAuth = false;");
                sb.AppendLine("                bool __methodAuth = false;");
                sb.AppendLine("                switch (methodName)");
                sb.AppendLine("                {");
                foreach (var method in m.Methods)
                {
                    bool hasMethodAuth = method.AuthorizeEntries.Count > 0;
                    if (!hasMethodAuth && !method.AllowAnonymous) continue;
                    if (method.AllowAnonymous)
                    {
                        sb.AppendLine($"                    case \"{method.ProtocolMethodName}\":");
                        sb.AppendLine("                        __skipAuth = true;");
                        sb.AppendLine("                        break;");
                    }
                    else
                    {
                        sb.AppendLine($"                    case \"{method.ProtocolMethodName}\":");
                        sb.AppendLine("                        __methodAuth = true;");
                        sb.AppendLine("                        break;");
                    }
                }
                sb.AppendLine("                    default:");
                sb.AppendLine("                        break;");
                sb.AppendLine("                }");
                sb.AppendLine($"                if (!__skipAuth && (__methodAuth || {(hasServiceAuth ? "true" : "false")}))");
                sb.AppendLine("                {");
                sb.AppendLine("                    if (_authHandler == null)");
                sb.AppendLine("                        throw new InvalidOperationException(\"IAuthorizationHandler is not registered.\");");
                // Service-level auth: iterate over all entries
                if (hasServiceAuth)
                {
                    EmitAuthCalls(sb, m.AuthorizeEntries);
                }
                // Method-level auth
                sb.AppendLine("                    if (__methodAuth)");
                sb.AppendLine("                    {");
                // Emit method-level auth calls - we need the entries from the matching method
                // We emit a separate switch for method-level auth entries
                sb.AppendLine("                        switch (methodName)");
                sb.AppendLine("                        {");
                foreach (var method in m.Methods)
                {
                    if (method.AuthorizeEntries.Count == 0) continue;
                    sb.AppendLine($"                            case \"{method.ProtocolMethodName}\":");
                    foreach (var entry in method.AuthorizeEntries)
                    {
                        sb.AppendLine($"                                await _authHandler.AuthorizeAsync(context, {Lit(entry.Policy)}, {Lit(entry.Roles)}, context.CancellationToken);");
                    }
                    sb.AppendLine("                                break;");
                }
                sb.AppendLine("                            default:");
                sb.AppendLine("                                break;");
                sb.AppendLine("                        }");
                sb.AppendLine("                    }");
                sb.AppendLine("                }");
            }
            else
            {
                // Simple service-level auth (no per-method overrides)
                sb.AppendLine("                if (_authHandler == null)");
                sb.AppendLine("                    throw new InvalidOperationException(\"IAuthorizationHandler is not registered.\");");
                foreach (var entry in m.AuthorizeEntries)
                {
                    sb.AppendLine($"                await _authHandler.AuthorizeAsync(context, {Lit(entry.Policy)}, {Lit(entry.Roles)}, context.CancellationToken);");
                }
            }
        }

        sb.AppendLine("                switch (methodName)");
        sb.AppendLine("                {");

        foreach (var method in m.Methods)
        {
            var callArgsList = new List<string>();
            if (method.HasPayload) callArgsList.Add("req");
            if (method.HasCancellationToken) callArgsList.Add("context.CancellationToken");
            string callParamsStr = string.Join(", ", callArgsList);

            sb.AppendLine($$"""
                    case "{{method.ProtocolMethodName}}":
                        {
                            {{(method.HasPayload ? $"// 反序列化会分配请求 DTO；可通过紧凑 DTO/值类型字段减少热路径对象数量。\n                            var req = _serializer.Deserialize<{method.RequestType}>(payload);" : "")}}
                            {{(method.IsVoid ? "" : "var res = ")}}await _service.{{method.SourceMethodName}}({{callParamsStr}});
                            {{(method.IsOneWay ? "// IsOneWay: no response sent" : (method.IsVoid ? "" : "// 响应直接写入 RpcContext 的池化缓冲，避免额外 byte[] 中转。\n                            _serializer.Serialize(context.ResponseBuffer, res);"))}}
                            break;
                        }
""");
        }

        sb.AppendLine($$"""
                    default:
                        throw new RpcException($"Method '{methodName}' not found in service {{m.ServiceName}}");
                }
            }
            finally
            {
                if (__baseService != null) __baseService.Context = null!;
            }
        }
    }

    public static partial class {{m.InterfaceName}}Extensions
    {
        public static IServerBuilder With{{serviceShortName}}<TImplementation>(this IServerBuilder builder)
            where TImplementation : class, {{m.InterfaceName}}
        {
            builder.Services.AddSingleton<{{m.InterfaceName}}, TImplementation>();
            builder.Services.AddSingleton<IServiceDispatcher>(sp => 
            {
                var impl = sp.GetRequiredService<{{m.InterfaceName}}>();
                var serializer = sp.GetRequiredService<ISerializer>();
""");

        if (hasAnyAuth)
        {
            sb.AppendLine("                var authHandler = sp.GetService<IAuthorizationHandler>();");
        }

        sb.Append($"                return new {dispatcherName}(impl, serializer");
        if (hasAnyAuth) sb.Append(", authHandler");
        sb.AppendLine(");");
        sb.AppendLine("            });");
        sb.AppendLine("            return builder;");
        sb.AppendLine("        }");

        sb.AppendLine($$"""

        public static IClientBuilder With{{serviceShortName}}Proxy(this IClientBuilder builder)
        {
            builder.Services.AddSingleton<{{m.InterfaceName}}>(sp => 
            {
                var client = sp.GetRequiredService<IRpcClient>();
                var serializer = sp.GetRequiredService<ISerializer>();
                return new {{proxyName}}(client, serializer);
            });
            return builder;
        }
    }
}
""");

        return sb.ToString();
    }

    private class ServiceMetadata
    {
        public string Namespace = null!;
        public string InterfaceName = null!;
        public string ServiceName = null!;
        public List<MethodMetadata> Methods = null!;
        public List<AuthEntry> AuthorizeEntries = new();
    }

    private class MethodMetadata
    {
        public string ProtocolMethodName = null!;
        public string SourceMethodName = null!;
        public string FullReturnType = null!;
        public string ResponseType = null!;
        public string RequestType = null!;
        public bool IsVoid;
        public bool IsOneWay;
        public bool IsRpcResult;
        public bool HasPayload;
        public bool HasCancellationToken;
        public List<AuthEntry> AuthorizeEntries = new();
        public bool AllowAnonymous;
    }

    private class AuthEntry
    {
        public string? Policy;
        public string? Roles;
    }
}
