// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace AWS.AgentCore.SourceGenerator.UnitTests;

/// <summary>
/// Helper for running the source generator against in-memory source code and inspecting the output.
/// </summary>
internal static class GeneratorTestHelper
{
    /// <summary>
    /// Runs the <see cref="AgentCoreStartupGenerator"/> against the given source code and returns
    /// the generated output (or null if nothing was generated).
    /// </summary>
    internal static GeneratorResult RunGenerator(string source)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source);

        // Add references needed for the attributes to resolve
        var references = new List<MetadataReference>
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(Attribute).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(System.Threading.CancellationToken).Assembly.Location),
        };

        // Add System.Runtime for core types
        var runtimeDir = System.IO.Path.GetDirectoryName(typeof(object).Assembly.Location)!;
        references.Add(MetadataReference.CreateFromFile(System.IO.Path.Combine(runtimeDir, "System.Runtime.dll")));
        references.Add(MetadataReference.CreateFromFile(System.IO.Path.Combine(runtimeDir, "System.Collections.dll")));

        // Add our attribute types from the runtime library
        // We create stub types since we can't reference the full ASP.NET assembly in a unit test
        var attributeStubs = CSharpSyntaxTree.ParseText(@"
namespace AWS.AgentCore
{
    [System.AttributeUsage(System.AttributeTargets.Class)]
    public class AgentCoreStartupAttribute : System.Attribute { }

    [System.AttributeUsage(System.AttributeTargets.Method)]
    public class AgentCoreHandlerAttribute : System.Attribute { }

    [System.AttributeUsage(System.AttributeTargets.Method)]
    public class AgentCorePingAttribute : System.Attribute { }

    public class AgentCoreRuntimeContext { }
}

namespace Microsoft.AspNetCore.Builder
{
    public class WebApplicationBuilder { }
}
");

        var compilation = CSharpCompilation.Create(
            "TestAssembly",
            new[] { syntaxTree, attributeStubs },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var generator = new AgentCoreStartupGenerator();
        GeneratorDriver driver = CSharpGeneratorDriver.Create(generator);
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out var diagnostics);

        var runResult = driver.GetRunResult();
        var generatedSource = runResult.GeneratedTrees
            .FirstOrDefault(t => t.FilePath.Contains("AgentCore_GeneratedProgram"))
            ?.GetText()
            .ToString();

        return new GeneratorResult
        {
            GeneratedSource = generatedSource,
            Diagnostics = diagnostics
        };
    }
}

internal class GeneratorResult
{
    public string? GeneratedSource { get; set; }
    public System.Collections.Immutable.ImmutableArray<Diagnostic> Diagnostics { get; set; }
}
