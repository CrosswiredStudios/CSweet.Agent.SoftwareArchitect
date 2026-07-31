using CSweet.Agent.SDK;
using CSweet.Agents.SoftwareArchitect;
using Microsoft.Extensions.Hosting;

if (args.Contains("--self-test", StringComparer.Ordinal))
{
    var agent = new SoftwareArchitectAgent(new SelfTestDesignGenerator());
    var result = await new AgentTestRuntime().ExecuteCapabilityAsync(
        agent,
        SoftwareArchitectProfile.DesignCapability,
        new ArchitectureDesignRequest(
            Guid.NewGuid(),
            "Deliver one approved product behavior.",
            ["The behavior must be maintainable."],
            ["The end-to-end scenario passes."],
            "self-test"));
    Console.WriteLine(result.Value);
    Environment.ExitCode = result.Succeeded ? 0 : 1;
    return;
}

var builder = Host.CreateApplicationBuilder(args);
builder.AddCSweetAgent<SoftwareArchitectAgent>();
await builder.Build().RunAsync();

file sealed class SelfTestDesignGenerator : IArchitectureDesignGenerator
{
    public Task<ArchitecturePlan> GenerateAsync(
        ArchitectureDesignRequest request,
        AgentRuntimeContext context,
        AgentSettings settings,
        CancellationToken cancellationToken) =>
        Task.FromResult(ArchitecturePlanSamples.MinimalValidPlan());
}
