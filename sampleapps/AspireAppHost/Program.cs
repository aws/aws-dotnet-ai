using AWS.AgentCore.Testing;

var builder = DistributedApplication.CreateBuilder(args);

var agent = builder.AddAgentCoreRuntime<Projects.AnnotationsSample>()
    .WithInMemory();

builder.AddProject<Projects.ChatBotUI>("ChatBotUI")
    .WithReference(agent);

builder.AddAgentCoreRuntime<Projects.AnnotationsStreamingAgent>()
    .WithStreaming()
    .WithInMemory();

builder.AddAgentCoreRuntime<Projects.MicrosoftAgentFrameworkSample>()
    .WithInMemory();

builder.AddAgentCoreRuntime<Projects.NativeAotAnnotations>()
    .WithInMemory();

builder.AddAgentCoreRuntime<Projects.NativeAotExtensions>()
    .WithInMemory();

builder.AddAgentCoreRuntime<Projects.StreamingAgent>()
    .WithStreaming()
    .WithInMemory();

builder.AddAgentCoreRuntime<Projects.RemoteMcpAgent>()
    .WithInMemory();

builder.Build().Run();
