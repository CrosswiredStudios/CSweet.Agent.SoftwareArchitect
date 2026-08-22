using CSweet.Agent.SDK;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace CSweet.Agents.SoftwareArchitect;

internal sealed class IncrementalArchitectureHarness(IAgentLlmClientFactory? llmClientFactory)
{
    public async Task<IncrementalStoryProposal> ProposeStoriesAsync(
        IncrementalProductBrief brief,
        AgentRuntimeContext context,
        AgentSettings settings,
        CancellationToken cancellationToken)
    {
        if (llmClientFactory is null)
            return FallbackStories(brief);
        var capture = new StoryCapture();
        await RunAsync(
            brief,
            capture,
            "submit_story_proposal",
            "Submit the bounded Story and planned-sprint proposal for only the current Epic.",
            "Propose the Stories, direct dependencies, risks, and planned sprint grouping for this one Epic. " +
            "Cover every supplied requirement without decomposing Tasks. Call submit_story_proposal exactly once.",
            Math.Min(4_000, settings.GetInt32("maxOutputTokens", SoftwareArchitectProfile.DefaultOutputTokens)),
            context,
            settings,
            cancellationToken);
        return capture.Value ?? throw new ArchitectureDesignException("The architecture model did not submit a Story proposal.");
    }

    public async Task<IncrementalTaskProposal> ProposeTasksAsync(
        IncrementalProductBrief brief,
        AgentRuntimeContext context,
        AgentSettings settings,
        CancellationToken cancellationToken)
    {
        if (brief.Story is null)
            throw new ArchitectureDesignException("A current Story is required for Task decomposition.");
        if (llmClientFactory is null)
            return FallbackTasks(brief);
        var capture = new TaskCapture(brief.Story.Key, brief.PageOrdinal);
        await RunAsync(
            brief,
            capture,
            "submit_task_page",
            "Submit no more than eight junior-ready Tasks for only the current Story and page.",
            "Decompose only this Story. Return at most eight Tasks on this page. Every Task must contain " +
            "implementation requirements, boundary, constraints, dependencies, edge cases, tests, objective evidence, " +
            "and definition of done. Set isFinalPage only when no additional Tasks are needed. Call submit_task_page exactly once.",
            Math.Min(8_000, settings.GetInt32("maxOutputTokens", SoftwareArchitectProfile.DefaultOutputTokens)),
            context,
            settings,
            cancellationToken);
        return capture.Value ?? throw new ArchitectureDesignException("The architecture model did not submit a Task page.");
    }

    private async Task RunAsync<TCapture>(
        IncrementalProductBrief brief,
        TCapture capture,
        string toolName,
        string toolDescription,
        string prompt,
        int outputTokens,
        AgentRuntimeContext context,
        AgentSettings settings,
        CancellationToken cancellationToken) where TCapture : class
    {
        var provider = settings.GetGuid("llmProviderId");
        var model = settings.GetString("llmModel");
        if (provider is null || provider == Guid.Empty || string.IsNullOrWhiteSpace(model))
            throw new ArchitectureDesignException("Configure an approved LLM provider and model before incremental planning.");
        var chat = await llmClientFactory!.CreateChatClientAsync(new AgentLlmSelection(provider.Value, model), cancellationToken);
        var tool = capture switch
        {
            StoryCapture stories => AIFunctionFactory.Create(
                (IncrementalStoryProposal proposal) => stories.Submit(proposal), toolName, toolDescription),
            TaskCapture tasks => AIFunctionFactory.Create(
                (IncrementalTaskProposal proposal) => tasks.Submit(proposal), toolName, toolDescription),
            _ => throw new InvalidOperationException("Unsupported incremental planning capture.")
        };
        var options = new HarnessAgentOptions
        {
            Id = SoftwareArchitectProfile.AgentId,
            Name = context.Identity?.DisplayName ?? SoftwareArchitectProfile.DisplayName,
            Description = "Produces one bounded incremental planning artifact.",
            MaximumIterationsPerRequest = 4,
            ChatOptions = new ChatOptions { Instructions = SoftwareArchitectProfile.SystemPrompt, Tools = [tool] },
            DisableAgentModeProvider = true,
            DisableAgentSkillsProvider = true,
            DisableFileMemory = true,
            DisableToolAutoApproval = true,
            DisableWebSearch = true
        };
#pragma warning disable MAAI001
        options.MaxContextWindowTokens = settings.GetInt32(
            "maxContextWindowTokens", SoftwareArchitectProfile.DefaultContextWindowTokens);
        options.MaxOutputTokens = outputTokens;
#pragma warning restore MAAI001
        var agent = chat.AsHarnessAgent(options);
        var session = await agent.CreateSessionAsync(cancellationToken);
        var boundedContext = System.Text.Json.JsonSerializer.Serialize(brief, IncrementalPlanningJson.Options);
        await foreach (var _ in agent.RunStreamingAsync(
            $"{prompt}\n\n<current_planning_scope>\n{boundedContext}\n</current_planning_scope>",
            session, options: null, cancellationToken)) { }
    }

