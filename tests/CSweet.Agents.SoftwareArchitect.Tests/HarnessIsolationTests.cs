using CSweet.Agent.SDK;
using Microsoft.Extensions.AI;

namespace CSweet.Agents.SoftwareArchitect.Tests;

public sealed class HarnessIsolationTests
{
    [Fact]
    public void HarnessKeepsTodoAndCompactionButExposesOnlyCuratedReadToolsAndSubmission()
    {
        var request = new ArchitectureDesignRequest(
            Guid.NewGuid(),
            "Design the approved behavior.",
            ["Keep it maintainable."],
            ["The scenario passes."],
            "harness-test");
        var options = ArchitectureDesignHarness.CreateOptions(
            request,
            new AgentTestRuntime().CreateContext(),
            new ArchitecturePlanCapture());

        Assert.Equal(SoftwareArchitectProfile.MaximumIterationsPerRequest, options.MaximumIterationsPerRequest);
        Assert.False(options.DisableTodoProvider);
        Assert.True(options.DisableAgentModeProvider);
        Assert.True(options.DisableAgentSkillsProvider);
        Assert.True(options.DisableFileMemory);
        Assert.True(options.DisableToolAutoApproval);
        Assert.True(options.DisableWebSearch);
#pragma warning disable MAAI001
        Assert.Null(options.FileAccessStore);
        Assert.Null(options.BackgroundAgents);
#pragma warning restore MAAI001
        Assert.False(options.DisableOpenTelemetry);
#pragma warning disable MAAI001
        Assert.Equal(SoftwareArchitectProfile.DefaultContextWindowTokens, options.MaxContextWindowTokens);
        Assert.Equal(SoftwareArchitectProfile.DefaultOutputTokens, options.MaxOutputTokens);
#pragma warning restore MAAI001

        var names = options.ChatOptions!.Tools!
            .OfType<AIFunctionDeclaration>()
            .Select(x => x.Name)
            .ToArray();
        Assert.Contains("read_business_context", names);
        Assert.Contains("read_organization_context", names);
        Assert.Contains("read_team_roster", names);
        Assert.Contains("read_work_board", names);
        Assert.Contains("read_work_item", names);
        Assert.Contains("read_work_sprints", names);
        Assert.Contains("read_sprint_report", names);
        Assert.Contains("submit_architecture_plan", names);
        Assert.DoesNotContain(names, name =>
            name.Contains("create", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("publish", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("estimate", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("scope", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("shell", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("file", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void CaptureRejectsInvalidPlanAndAcceptsOnlyOneValidPlan()
    {
        var capture = new ArchitecturePlanCapture();
        var invalid = capture.Submit(
            ArchitecturePlanSamples.MinimalValidPlan() with { Components = [] });
        var valid = capture.Submit(ArchitecturePlanSamples.MinimalValidPlan());
        var duplicate = capture.Submit(ArchitecturePlanSamples.MinimalValidPlan());

        Assert.False(invalid.Accepted);
        Assert.True(valid.Accepted);
        Assert.False(duplicate.Accepted);
    }
}
