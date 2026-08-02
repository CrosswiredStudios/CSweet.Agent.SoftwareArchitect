using CSweet.Agent.SDK;
using CSweet.WorkManagement.Contracts;

namespace CSweet.Agents.SoftwareArchitect.Tests;

public sealed class SoftwareArchitectAgentTests
{
    [Fact]
    public async Task Design_ReturnsValidatedTypedPlanAndProgress()
    {
        var agent = new SoftwareArchitectAgent(new StubDesignGenerator(
            ArchitecturePlanSamples.MinimalValidPlan()));
        var runtime = new AgentTestRuntime();

        var result = await runtime.ExecuteCapabilityAsync(
            agent,
            SoftwareArchitectProfile.DesignCapability,
            ValidDesignRequest());

        Assert.True(result.Succeeded);
        Assert.Equal(2, runtime.Progress.Count);
        Assert.Equal(
            ValidDesignRequest().BoardId,
            result.Value!.Value.GetProperty("boardId").GetGuid());
        Assert.Matches("^[a-f0-9]{64}$", result.Value.Value.GetProperty("planHash").GetString());
    }

    [Fact]
    public async Task Design_MissingAcceptanceCriteriaFailsBeforeModel()
    {
        var generator = new CountingDesignGenerator();
        var result = await new AgentTestRuntime().ExecuteCapabilityAsync(
            new SoftwareArchitectAgent(generator),
            SoftwareArchitectProfile.DesignCapability,
            ValidDesignRequest() with { AcceptanceCriteria = [] });

        Assert.False(result.Succeeded);
        Assert.Equal("at least one acceptance criterion is required.", result.Error);
        Assert.Equal(0, generator.Calls);
    }