    private static IncrementalStoryProposal FallbackStories(IncrementalProductBrief brief) => new(
        brief.PlanKey,
        brief.Epic.Key,
        [new IncrementalStory(
            $"{brief.Epic.Key}-S01",
            brief.Epic.Title,
            brief.Epic.Outcome,
            brief.Requirements,
            brief.Epic.AcceptanceCriteria,
            [],
            $"{brief.PlanKey}-SPRINT-01",
            1,
            $"Deliver a testable {brief.Epic.Title} increment")],
        []);

    private static IncrementalTaskProposal FallbackTasks(IncrementalProductBrief brief)
    {
        var story = brief.Story!;
        var tasks = new[]
        {
            new JuniorReadyTask($"{story.Key}-T01", $"Define {story.Title} boundary",
                $"Establish the smallest maintainable boundary needed for {story.Outcome}.", story.Requirements,
                story.Title, ["Preserve existing approved contracts", "Avoid speculative abstractions"], [],
                ["Missing or invalid input fails explicitly"], ["Unit tests cover positive and negative behavior"],
                ["Passing tests and reviewed contract examples"], "The boundary and failure contract are implemented, documented, and tested."),
            new JuniorReadyTask($"{story.Key}-T02", $"Implement {story.Title} behavior",
                $"Implement the approved behavior for {story.Outcome}.", story.Requirements,
                story.Title, ["Follow the boundary established by the prerequisite Task"], [$"{story.Key}-T01"],
                ["Partial state is not reported as success", "Retries do not duplicate effects"],
                ["Integration tests cover the accepted behavior and failure paths"],
                ["Automated tests demonstrate every Story acceptance criterion"],
                "The behavior satisfies the Story acceptance criteria with observable failure handling."),
            new JuniorReadyTask($"{story.Key}-T03", $"Verify {story.Title} outcome",
                $"Produce objective evidence that {story.Outcome} is satisfied.", story.AcceptanceCriteria,
                story.Title, ["Verification must be repeatable"], [$"{story.Key}-T02"],
                ["Verification failures identify the unmet criterion"],
                ["End-to-end tests map evidence to each acceptance criterion"],
                ["A repeatable test report records passing and failing evidence"],
                "All acceptance criteria have repeatable evidence and no unresolved failure."),
        };
        return new IncrementalTaskProposal(brief.PlanKey, story.Key, brief.PageOrdinal, true, tasks);
    }

    private sealed class StoryCapture
    {
        public IncrementalStoryProposal? Value { get; private set; }
        public object Submit(IncrementalStoryProposal proposal)
        {
            if (Value is not null) return new { accepted = false, message = "A proposal was already submitted." };
            if (proposal.Stories.Count is < 1 or > 12 || proposal.Stories.Any(x =>
                    string.IsNullOrWhiteSpace(x.Key) || string.IsNullOrWhiteSpace(x.Title) ||
                    string.IsNullOrWhiteSpace(x.SprintKey) || x.SprintOrdinal < 1 ||
                    x.Requirements.Count == 0 || x.AcceptanceCriteria.Count == 0))
                return new { accepted = false, message = "The Story proposal is incomplete or exceeds the bounded page." };
            Value = proposal;
            return new { accepted = true };
        }
    }

    private sealed class TaskCapture(string storyKey, int pageOrdinal)
    {
        public IncrementalTaskProposal? Value { get; private set; }
        public object Submit(IncrementalTaskProposal proposal)
        {
            if (Value is not null) return new { accepted = false, message = "A page was already submitted." };
            if (!string.Equals(proposal.StoryKey, storyKey, StringComparison.Ordinal) ||
                proposal.PageOrdinal != pageOrdinal || proposal.Tasks.Count is < 1 or > 8 ||
                proposal.Tasks.Any(x => string.IsNullOrWhiteSpace(x.Key) || string.IsNullOrWhiteSpace(x.Title) ||
                    string.IsNullOrWhiteSpace(x.Purpose) || x.Requirements.Count == 0 ||
                    string.IsNullOrWhiteSpace(x.AffectedBoundary) || x.TechnicalConstraints.Count == 0 ||
                    x.EdgeCases.Count == 0 || x.TestExpectations.Count == 0 ||
                    x.VerificationEvidence.Count == 0 || string.IsNullOrWhiteSpace(x.DefinitionOfDone)))
                return new { accepted = false, message = "The Task page is incomplete, mismatched, or exceeds eight Tasks." };
            Value = proposal;
            return new { accepted = true };
        }
    }
}
