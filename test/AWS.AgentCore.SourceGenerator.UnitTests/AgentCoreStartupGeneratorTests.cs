// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

namespace AWS.AgentCore.SourceGenerator.UnitTests;

public class AgentCoreStartupGeneratorTests
{
    [Fact]
    public void Generator_WithStartupAndAgent_EmitsProgram()
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

        Assert.NotNull(result.GeneratedSource);
        Assert.Contains("var builder = WebApplication.CreateBuilder(args);", result.GeneratedSource);
        Assert.Contains("var startup = new TestApp.Startup();", result.GeneratedSource);
        Assert.Contains("startup.ConfigureServices(builder);", result.GeneratedSource);
        Assert.Contains("builder.Services.AddTransient<TestApp.MyAgent>();", result.GeneratedSource);
        Assert.Contains("app.MapAgentCore<TestApp.PromptRequest>(", result.GeneratedSource);
        Assert.Contains("agent.Handle(request, context, ct)", result.GeneratedSource);
    }

    [Fact]
    public void Generator_WithPingHandler_EmitsPingDelegate()
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

        Assert.NotNull(result.GeneratedSource);
        Assert.Contains("pingHandler:", result.GeneratedSource);
        Assert.Contains("agent.Ping()", result.GeneratedSource);
    }

    [Fact]
    public void Generator_WithoutStartup_SkipsConfigureServices()
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

        Assert.NotNull(result.GeneratedSource);
        Assert.DoesNotContain("ConfigureServices", result.GeneratedSource);
        Assert.Contains("builder.Services.AddTransient<TestApp.MyAgent>();", result.GeneratedSource);
    }

    [Fact]
    public void Generator_WithoutInvocation_EmitsNothing()
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
    public void Generator_StreamingHandler_DetectsReturnType()
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

        Assert.NotNull(result.GeneratedSource);
        Assert.Contains("app.MapAgentCore<TestApp.PromptRequest>(", result.GeneratedSource);
        Assert.Contains("agent.Handle(request, ct)", result.GeneratedSource);
    }

    [Fact]
    public void Generator_RequestOnlyParameter_IdentifiesRequestType()
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

        Assert.NotNull(result.GeneratedSource);
        Assert.Contains("app.MapAgentCore<TestApp.MyRequest>(", result.GeneratedSource);
    }

    [Fact]
    public void Generator_GlobalNamespace_HandlesCorrectly()
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

        Assert.NotNull(result.GeneratedSource);
        Assert.Contains("builder.Services.AddTransient<MyAgent>();", result.GeneratedSource);
        Assert.Contains("app.MapAgentCore<PromptRequest>(", result.GeneratedSource);
    }
}
