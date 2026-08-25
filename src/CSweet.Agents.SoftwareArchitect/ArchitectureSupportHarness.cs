using System.Text.Json;
using CSweet.Agent.SDK;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace CSweet.Agents.SoftwareArchitect;

internal sealed class ArchitectureSupportHarness(IAgentLlmClientFactory? llmClientFactory = null)
{
    public async Task<SoftwareArchitectureGuidance> GenerateAsync(
        AgentCoordinationWorkSource source,
        SoftwareDevelopmentSupportRequest support,
        AgentRuntimeContext context,
        AgentSettings settings,
        CancellationToken cancellationToken)
    {
        var provider = settings.GetGuid("llmProviderId")
            ?? throw new ArchitectureDesignException("Configure an approved LLM provider for Developer guidance.");
        var model = settings.GetString("llmModel");
        if (string.IsNullOrWhiteSpace(model))
            throw new ArchitectureDesignException("Configure an approved model for Developer guidance.");
        var item = await context.Platform.Work.ReadItemAsync(
            new(source.BoardId, source.ItemId), cancellationToken);
        var comments = await context.Platform.Work.ReadCommentsAsync(
            new(source.BoardId, source.ItemId), cancellationToken);
        var execution = await context.Platform.Work.ReadOrchestrationAsync(
            new(source.BoardId, SprintExecutionId: source.SprintExecutionId), cancellationToken);

        var capture = new GuidanceCapture();
        var tools = (await context.Platform.GetModelToolsAsync(
            [WorkItemCapabilities.Read, WorkItemCapabilities.ReadComments, WorkOrchestrationCapabilities.Read],
            cancellationToken)).ToList();
        tools.Add(AIFunctionFactory.Create(
            (SoftwareArchitectureGuidance guidance) => capture.Submit(guidance),
            "submit_architecture_guidance",
            "Submit bounded technical guidance. This does not mutate work or retry execution."));
        var options = new HarnessAgentOptions
        {
            Id = SoftwareArchitectProfile.AgentId,
            Name = context.Identity?.DisplayName ?? SoftwareArchitectProfile.DisplayName,
            Description = "Diagnoses one exact Developer blocker against the approved architecture.",
            MaximumIterationsPerRequest = 8,
            ChatOptions = new ChatOptions
            {
                Instructions = SoftwareArchitectProfile.DeveloperSupportInstructions + """

You are in the developer-guidance harness. Diagnose only the linked work item and approved design.
Never change product scope, budget, timing, risk acceptance, assignments, repository, or sprint state.
If safe guidance requires one of those changes, set requiresArchitectureApproval=true and explain why.
Call submit_architecture_guidance exactly once. Do not merely print the result.
""",
                Tools = tools
            },
            DisableAgentModeProvider = true,
            DisableAgentSkillsProvider = true,
            DisableFileMemory = true,
            DisableToolAutoApproval = true,
            DisableWebSearch = true
        };
        var selection = new AgentLlmSelection(provider, model);
        var chat = llmClientFactory is null
            ? context.CreateChatClient(selection)
            : await llmClientFactory.CreateChatClientAsync(selection, cancellationToken);
        var agent = chat.AsHarnessAgent(options);
        var session = await agent.CreateSessionAsync(cancellationToken);
        var bounded = JsonSerializer.Serialize(new
        {
            source,
            support,
            item = new { item.Id, item.Title, item.Description, item.Planning, item.Delivery },
            comments = comments.Items.Select(x => new { x.Kind, x.Body, x.ArtifactDigest }),
            execution
        }, IncrementalPlanningJson.Options);
        await foreach (var _ in agent.RunStreamingAsync(
            $"Diagnose this bounded technical failure and submit ordered guidance.\n<support_context>{bounded}</support_context>",
            session, options: null, cancellationToken)) { }
        return capture.Value ?? throw new ArchitectureDesignException(
            "The configured model did not submit typed Developer guidance.");
    }

    private sealed class GuidanceCapture
    {
        public SoftwareArchitectureGuidance? Value { get; private set; }
        public object Submit(SoftwareArchitectureGuidance guidance)
        {
            if (Value is not null)
                return new { accepted = false, message = "Guidance was already submitted." };
            if (string.IsNullOrWhiteSpace(guidance.Diagnosis) || guidance.OrderedNextSteps.Count == 0 ||
                guidance.Invariants.Count == 0 || guidance.Verification.Count == 0)
                return new { accepted = false, message = "Guidance is incomplete." };
            Value = guidance;
            return new { accepted = true };
        }
    }
}
