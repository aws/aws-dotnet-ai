using AWS.AgentCore;
using AWS.AgentCore.Extensions;

namespace MeetingJudgeAgent;

[AgentCoreStartup]
public class Startup
{
    public void ConfigureServices(WebApplicationBuilder builder)
    {
        builder.AddAgentCore(options =>
        {
            options.ModelId = "global.anthropic.claude-opus-4-7";
        });
    }
}
