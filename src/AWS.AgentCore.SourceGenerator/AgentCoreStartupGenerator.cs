// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Immutable;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace AWS.AgentCore.SourceGenerator;

/// <summary>
/// Roslyn incremental source generator that emits a complete Program.cs for AgentCore agents.
/// Detects [AgentCoreStartup], [AgentCoreHandler], and [AgentCorePing] attributes and
/// generates the ASP.NET Minimal API plumbing that calls the runtime library's extension methods.
/// </summary>
[Generator]
public class AgentCoreStartupGenerator : IIncrementalGenerator
{
    private const string StartupAttributeName = "AWS.AgentCore.AgentCoreStartupAttribute";
    private const string HandlerAttributeName = "AWS.AgentCore.AgentCoreHandlerAttribute";
    private const string PingAttributeName = "AWS.AgentCore.AgentCorePingAttribute";

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // Find all classes with any of our attributes
        var classDeclarations = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: static (node, _) => IsCandidateClass(node),
                transform: static (ctx, _) => GetClassInfo(ctx))
            .Where(static info => info is not null);

        var compilationAndClasses = context.CompilationProvider.Combine(classDeclarations.Collect());

        context.RegisterSourceOutput(compilationAndClasses,
            static (spc, source) => Execute(spc, source.Left, source.Right!));
    }

    /// <summary>
    /// Syntactic filter: quickly check if a node is a class with attributes or contains
    /// methods with attributes. This runs on every keystroke so it must be fast.
    /// </summary>
    private static bool IsCandidateClass(SyntaxNode node)
    {
        if (node is not ClassDeclarationSyntax classDecl)
            return false;

        // Check if the class itself has attributes
        if (classDecl.AttributeLists.Count > 0)
            return true;

        // Check if any methods have attributes
        foreach (var member in classDecl.Members)
        {
            if (member is MethodDeclarationSyntax method && method.AttributeLists.Count > 0)
                return true;
        }

        return false;
    }

    /// <summary>
    /// Semantic analysis: extract class info from candidates that actually have our attributes.
    /// </summary>
    private static ClassInfo? GetClassInfo(GeneratorSyntaxContext context)
    {
        var classDecl = (ClassDeclarationSyntax)context.Node;
        var model = context.SemanticModel;
        var classSymbol = model.GetDeclaredSymbol(classDecl);
        if (classSymbol is null) return null;

        var info = new ClassInfo
        {
            ClassName = classSymbol.Name,
            Namespace = classSymbol.ContainingNamespace.IsGlobalNamespace
                ? null
                : classSymbol.ContainingNamespace.ToDisplayString()
        };

        // Check for [AgentCoreStartup]
        foreach (var attr in classSymbol.GetAttributes())
        {
            if (attr.AttributeClass?.ToDisplayString() == StartupAttributeName)
            {
                info.IsStartup = true;
            }
        }

        // Check methods for [AgentCoreHandler], [AgentCorePing], and ConfigureServices
        foreach (var member in classSymbol.GetMembers())
        {
            if (member is not IMethodSymbol method) continue;

            foreach (var attr in method.GetAttributes())
            {
                if (attr.AttributeClass?.ToDisplayString() == HandlerAttributeName)
                {
                    info.InvocationMethod = AnalyzeInvocationMethod(method);
                }
                else if (attr.AttributeClass?.ToDisplayString() == PingAttributeName)
                {
                    info.PingMethodName = method.Name;
                }
            }

            // Detect ConfigureServices(WebApplicationBuilder) by convention
            if (method.Name == "ConfigureServices" &&
                method.Parameters.Length == 1 &&
                method.Parameters[0].Type.ToDisplayString() == "Microsoft.AspNetCore.Builder.WebApplicationBuilder")
            {
                info.HasConfigureServices = true;
            }
        }

        // Only return if this class has something we care about
        if (!info.IsStartup && info.InvocationMethod is null && info.PingMethodName is null)
            return null;

        return info;
    }

    private static InvocationMethodInfo AnalyzeInvocationMethod(IMethodSymbol method)
    {
        var info = new InvocationMethodInfo
        {
            MethodName = method.Name
        };

        // Detect return type: Task<string> vs IAsyncEnumerable<string>
        var returnType = method.ReturnType;
        if (returnType is INamedTypeSymbol namedReturn)
        {
            if (namedReturn.OriginalDefinition.ToDisplayString() == "System.Collections.Generic.IAsyncEnumerable<T>" &&
                namedReturn.TypeArguments.Length == 1 &&
                namedReturn.TypeArguments[0].SpecialType == SpecialType.System_String)
            {
                info.IsStreaming = true;
            }
        }

        // Analyze parameters to find the request type
        foreach (var param in method.Parameters)
        {
            var paramType = param.Type.ToDisplayString();

            if (paramType == "System.Threading.CancellationToken")
                continue;
            if (paramType == "AWS.AgentCore.AgentCoreRuntimeContext")
                continue;

            // First non-special parameter is the request type
            if (info.RequestType is null)
            {
                info.RequestType = paramType;
                info.RequestParameterName = param.Name;
            }
        }

        // Build the parameter list for the delegate signature
        var delegateParams = new List<string>();
        var callArgs = new List<string>();

        foreach (var param in method.Parameters)
        {
            var paramType = param.Type.ToDisplayString();
            delegateParams.Add($"{paramType} {param.Name}");
            callArgs.Add(param.Name);
        }

        info.DelegateParameters = string.Join(", ", delegateParams);
        info.CallArguments = string.Join(", ", callArgs);

        return info;
    }

    private static void Execute(
        SourceProductionContext context,
        Compilation compilation,
        ImmutableArray<ClassInfo?> classes)
    {
        var allClasses = classes.Where(c => c is not null).Cast<ClassInfo>().ToList();
        if (allClasses.Count == 0) return;

        var startupClass = allClasses.FirstOrDefault(c => c.IsStartup);
        var agentClass = allClasses.FirstOrDefault(c => c.InvocationMethod is not null);

        if (agentClass?.InvocationMethod is null) return;

        var source = GenerateProgramSource(startupClass, agentClass);
        context.AddSource("AgentCore_GeneratedProgram.g.cs", SourceText.From(source, Encoding.UTF8));
    }

    private static string GenerateProgramSource(ClassInfo? startupClass, ClassInfo agentClass)
    {
        var sb = new StringBuilder();
        var invocation = agentClass.InvocationMethod!;

        sb.AppendLine("// <auto-generated/>");
        sb.AppendLine("// Generated by AWS.AgentCore.SourceGenerator");
        sb.AppendLine();
        sb.AppendLine("using AWS.AgentCore;");
        sb.AppendLine("using AWS.AgentCore.Extensions;");
        sb.AppendLine("using Microsoft.Extensions.DependencyInjection;");
        sb.AppendLine();
        sb.AppendLine("var builder = WebApplication.CreateBuilder(args);");
        sb.AppendLine();

        // Call ConfigureServices if the startup class has it
        if (startupClass is not null && startupClass.HasConfigureServices)
        {
            var startupFullName = startupClass.Namespace is not null
                ? $"{startupClass.Namespace}.{startupClass.ClassName}"
                : startupClass.ClassName;

            sb.AppendLine($"var startup = new {startupFullName}();");
            sb.AppendLine("startup.ConfigureServices(builder);");
            sb.AppendLine();
        }

        // Register the agent class in DI
        var agentFullName = agentClass.Namespace is not null
            ? $"{agentClass.Namespace}.{agentClass.ClassName}"
            : agentClass.ClassName;

        sb.AppendLine($"builder.Services.AddTransient<{agentFullName}>();");
        sb.AppendLine();
        sb.AppendLine("var app = builder.Build();");
        sb.AppendLine();

        // Generate MapAgentCore call
        var requestType = invocation.RequestType ?? "object";

        sb.AppendLine($"app.MapAgentCore<{requestType}>(");

        // Handler delegate — resolve agent from DI and call the invocation method
        sb.AppendLine($"    handler: ({invocation.DelegateParameters}) =>");
        sb.AppendLine("    {");
        sb.AppendLine($"        var agent = app.Services.GetRequiredService<{agentFullName}>();");
        sb.AppendLine($"        return agent.{invocation.MethodName}({invocation.CallArguments});");
        sb.AppendLine("    }");

        // Ping handler if present
        if (agentClass.PingMethodName is not null)
        {
            sb.AppendLine($"    , pingHandler: () =>");
            sb.AppendLine("    {");
            sb.AppendLine($"        var agent = app.Services.GetRequiredService<{agentFullName}>();");
            sb.AppendLine($"        return agent.{agentClass.PingMethodName}();");
            sb.AppendLine("    }");
        }

        sb.AppendLine(");");
        sb.AppendLine();
        sb.AppendLine("app.Run();");

        return sb.ToString();
    }

    private class ClassInfo
    {
        public string ClassName { get; set; } = "";
        public string? Namespace { get; set; }
        public bool IsStartup { get; set; }
        public bool HasConfigureServices { get; set; }
        public InvocationMethodInfo? InvocationMethod { get; set; }
        public string? PingMethodName { get; set; }
    }

    private class InvocationMethodInfo
    {
        public string MethodName { get; set; } = "";
        public string? RequestType { get; set; }
        public string? RequestParameterName { get; set; }
        public bool IsStreaming { get; set; }
        public string DelegateParameters { get; set; } = "";
        public string CallArguments { get; set; } = "";
    }
}