    [Fact]
    public async Task Design_InvalidPlanFailsWithoutPublishing()
    {
        var invalid = ArchitecturePlanSamples.MinimalValidPlan() with
        {
            QualityAttributes = []
        };
        var result = await new AgentTestRuntime().ExecuteCapabilityAsync(
            new SoftwareArchitectAgent(new StubDesignGenerator(invalid)),
            SoftwareArchitectProfile.DesignCapability,
            ValidDesignRequest());

        Assert.False(result.Succeeded);
        Assert.Contains("quality-attribute", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Publish_RequiresProductManagerApprovalBeforeAnyWrite()
    {
        var design = FinalizedDesign(ArchitecturePlanSamples.MinimalValidPlan());
        var request = new ArchitecturePublishRequest(
            design.BoardId,
            design,
            new ArchitectureApproval("Engineering Lead", "Looks good.", DateTimeOffset.UtcNow),
            "publish-1")
        {
            RepositoryConnectionId = Guid.NewGuid(),
            BaseBranch = "main",
            FirstSprintSequence = 1
        };

        var result = await new AgentTestRuntime().ExecuteCapabilityAsync(
            new SoftwareArchitectAgent(),
            SoftwareArchitectProfile.PublishCapability,
            request);

        Assert.False(result.Succeeded);
        Assert.Contains("Product or Project Manager", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Publish_BlocksUnresolvedQuestionsBeforeAnyWrite()
    {
        var plan = ArchitecturePlanSamples.MinimalValidPlan() with
        {
            BlockingQuestions = ["Which customer outcome is authoritative?"]
        };
        var design = FinalizedDesign(plan);
        var result = await new AgentTestRuntime().ExecuteCapabilityAsync(
            new SoftwareArchitectAgent(),
            SoftwareArchitectProfile.PublishCapability,
            ValidPublication(design));

        Assert.False(result.Succeeded);
        Assert.Contains("unresolved", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Plan_RejectsUnknownTicketDependency()
    {
        var plan = ArchitecturePlanSamples.MinimalValidPlan();
        var sprint = plan.Sprints[0];
        var ticket = sprint.Tickets[0] with { Dependencies = ["MISSING"] };

        var error = ArchitecturePlanPolicy.ValidatePlan(
            plan with { Sprints = [sprint with { Tickets = [ticket] }] },
            forPublication: false);

        Assert.Contains("unknown dependency", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Plan_RejectsCyclicTicketDependencies()
    {
        var plan = ArchitecturePlanSamples.MinimalValidPlan();
        var sprint = plan.Sprints[0];
        var first = sprint.Tickets[0] with { Dependencies = ["SA-2"] };
        var second = sprint.Tickets[0] with
        {
            Key = "SA-2",
            Title = "Second delivery ticket",
            Kind = WorkItemKinds.Task,
            Dependencies = ["SA-1"]
        };

        var error = ArchitecturePlanPolicy.ValidatePlan(
            plan with { Sprints = [sprint with { Tickets = [first, second] }] },
            forPublication: false);

        Assert.Contains("acyclic", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Plan_RejectsEarlierSprintDependingOnLaterSprint()
    {
        var plan = ArchitecturePlanSamples.MinimalValidPlan();
        var firstSprint = plan.Sprints[0];
        var first = firstSprint.Tickets[0] with { Dependencies = ["SA-2"] };
        var second = firstSprint.Tickets[0] with
        {
            Key = "SA-2",
            Title = "Later sprint story",
            Dependencies = []
        };
        var laterSprint = firstSprint with
        {
            Ordinal = 2,
            Name = "Sprint 2",
            Tickets = [second]
        };

        var error = ArchitecturePlanPolicy.ValidatePlan(
            plan with
            {
                Sprints =
                [
                    firstSprint with { Tickets = [first] },
                    laterSprint
                ]
            },
            forPublication: false);

        Assert.Contains("later-sprint", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Publish_CreatesEpicSprintTicketEstimateAndScopeDeterministically()
    {
        var design = FinalizedDesign(ArchitecturePlanSamples.MinimalValidPlan());
        var board = Board(design.BoardId);
        var itemKeys = new Dictionary<string, WorkItem>(StringComparer.Ordinal);
        var sprintKeys = new Dictionary<string, WorkSprint>(StringComparer.Ordinal);
        var createRequests = new List<CreateWorkItemRequest>();
        var estimateRequests = new List<EstimateWorkItemRequest>();
        var scopeRequests = new List<SetWorkItemSprintRequest>();
        var sprintRequests = new List<CreateWorkSprintRequest>();
        var runtime = new AgentTestRuntime()
            .RegisterCapability<WorkBoardReference, WorkBoardDetail>(
                WorkItemCapabilities.Read,
                (_, _) => Task.FromResult(board))
            .RegisterCapability<CreateWorkItemRequest, WorkItem>(
                WorkItemCapabilities.Create,
                (request, _) =>
                {
                    createRequests.Add(request);
                    if (!itemKeys.TryGetValue(request.IdempotencyKey, out var item))
                    {
                        item = Item(
                            Guid.NewGuid(),
                            request.Kind,
                            request.Title,
                            request.Description ?? string.Empty,
                            request.ParentItemId);
                        itemKeys.Add(request.IdempotencyKey, item);
                    }
                    return Task.FromResult(item);
                })
            .RegisterCapability<CreateWorkSprintRequest, WorkSprint>(
                WorkSprintCapabilities.Create,
                (request, _) =>
                {
                    sprintRequests.Add(request);
                    if (!sprintKeys.TryGetValue(request.IdempotencyKey, out var sprint))
                    {
                        sprint = new WorkSprint(
                            Guid.NewGuid(),
                            request.BoardId,
                            request.Name,
                            request.Goal ?? string.Empty,
                            "Planned",
                            request.StartsAt,
                            request.EndsAt,
                            null,
                            null,
                            null,
                            0,
                            0,
                            0,
                            0,
                            1);
                        sprintKeys.Add(request.IdempotencyKey, sprint);
                    }
                    return Task.FromResult(sprint);
                })
            .RegisterCapability<EstimateWorkItemRequest, WorkItem>(
                WorkItemCapabilities.Estimate,
                (request, _) =>
                {
                    estimateRequests.Add(request);
                    var item = itemKeys.Values.Single(x => x.Id == request.ItemId);
                    return Task.FromResult(item with
                    {
                        EstimatePoints = request.EstimatePoints,
                        Revision = item.Revision + 1
                    });
                })
            .RegisterCapability<SetWorkItemSprintRequest, WorkItem>(
                WorkSprintCapabilities.ManageScope,
                (request, _) =>
                {
                    scopeRequests.Add(request);
                    var item = itemKeys.Values.Single(x => x.Id == request.ItemId);
                    return Task.FromResult(item with
                    {
                        SprintId = request.SprintId,
                        Revision = request.ExpectedItemRevision + 1
                    });
                });

        var agent = new SoftwareArchitectAgent();
        var first = await runtime.ExecuteCapabilityAsync(
            agent,
            SoftwareArchitectProfile.PublishCapability,
            ValidPublication(design));
        var second = await runtime.ExecuteCapabilityAsync(
            agent,
            SoftwareArchitectProfile.PublishCapability,
            ValidPublication(design));

        Assert.True(first.Succeeded);
        Assert.True(second.Succeeded);
        Assert.Equal(2, itemKeys.Count);
        Assert.Single(sprintKeys);
        Assert.All(createRequests.GroupBy(x => x.IdempotencyKey), group => Assert.Equal(2, group.Count()));
        Assert.All(sprintRequests, request => Assert.Contains(design.PlanId.ToString("N"), request.IdempotencyKey));
        Assert.All(estimateRequests, request => Assert.Equal(5, request.EstimatePoints));
        Assert.All(scopeRequests, request => Assert.NotNull(request.SprintId));
        Assert.Equal(WorkItemKinds.Epic, createRequests[0].Kind);
        Assert.Equal(WorkItemKinds.Story, createRequests[1].Kind);
        Assert.Contains("## Context", createRequests[1].Description);
        Assert.Contains("## Acceptance criteria", createRequests[1].Description);
        Assert.Contains("## Migration and rollback", createRequests[1].Description);
        Assert.Equal(
            Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"),
            createRequests[1].AccountableOrganizationUserId);
        Assert.Equal(4, createRequests[1].StageAssignments.Count);
        Assert.Contains(createRequests[1].StageAssignments, x =>
            x.StageKey == "development" &&
            x.AgentInstallationId == Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd"));
        Assert.Contains(createRequests[1].StageAssignments, x =>
            x.StageKey == "quality" &&
            x.AgentInstallationId == Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee"));
        Assert.Contains(createRequests[1].StageAssignments, x =>
            x.StageKey == "merge-decision" &&
            x.PrincipalKind == WorkOrchestrationPrincipalKinds.BoardManager);
        Assert.Contains(createRequests[1].StageAssignments, x =>
            x.StageKey == "governed-merge" &&
            x.PlatformAction == "git.merge.qa-approved.v1");
    }

    [Fact]
    public async Task Publish_RejectsStaleBoardContextBeforeAnyWrite()
    {
        var design = FinalizedDesign(ArchitecturePlanSamples.MinimalValidPlan());
        var writes = 0;
        var runtime = new AgentTestRuntime()
            .RegisterCapability<WorkBoardReference, WorkBoardDetail>(
                WorkItemCapabilities.Read,
                (_, _) => Task.FromResult(Board(Guid.NewGuid())))
            .RegisterCapability<CreateWorkItemRequest, WorkItem>(
                WorkItemCapabilities.Create,
                (_, _) =>
                {
                    writes++;
                    throw new InvalidOperationException("A stale board must not be mutated.");
                });

        var result = await runtime.ExecuteCapabilityAsync(
            new SoftwareArchitectAgent(),
            SoftwareArchitectProfile.PublishCapability,
            ValidPublication(design));

        Assert.False(result.Succeeded);
        Assert.Contains("board context", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, writes);
    }

    [Fact]
    public async Task Publish_RejectsPlanHashMismatch()
    {
        var design = FinalizedDesign(ArchitecturePlanSamples.MinimalValidPlan()) with
        {
            PlanHash = new string('0', 64)
        };
        var result = await new AgentTestRuntime().ExecuteCapabilityAsync(
            new SoftwareArchitectAgent(),
            SoftwareArchitectProfile.PublishCapability,
            ValidPublication(design));

        Assert.False(result.Succeeded);
        Assert.Contains("hash", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Configuration_DescribesHarnessBudgetsAndCadence()
    {
        var result = await new AgentTestRuntime().ExecuteCapabilityAsync(
            new SoftwareArchitectAgent(),
            AgentConfigurationCapabilities.Describe,
            new { });

        Assert.True(result.Succeeded);
        var keys = result.Value!.Value.GetProperty("fields")
            .EnumerateArray()
            .Select(x => x.GetProperty("key").GetString())
            .ToArray();
        Assert.Contains("llmProviderId", keys);
        Assert.Contains("llmModel", keys);
        Assert.Contains("maxContextWindowTokens", keys);
        Assert.Contains("maxOutputTokens", keys);
        Assert.Contains("defaultSprintLengthDays", keys);
        Assert.Contains("customInstructions", keys);
    }

    [Fact]
    public async Task UnsupportedCapabilityFailsSafelyAndCancellationIsHonored()
    {
        var agent = new SoftwareArchitectAgent();
        var unsupported = await new AgentTestRuntime().ExecuteCapabilityAsync(
            agent,
            "software-architecture.unknown.v1",
            new { });
        Assert.False(unsupported.Succeeded);

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new AgentTestRuntime().ExecuteCapabilityAsync(
                agent,
                SoftwareArchitectProfile.DesignCapability,
                ValidDesignRequest(),
                cancellation.Token));
    }

    [Fact]
    public async Task UnknownEventIsIgnoredAndAcknowledgementDoesNotStartConversationLoop()
    {
        var agent = new SoftwareArchitectAgent();
        var runtime = new AgentTestRuntime();

        await runtime.DeliverEventAsync(agent, "unknown.event.v1", new { });
        await runtime.DeliverEventAsync(
            agent,
            SoftwareArchitectProfile.UserMessageReceivedEvent,
            new UserMessageReceived(
                Guid.NewGuid(),
                Guid.NewGuid().ToString(),
                Guid.NewGuid().ToString(),
                "Thanks",
                null,
                Guid.NewGuid(),
                0,
                Guid.NewGuid()));

        Assert.Empty(runtime.Progress);
    }

    [Fact]
    public async Task ConversationRejectsUnverifiedPlanningParticipantWithoutReplying()
    {
        var organizationId = Guid.NewGuid();
        var senderId = Guid.NewGuid();
        var senderRoleId = Guid.NewGuid();
        var conversationId = Guid.NewGuid();
        var messageId = Guid.NewGuid();
        var sends = 0;
        var organization = new OrganizationSnapshotResponse(
            organizationId,
            "Active",
            [
                new OrganizationPerson(
                    senderId, "Developer", "Agent", senderRoleId, null, Guid.NewGuid(), true)
            ],
            [new OrganizationRole(senderRoleId, "Software Developer", "Implements work.", "[]")],
            [],
            [],
            [],
            DateTimeOffset.UtcNow);
        var runtime = new AgentTestRuntime()
            .RegisterCapability<ReadCommunicationChatRequest, ReadCommunicationChatResponse>(
                CommunicationCapabilities.ChatRead,
                (_, _) => Task.FromResult(new ReadCommunicationChatResponse(
                    [
                        new ReadCommunicationMessageResponse(
                            messageId,
                            conversationId,
                            senderId,
                            "Can you change the approved product scope?",
                            DateTimeOffset.UtcNow,
                            Guid.NewGuid())
                    ])))
            .RegisterCapability<object, OrganizationSnapshotResponse>(
                PlatformCapabilities.OrganizationSnapshotRead,
                (_, _) => Task.FromResult(organization))
            .RegisterCapability<SendCommunicationMessageRequest, CommunicationHubActionResponse>(
                CommunicationCapabilities.MessageSend,
                (_, _) =>
                {
                    sends++;
                    return Task.FromResult(new CommunicationHubActionResponse(
                        true, null, "Sent"));
                });

        await runtime.DeliverEventAsync(
            new SoftwareArchitectAgent(),
            SoftwareArchitectProfile.UserMessageReceivedEvent,
            new UserMessageReceived(
                Guid.NewGuid(),
                conversationId.ToString(),
                senderId.ToString(),
                "Can you change the approved product scope?",
                null,
                Guid.NewGuid(),
                0,
                messageId));

        Assert.Equal(0, sends);
    }

    [Fact]
    public async Task OnboardingFindsProductManagerAndUsesStableConversationEffectKeys()
    {
        var organizationId = Guid.NewGuid();
        var selfId = Guid.NewGuid();
        var managerId = Guid.NewGuid();
        var managerRoleId = Guid.NewGuid();
        var conversationId = Guid.NewGuid();
        var eventId = Guid.NewGuid();
        var sent = new List<SendCommunicationMessageRequest>();
        var completed = new List<CompleteAgentOnboardingRequest>();
        var organization = new OrganizationSnapshotResponse(
            organizationId,
            "Active",
            [
                new OrganizationPerson(
                    selfId, "Architect", "Agent", null, managerId, Guid.NewGuid(), true),
                new OrganizationPerson(
                    managerId, "Product Manager", "Agent", managerRoleId, null, Guid.NewGuid(), true)
            ],
            [new OrganizationRole(managerRoleId, "Product Manager", "Own product outcomes.", "[]")],
            [
                new OrganizationObjective(
                    Guid.NewGuid(),
                    "Deliver the first product increment",
                    "Approved objective",
                    "Active",
                    null)
            ],
            [],
            [],
            DateTimeOffset.UtcNow);
        var runtime = new AgentTestRuntime()
            .RegisterCapability<object, OrganizationSnapshotResponse>(
                PlatformCapabilities.OrganizationSnapshotRead,
                (_, _) => Task.FromResult(organization))
            .RegisterCapability<TeamRosterRequest, TeamRosterResponse>(
                PlatformCapabilities.TeamRosterRead,
                (_, _) => Task.FromResult(new TeamRosterResponse(null)))
            .RegisterCapability<CreateCommunicationChatRequest, CommunicationHubActionResponse>(
                CommunicationCapabilities.ChatCreate,
                (_, _) => Task.FromResult(new CommunicationHubActionResponse(
                    true,
                    null,
                    "Created",
                    new CommunicationChatResponse(
                        conversationId,
                        "Product Manager",
                        null,
                        true,
                        true,
                        false,
                        true,
                        DateTimeOffset.UtcNow,
                        [],
                        null,
                        null,
                        0))))
            .RegisterCapability<SendCommunicationMessageRequest, CommunicationHubActionResponse>(
                CommunicationCapabilities.MessageSend,
                (request, _) =>
                {
                    sent.Add(request);
                    return Task.FromResult(new CommunicationHubActionResponse(
                        true, null, "Sent"));
                })
            .RegisterCapability<CompleteAgentOnboardingRequest, CompleteAgentOnboardingResponse>(
                AgentLifecycleCapabilities.CompleteOnboarding,
                (request, _) =>
                {
                    completed.Add(request);
                    return Task.FromResult(new CompleteAgentOnboardingResponse(
                        true,
                        DateTimeOffset.UtcNow));
                });
        var agent = new SoftwareArchitectAgent();
        var onboarded = new AgentOnboardedEvent(
            organizationId,
            selfId,
            managerId,
            conversationId,
            DateTimeOffset.UtcNow);

        await runtime.DeliverEventAsync(
            agent,
            AgentLifecycleEvents.Onboarded,
            onboarded,
            eventId);
        await runtime.DeliverEventAsync(
            agent,
            AgentLifecycleEvents.Onboarded,
            onboarded,
            eventId);

        Assert.Equal(2, sent.Count);
        Assert.All(sent, message => Assert.Equal(conversationId, message.ChatId));
        Assert.Single(sent.Select(x => x.IdempotencyKey).Distinct());
        Assert.Equal($"software-architect:onboarding:{eventId:N}", sent[0].IdempotencyKey);
        Assert.All(completed, request => Assert.Equal(eventId, request.EventId));
    }

    private static ArchitectureDesignRequest ValidDesignRequest()
    {
        return new ArchitectureDesignRequest(
            Guid.Parse("8958d565-a616-4d36-b786-d40656929a4f"),
            "Deliver an approved customer workflow.",
            ["Keep product behavior cohesive and maintainable."],
            ["The workflow passes end to end."],
            "design-1",
            Constraints: ["Do not introduce unnecessary distributed services."],
            QualityAttributes: ["Maintainability", "Reliability"]);
    }

    private static ArchitectureDesignResponse FinalizedDesign(ArchitecturePlan plan) =>
        ArchitecturePlanPolicy.FinalizeDraft(
            ValidDesignRequest(),
            plan,
            DateTimeOffset.Parse("2026-08-03T00:00:00Z"));

    private static ArchitecturePublishRequest ValidPublication(ArchitectureDesignResponse design) =>
        new(
            design.BoardId,
            design,
            new ArchitectureApproval(
                "Product Manager",
                "The plan satisfies the approved product outcome and acceptance criteria.",
                DateTimeOffset.Parse("2026-08-01T12:00:00Z"),
                Guid.NewGuid(),
                Guid.NewGuid()),
            "publish-1")
        {
            RepositoryConnectionId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
            BaseBranch = "main",
            FirstSprintSequence = 1,
            AccountableOrganizationUserId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"),
            DeveloperInstallationId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd"),
            QualityInstallationId = Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee")
        };

    private static WorkBoardDetail Board(Guid boardId) =>
        new(
            new WorkBoardSummary(boardId, "Product Team", "Approved board", false, false, 1, []),
            [new WorkBoardColumn(Guid.NewGuid(), "To Do", "ToDo", 0, "None", null)],
            []);

    private static WorkItem Item(
        Guid id,
        string kind,
        string title,
        string description,
        Guid? parentId) =>
        new(
            id,
            Guid.NewGuid(),
            parentId,
            null,
            kind,
            title,
            description,
            "Ready",
            "High",
            null,
            1,
            1,
            null);

    private sealed class StubDesignGenerator(ArchitecturePlan plan) : IArchitectureDesignGenerator
    {
        public Task<ArchitecturePlan> GenerateAsync(
            ArchitectureDesignRequest request,
            AgentRuntimeContext context,
            AgentSettings settings,
            CancellationToken cancellationToken) =>
            Task.FromResult(plan);
    }

    private sealed class CountingDesignGenerator : IArchitectureDesignGenerator
    {
        public int Calls { get; private set; }

        public Task<ArchitecturePlan> GenerateAsync(
            ArchitectureDesignRequest request,
            AgentRuntimeContext context,
            AgentSettings settings,
            CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(ArchitecturePlanSamples.MinimalValidPlan());
        }
    }
}
