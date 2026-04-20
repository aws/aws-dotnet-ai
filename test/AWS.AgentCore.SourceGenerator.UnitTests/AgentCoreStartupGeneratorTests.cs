// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

namespace AWS.AgentCore.SourceGenerator.UnitTests;

public class AgentCoreStartupGeneratorTests
{
    [Fact]
    public async Task Generator_WithStartupAndAgent_EmitsProgram()
    {
        var source = @"
using AWS.AgentCore;
using System.Threading;
using System.Threading.Tasks;

namespace TestApp
{
    [AgentCoreStartup]
    public class Startup
    {
        public void ConfigureServices(Microsoft.AspNetCore.Builder.WebApplicationBuilder builder) { }
    }

    public record PromptRequest(string? Prompt);

    public class MyAgent
    {
        [AgentCoreHandler]
        public Task<string> Handle(PromptRequest request, AgentCoreRuntimeContext context, CancellationToken ct)
        {
            return Task.FromResult(""hello"");
        }
    }
}";

        var result = GeneratorTestHelper.RunGenerator(source);
        var expected = await ReadSnapshot("StartupAndAgent.g.cs");

        Assert.NotNull(result.GeneratedSource);
        Assert.Equal(expected, result.GeneratedSource);
    }

    [Fact]
    public async Task Generator_WithPingHandler_EmitsPingDelegate()
    {
        var source = @"
using AWS.AgentCore;
using System.Threading;
using System.Threading.Tasks;

namespace TestApp
{
    public record PromptRequest(string? Prompt);

    public class MyAgent
    {
        [AgentCoreHandler]
        public Task<string> Handle(PromptRequest request, CancellationToken ct)
        {
            return Task.FromResult(""hello"");
        }

        [AgentCorePing]
        public object Ping() => new { status = ""Healthy"" };
    }
}";

        var result = GeneratorTestHelper.RunGenerator(source);
        var expected = await ReadSnapshot("WithPingHandler.g.cs");

        Assert.NotNull(result.GeneratedSource);
        Assert.Equal(expected, result.GeneratedSource);
    }

    [Fact]
    public async Task Generator_WithoutStartup_SkipsConfigureServices()
    {
        var source = @"
using AWS.AgentCore;
using System.Threading;
using System.Threading.Tasks;

namespace TestApp
{
    public record PromptRequest(string? Prompt);

    public class MyAgent
    {
        [AgentCoreHandler]
        public Task<string> Handle(PromptRequest request, CancellationToken ct)
        {
            return Task.FromResult(""hello"");
        }
    }
}";

        var result = GeneratorTestHelper.RunGenerator(source);
        var expected = await ReadSnapshot("WithoutStartup.g.cs");

        Assert.NotNull(result.GeneratedSource);
        Assert.Equal(expected, result.GeneratedSource);
    }

    [Fact]
    public void Generator_WithoutHandler_EmitsNothing()
    {
        var source = @"
using AWS.AgentCore;

namespace TestApp
{
    [AgentCoreStartup]
    public class Startup
    {
        public void ConfigureServices(Microsoft.AspNetCore.Builder.WebApplicationBuilder builder) { }
    }
}";

        var result = GeneratorTestHelper.RunGenerator(source);

        Assert.Null(result.GeneratedSource);
    }

    [Fact]
    public async Task Generator_StreamingHandler_DetectsReturnType()
    {
        var source = @"
using AWS.AgentCore;
using System.Collections.Generic;
using System.Threading;

namespace TestApp
{
    public record PromptRequest(string? Prompt);

    public class MyAgent
    {
        [AgentCoreHandler]
        public IAsyncEnumerable<string> Handle(PromptRequest request, CancellationToken ct)
        {
            return null!;
        }
    }
}";

        var result = GeneratorTestHelper.RunGenerator(source);
        var expected = await ReadSnapshot("StreamingHandler.g.cs");

        Assert.NotNull(result.GeneratedSource);
        Assert.Equal(expected, result.GeneratedSource);
    }

    [Fact]
    public async Task Generator_RequestOnlyParameter_IdentifiesRequestType()
    {
        var source = @"
using AWS.AgentCore;
using System.Threading.Tasks;

namespace TestApp
{
    public record MyRequest(string? Input);

    public class MyAgent
    {
        [AgentCoreHandler]
        public Task<string> Handle(MyRequest request)
        {
            return Task.FromResult(""ok"");
        }
    }
}";

        var result = GeneratorTestHelper.RunGenerator(source);
        var expected = await ReadSnapshot("RequestOnly.g.cs");

        Assert.NotNull(result.GeneratedSource);
        Assert.Equal(expected, result.GeneratedSource);
    }

    [Fact]
    public async Task Generator_GlobalNamespace_HandlesCorrectly()
    {
        var source = @"
using AWS.AgentCore;
using System.Threading.Tasks;

public record PromptRequest(string? Prompt);

public class MyAgent
{
    [AgentCoreHandler]
    public Task<string> Handle(PromptRequest request)
    {
        return Task.FromResult(""ok"");
    }
}";

        var result = GeneratorTestHelper.RunGenerator(source);
        var expected = await ReadSnapshot("GlobalNamespace.g.cs");

        Assert.NotNull(result.GeneratedSource);
        Assert.Equal(expected, result.GeneratedSource);
    }

    private static async Task<string> ReadSnapshot(string fileName)
    {
        var path = Path.Combine("Snapshots", fileName);
        var content = await File.ReadAllTextAsync(path);
        return content.ReplaceLineEndings("\n");
    }
}
