using CSweet.Agent.SDK;
using CSweet.Agents.SoftwareArchitect;
using Microsoft.Extensions.Hosting;

if (args.Contains("--self-test", StringComparer.Ordinal))
{
    var agent = new SoftwareArchitectAgent(new SelfTestDesignGenerator());
    var runtime = new AgentTestRuntime().RegisterCapability<TeamRosterRequest, TeamRosterResponse>(
        PlatformCapabilities.TeamRosterRead,
        (_, _) => Task.FromResult(new TeamRosterResponse(new AgentTeamContext(
            Guid.NewGuid().ToString("D"), "self-test", "Self Test", 1,
            Guid.NewGuid().ToString("D"), "Architect",
            [
                new AgentTeammate(Guid.NewGuid().ToString("D"), "Developer", "Human", null,
                    "Software Developer", "Peer", "Active"),
                new AgentTeammate(Guid.NewGuid().ToString("D"), "QA", "Agent", null,
                    "Software QA", "Peer", "Active")
            ], [], 2, false))));
    var result = await runtime.ExecuteCapabilityAsync(
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
        ArchitectureDeliveryProfile deliveryProfile,
        AgentRuntimeContext context,
        AgentSettings settings,
        CancellationToken cancellationToken) =>
        Task.FromResult(ArchitecturePlanSamples.MinimalValidPlan());
}
