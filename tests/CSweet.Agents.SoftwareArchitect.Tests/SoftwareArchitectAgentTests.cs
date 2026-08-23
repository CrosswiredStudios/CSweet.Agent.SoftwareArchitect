using System.Text.Json;
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
        var runtime = DesignRuntime();

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
    public async Task DesignV2_RequiresOutcomeEpicStoryAndTaskHierarchy()
    {
        var valid = await DesignRuntime().ExecuteCapabilityAsync(
            new SoftwareArchitectAgent(new StubDesignGenerator(
                ArchitecturePlanSamples.MinimalHierarchicalPlan())),
            SoftwareArchitectProfile.DesignCapabilityV2,
            ValidDesignRequest());
        var orphaned = ArchitecturePlanSamples.MinimalHierarchicalPlan();
        var firstSprint = orphaned.Sprints[0];
        orphaned = orphaned with
        {
            Sprints =
            [
                firstSprint with
                {
                    Tickets = firstSprint.Tickets.Select(ticket =>
                        ticket.Kind == WorkItemKinds.Task
                            ? ticket with { ParentStoryKey = "MISSING" }
                            : ticket).ToArray()
                },
                orphaned.Sprints[1]
            ]
        };
        var invalid = await DesignRuntime().ExecuteCapabilityAsync(
            new SoftwareArchitectAgent(new StubDesignGenerator(orphaned)),
            SoftwareArchitectProfile.DesignCapabilityV2,
            ValidDesignRequest());

        Assert.True(valid.Succeeded);
        Assert.False(invalid.Succeeded);
        Assert.Contains("parent Story", invalid.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DesignV2_RequiresTasksForStoriesInEverySprint()
    {
        var plan = ArchitecturePlanSamples.MinimalHierarchicalPlan();
        var story = plan.Sprints[0].Tickets[0] with
        {
            Key = "SA-3",
            Title = "Extend the customer outcome",
            Dependencies = []
        };
        var task = plan.Sprints[0].Tickets[1] with
        {
            Key = "SA-3-T1",
            Title = "Implement the later extension",
            ParentStoryKey = story.Key,
            Dependencies = []
        };
        plan = plan with
        {
            RequirementTraceability =
            [
                new ArchitectureRequirementTrace(
                    "Keep product behavior cohesive and maintainable.",
                    ["Application"],
                    plan.Sprints.SelectMany(x => x.Tickets).Select(x => x.Key)
                        .Concat([story.Key, task.Key]).ToArray()),
                new ArchitectureRequirementTrace(
                    "The workflow passes end to end.",
                    ["Application"],
                    [plan.Sprints[0].Tickets[0].Key, story.Key])
            ],
            Sprints = plan.Sprints.Concat(
            [
                plan.Sprints[0] with
                {
                    Ordinal = 3,
                    Name = "Sprint 3 - Extension",
                    Goal = "Extend the approved outcome.",
                    Tickets = [story, task]
                }
            ]).ToArray()
        };
        var agent = new SoftwareArchitectAgent(new StubDesignGenerator(plan));

        var initial = await DesignRuntime().ExecuteCapabilityAsync(
            agent, SoftwareArchitectProfile.DesignCapabilityV2, ValidDesignRequest());
        var refinement = await DesignRuntime().ExecuteCapabilityAsync(
            agent,
            SoftwareArchitectProfile.DesignCapabilityV2,
            ValidDesignRequest() with { RollingRefinement = true });

        Assert.True(initial.Succeeded);
        Assert.True(refinement.Succeeded);
    }

    [Fact]
    public async Task DesignV2_AcceptsPlansLargerThanOnePublicationBatch()
    {
        var plan = ArchitecturePlanSamples.MinimalHierarchicalPlan();
        var firstSprint = plan.Sprints[0];
        var story = firstSprint.Tickets.Single(x => x.Kind == WorkItemKinds.Story);
        var template = firstSprint.Tickets.Single(x => x.Kind == WorkItemKinds.Task);
        var tasks = Enumerable.Range(1, 41)
            .Select(index => template with
            {
                Key = $"SA-1-T{index}",
                Title = $"Implement vertical slice part {index}",
                ParentStoryKey = story.Key,
                Dependencies = index == 1 ? [] : [$"SA-1-T{index - 1}"]
            })
            .ToArray();
        var allKeys = new[] { story.Key }
            .Concat(tasks.Select(x => x.Key))
            .Concat(plan.Sprints[1].Tickets.Select(x => x.Key))
            .ToArray();
        plan = plan with
        {
            RequirementTraceability =
            [
                new ArchitectureRequirementTrace(
                    "Keep product behavior cohesive and maintainable.",
                    ["Application"],
                    allKeys),
                new ArchitectureRequirementTrace(
                    "The workflow passes end to end.",
                    ["Application"],
                    allKeys)
            ],
            Sprints =
            [
                firstSprint with { Tickets = [story, .. tasks] },
                plan.Sprints[1]
            ]
        };

        var result = await DesignRuntime().ExecuteCapabilityAsync(
            new SoftwareArchitectAgent(new StubDesignGenerator(plan)),
            SoftwareArchitectProfile.DesignCapabilityV2,
            ValidDesignRequest());

        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task DesignV2_RejectsAnApprovedCriterionWithoutStoryTraceability()
    {
        var plan = ArchitecturePlanSamples.MinimalHierarchicalPlan() with
        {
            RequirementTraceability =
            [
                ArchitecturePlanSamples.MinimalHierarchicalPlan().RequirementTraceability[0]
            ]
        };

        var result = await DesignRuntime().ExecuteCapabilityAsync(
            new SoftwareArchitectAgent(new StubDesignGenerator(plan)),
            SoftwareArchitectProfile.DesignCapabilityV2,
            ValidDesignRequest());

        Assert.False(result.Succeeded);
        Assert.Contains("exact requirement-trace", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Design_MissingAcceptanceCriteriaFailsBeforeModel()
    {
        var generator = new CountingDesignGenerator();
        var result = await DesignRuntime().ExecuteCapabilityAsync(
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
        var result = await DesignRuntime().ExecuteCapabilityAsync(
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
            RepositoryId = Guid.NewGuid(),
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
    public void Plan_RequiresJuniorReadyGuidanceAndPositiveEstimate()
    {
        var plan = ArchitecturePlanSamples.MinimalValidPlan();
        var sprint = plan.Sprints[0];
        var ticket = sprint.Tickets[0];

        var missingGuidance = ArchitecturePlanPolicy.ValidatePlan(
            plan with
            {
                Sprints = [sprint with
                {
                    Tickets = [ticket with { ImplementationGuidance = [] }]
                }]
            },
            forPublication: false);
        var missingEstimate = ArchitecturePlanPolicy.ValidatePlan(
            plan with
            {
                Sprints = [sprint with
                {
                    Tickets = [ticket with { EstimatePoints = 0 }]
                }]
            },
            forPublication: false);

        Assert.Contains("ordered implementation guidance", missingGuidance, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("greater than 0", missingEstimate, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Publish_CreatesEpicSprintTicketEstimateAndScopeDeterministically()
    {
        var design = FinalizedDesign(ArchitecturePlanSamples.MinimalHierarchicalPlan());
        var board = Board(design.BoardId);
        var itemKeys = new Dictionary<string, WorkItem>(StringComparer.Ordinal);
        var sprintKeys = new Dictionary<string, WorkSprint>(StringComparer.Ordinal);
        var createRequests = new List<CreateWorkItemRequest>();
        var finalizeRequests = new List<FinalizeWorkItemDeliveryRequest>();
        var estimateRequests = new List<EstimateWorkItemRequest>();
        var scopeRequests = new List<SetWorkItemSprintRequest>();
        var moveRequests = new List<MoveWorkItemRequest>();
        var sprintRequests = new List<CreateWorkSprintRequest>();
        var architectEmployeeId = Guid.NewGuid();
        var developerEmployeeId = Guid.NewGuid();
        var qualityEmployeeId = Guid.NewGuid();
        var architectInstallationId = Guid.NewGuid();
        var runtime = new AgentTestRuntime()
            .RegisterCapability<WorkBoardReference, WorkBoardDetail>(
                WorkItemCapabilities.Read,
                (_, _) => Task.FromResult(board))
            .RegisterCapability<TeamRosterRequest, TeamRosterResponse>(
                PlatformCapabilities.TeamRosterRead,
                (_, _) => Task.FromResult(new TeamRosterResponse(new AgentTeamContext(
                    board.Board.TeamId!.Value.ToString("D"), "product", "Product Team", 1,
                    Guid.NewGuid().ToString("D"), "Product Manager",
                    [
                        new AgentTeammate(architectEmployeeId.ToString("D"), "Architect", "Agent", null, "Software Architect", "Peer", "Active"),
                        new AgentTeammate(developerEmployeeId.ToString("D"), "Developer", "Agent", null, "Software Developer", "Peer", "Active"),
                        new AgentTeammate(qualityEmployeeId.ToString("D"), "QA", "Agent", null, "Software QA", "Peer", "Active")
                    ], [], 3, false))))
            .RegisterCapability<object, OrganizationSnapshotResponse>(
                PlatformCapabilities.OrganizationSnapshotRead,
                (_, _) => Task.FromResult(new OrganizationSnapshotResponse(
                    Guid.NewGuid(), "Active",
                    [
                        new OrganizationPerson(architectEmployeeId, "Architect", "Agent", null, null, architectInstallationId, true),
                        new OrganizationPerson(developerEmployeeId, "Developer", "Agent", null, null,
                            Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd"), true),
                        new OrganizationPerson(qualityEmployeeId, "QA", "Agent", null, null,
                            Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee"), true)
                    ], [], [], [], [], DateTimeOffset.UtcNow)))
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
                            request.ParentItemId) with
                        {
                            Delivery = request.Delivery,
                            AccountableOrganizationUserId = request.AccountableOrganizationUserId,
                            StageAssignments = request.StageAssignments
                        };
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
            .RegisterCapability<FinalizeWorkItemDeliveryRequest, WorkItem>(
                WorkItemCapabilities.FinalizeDelivery,
                (request, _) =>
                {
                    finalizeRequests.Add(request);
                    var item = itemKeys.Values.Single(x => x.Id == request.ItemId);
                    var finalized = item with
                    {
                        Delivery = request.Delivery,
                        AccountableOrganizationUserId = request.AccountableOrganizationUserId,
                        StageAssignments = request.StageAssignments,
                        Revision = item.Revision + 1
                    };
                    itemKeys[itemKeys.Single(x => x.Value.Id == item.Id).Key] = finalized;
                    return Task.FromResult(finalized);
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
                })
            .RegisterCapability<MoveWorkItemRequest, WorkItem>(
                WorkItemCapabilities.Move,
                (request, _) =>
                {
                    moveRequests.Add(request);
                    var item = itemKeys.Values.Single(x => x.Id == request.ItemId);
                    return Task.FromResult(item with
                    {
                        ColumnId = request.TargetColumnId,
                        Revision = request.ExpectedRevision + 1
                    });
                });

        var agent = new SoftwareArchitectAgent();
        var draft = await runtime.ExecuteCapabilityAsync(
            agent,
            SoftwareArchitectProfile.PublishCapabilityV2,
            ValidPublication(design) with
            {
                RepositoryId = Guid.Empty,
                BaseBranch = string.Empty
            });
        var first = await runtime.ExecuteCapabilityAsync(
            agent,
            SoftwareArchitectProfile.PublishCapabilityV2,
            ValidPublication(design));
        var second = await runtime.ExecuteCapabilityAsync(
            agent,
            SoftwareArchitectProfile.PublishCapabilityV2,
            ValidPublication(design));

        Assert.True(draft.Succeeded);
        Assert.False(draft.Value!.Value.GetProperty("deliveryFinalized").GetBoolean());
        Assert.True(first.Succeeded);
        Assert.True(second.Succeeded);
        Assert.Equal(6, itemKeys.Count);
        Assert.Equal(2, sprintKeys.Count);
        Assert.All(createRequests.GroupBy(x => x.IdempotencyKey), group => Assert.Equal(3, group.Count()));
        Assert.All(createRequests.Take(6), request => Assert.Null(request.DueDate));
        Assert.All(sprintRequests, request => Assert.Contains(design.BoardId.ToString("N"), request.IdempotencyKey));
        Assert.All(sprintRequests.Take(2), request =>
        {
            Assert.Null(request.StartsAt);
            Assert.Null(request.EndsAt);
        });
        Assert.Equal(8, estimateRequests.Count);
        Assert.All(estimateRequests, request => Assert.Equal(5, request.EstimatePoints));
        Assert.All(scopeRequests, request => Assert.NotNull(request.SprintId));
        Assert.Equal(4, moveRequests.Count);
        Assert.All(moveRequests, request => Assert.Equal(
            "Ready For Development",
            board.Columns.Single(x => x.Id == request.TargetColumnId).Name));
        Assert.Equal(WorkItemKinds.Epic, createRequests[0].Kind);
        Assert.Equal(WorkItemKinds.Epic, createRequests[1].Kind);
        Assert.Equal(WorkItemKinds.Story, createRequests[2].Kind);
        Assert.Contains(createRequests[2].ParentItemId,
            itemKeys.Values.Where(x => x.Kind == WorkItemKinds.Epic).Select(x => (Guid?)x.Id));
        var taskRequest = createRequests.First(x => x.Kind == WorkItemKinds.Task);
        Assert.Equal(itemKeys.Values.Single(x => x.Title == createRequests[2].Title).Id, taskRequest.ParentItemId);
        Assert.Contains("## Context", createRequests[2].Description);
        Assert.Contains("## Acceptance criteria", createRequests[2].Description);
        Assert.Contains("## Ordered implementation guidance", createRequests[2].Description);
        Assert.Contains("Negative:", createRequests[2].Description);
        Assert.Contains("Observability:", createRequests[2].Description);
        Assert.Contains("## Migration and rollback", createRequests[2].Description);
        Assert.NotNull(createRequests[2].Planning);
        Assert.Null(createRequests[2].Delivery);
        Assert.Equal(8, finalizeRequests.Count);
        Assert.Equal(
            Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"),
            finalizeRequests[0].AccountableOrganizationUserId);
        Assert.Equal("main", finalizeRequests[0].Delivery.BaseBranch);
        Assert.Equal(4, finalizeRequests[0].StageAssignments.Count);
        Assert.Contains(finalizeRequests[0].StageAssignments, x =>
            x.StageKey == "development" &&
            x.AgentInstallationId == Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd"));
        Assert.Contains(finalizeRequests[0].StageAssignments, x =>
            x.StageKey == "quality" &&
            x.AgentInstallationId == Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee"));
        Assert.Contains(finalizeRequests[0].StageAssignments, x =>
            x.StageKey == "merge-decision" &&
            x.PrincipalKind == WorkOrchestrationPrincipalKinds.BoardManager);
        Assert.Contains(finalizeRequests[0].StageAssignments, x =>
            x.StageKey == "governed-merge" &&
            x.PlatformAction == "source-control.merge.execute.v2");

        var writesBeforeInvalidPool = createRequests.Count;
        var invalidPool = ValidPublication(design) with
        {
            DeveloperInstallationIds = [Guid.NewGuid()]
        };
        var rejected = await runtime.ExecuteCapabilityAsync(
            agent, SoftwareArchitectProfile.PublishCapabilityV2, invalidPool);
        Assert.False(rejected.Succeeded);
        Assert.Contains("outside the active approved team", rejected.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(writesBeforeInvalidPool, createRequests.Count);
    }

    [Fact]
    public void AssignmentPools_BalanceDeveloperAndQaEstimatesDeterministicallyAcrossRetries()
    {
        var developerOne = Guid.Parse("10000000-0000-0000-0000-000000000000");
        var developerTwo = Guid.Parse("20000000-0000-0000-0000-000000000000");
        var qualityOne = Guid.Parse("30000000-0000-0000-0000-000000000000");
        var qualityTwo = Guid.Parse("40000000-0000-0000-0000-000000000000");

        IReadOnlyList<(Guid Developer, Guid Quality)> Assign()
        {
            var developers = ArchitecturePlanPolicy.NormalizeAssignmentPool(
                [], [developerTwo, developerOne], Guid.Empty);
            var quality = ArchitecturePlanPolicy.NormalizeAssignmentPool(
                [], [qualityTwo, qualityOne], Guid.Empty);
            var developerLoad = developers.ToDictionary(ArchitecturePlanPolicy.AssignmentKey, _ => 0m);
            var qualityLoad = quality.ToDictionary(ArchitecturePlanPolicy.AssignmentKey, _ => 0m);
            return new decimal[] { 5, 3, 2 }
                .Select(points => (
                    ArchitecturePlanPolicy.AssignLeastLoaded(developers, developerLoad, points).AgentInstallationId!.Value,
                    ArchitecturePlanPolicy.AssignLeastLoaded(quality, qualityLoad, points).AgentInstallationId!.Value))
                .ToList();
        }

        var firstAttempt = Assign();
        var retry = Assign();
        Assert.Equal(firstAttempt, retry);
        Assert.Equal(
            [(developerOne, qualityOne), (developerTwo, qualityTwo), (developerTwo, qualityTwo)],
            firstAttempt);
    }

    [Fact]
    public void DeliveryProfile_AgentOnlyUsesShortWindowsWithoutHumanEstimates()
    {
        var profile = ArchitecturePlanPolicy.BuildDeliveryProfile(
            Roster("Agent", "Agent"), requestedSprintLengthDays: 14, defaultHumanSprintLengthDays: 14);

        Assert.False(profile.UsesHumanEstimates);
        Assert.Equal(1, profile.SprintLengthDays);
        Assert.Equal(0, profile.HumanDeliveryMemberCount);
        Assert.Equal(2, profile.AgentDeliveryMemberCount);
        Assert.Contains("dependency depth", profile.ScheduleBasis, StringComparison.OrdinalIgnoreCase);
        var noPoints = ArchitecturePlanSamples.MinimalValidPlan() with
        {
            Sprints = ArchitecturePlanSamples.MinimalValidPlan().Sprints
                .Select(sprint => sprint with
                {
                    Tickets = sprint.Tickets.Select(ticket => ticket with { EstimatePoints = null }).ToList()
                }).ToList()
        };
        Assert.Null(ArchitecturePlanPolicy.ValidatePlan(noPoints, false, profile));
    }

    [Fact]
    public void DeliveryProfile_HumanMemberEnablesConfiguredHumanCadence()
    {
        var profile = ArchitecturePlanPolicy.BuildDeliveryProfile(
            Roster("Human", "Agent"), null, 10);

        Assert.True(profile.UsesHumanEstimates);
        Assert.Equal(10, profile.SprintLengthDays);
        Assert.Equal(1, profile.HumanDeliveryMemberCount);
        Assert.Null(ArchitecturePlanPolicy.ValidatePlan(
            ArchitecturePlanSamples.MinimalValidPlan(), false, profile));
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
        var conversationId = Guid.NewGuid();
        var senderId = Guid.NewGuid();
        var messageId = Guid.NewGuid();
        var organizationId = Guid.NewGuid();
        var runtime = new AgentTestRuntime()
            .RegisterCapability<object, CommunicationMessages>(
                CommunicationCapabilities.ChatRead,
                (_, _) => Task.FromResult(new CommunicationMessages(
                [
                    new CommunicationMessage(messageId, 1, conversationId, senderId,
                        "CEO", "Human", "Thanks", DateTimeOffset.UtcNow)
                ])))
            .RegisterCapability<object, OrganizationSnapshotResponse>(
                PlatformCapabilities.OrganizationSnapshotRead,
                (_, _) => Task.FromResult(new OrganizationSnapshotResponse(
                    organizationId, "Active",
                    [new OrganizationPerson(senderId, "CEO", "Human", null, null, null, true)],
                    [], [], [], [], DateTimeOffset.UtcNow)));

        await runtime.DeliverEventAsync(agent, "unknown.event.v1", new { });
        await runtime.DeliverEventAsync(
            agent,
            CommunicationEvents.MessageReceived,
            new CommunicationMessageReceivedEvent(
                Guid.NewGuid(),
                conversationId.ToString(),
                senderId.ToString(),
                "Thanks",
                null,
                Guid.NewGuid(),
                0,
                messageId));

        Assert.Single(runtime.Progress);
        Assert.Equal("Acknowledged.", runtime.Progress[0].GetProperty("delta").GetString());
        Assert.True(runtime.Progress[0].GetProperty("isFinal").GetBoolean());
        Assert.Equal(AgentTurnStreamKinds.FinalCommit, runtime.Progress[0].GetProperty("kind").GetString());
    }

    [Fact]
    public async Task ConversationExplicitlyRejectsInactiveParticipant()
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
                    senderId, "Developer", "Agent", senderRoleId, null, Guid.NewGuid(), false)
            ],
            [new OrganizationRole(senderRoleId, "Software Developer", "Implements work.", "[]")],
            [],
            [],
            [],
            DateTimeOffset.UtcNow);
        var runtime = new AgentTestRuntime()
            .RegisterCapability<object, CommunicationMessages>(
                CommunicationCapabilities.ChatRead,
                (_, _) => Task.FromResult(new CommunicationMessages(
                    [
                        new CommunicationMessage(
                            messageId,
                            1,
                            conversationId,
                            senderId,
                            "Developer",
                            "Agent",
                            "Can you change the approved product scope?",
                            DateTimeOffset.UtcNow,
                            Guid.NewGuid())
                    ])))
            .RegisterCapability<object, OrganizationSnapshotResponse>(
                PlatformCapabilities.OrganizationSnapshotRead,
                (_, _) => Task.FromResult(organization))
            .RegisterCapability<object, CommunicationMessage>(
                CommunicationCapabilities.MessageSend,
                (_, _) =>
                {
                    sends++;
                    return Task.FromResult(new CommunicationMessage(
                        Guid.NewGuid(), 1, conversationId, null, "Architect", "Agent",
                        "Rejected", DateTimeOffset.UtcNow));
                });

        await runtime.DeliverEventAsync(
            new SoftwareArchitectAgent(),
            CommunicationEvents.MessageReceived,
            new CommunicationMessageReceivedEvent(
                Guid.NewGuid(),
                conversationId.ToString(),
                senderId.ToString(),
                "Can you change the approved product scope?",
                null,
                Guid.NewGuid(),
                0,
                messageId));

        Assert.Equal(0, sends);
        Assert.Contains("not an active participant", runtime.Progress[0].GetProperty("delta").GetString());
    }

    [Fact]
    public async Task CeoCanAskArchitectToCollaborateWithProductManagerOnKanbanBoard()
    {
        var organizationId = Guid.NewGuid();
        var architectId = Guid.NewGuid();
        var architectInstallationId = Guid.NewGuid();
        var ceoId = Guid.NewGuid();
        var productManagerId = Guid.NewGuid();
        var productManagerInstallationId = Guid.NewGuid();
        var productManagerRoleId = Guid.NewGuid();
        var conversationId = Guid.NewGuid();
        var messageId = Guid.NewGuid();
        var turnId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        StartAgentCoordinationRequest? started = null;
        JsonElement? sentToProductManager = null;
        var runtime = new AgentTestRuntime()
            .RegisterCapability<object, CommunicationMessages>(
                CommunicationCapabilities.ChatRead,
                (_, _) => Task.FromResult(new CommunicationMessages(
                [
                    new CommunicationMessage(messageId, 1, conversationId, ceoId,
                        "CEO", "Human",
                        "Please reach out to the Product Manager and populate the kanban board with tickets to get the demo completed.",
                        DateTimeOffset.UtcNow, turnId)
                ])))
            .RegisterCapability<object, OrganizationSnapshotResponse>(
                PlatformCapabilities.OrganizationSnapshotRead,
                (_, _) => Task.FromResult(new OrganizationSnapshotResponse(
                    organizationId, "Active",
                    [
                        new OrganizationPerson(architectId, "Architect", "Agent", null, null, architectInstallationId, true),
                        new OrganizationPerson(ceoId, "CEO", "Human", null, null, null, true),
                        new OrganizationPerson(productManagerId, "Product Manager", "Agent",
                            productManagerRoleId, null, productManagerInstallationId, true)
                    ],
                    [new OrganizationRole(productManagerRoleId, "Product Manager", "Owns outcomes.", "[]")],
                    [], [], [], DateTimeOffset.UtcNow)))
            .RegisterCapability<CreateCommunicationChat, CommunicationAction>(
                CommunicationCapabilities.ChatCreate,
                (_, _) => Task.FromResult(new CommunicationAction(
                    true, null, "Created",
                    new CommunicationChat(Guid.NewGuid(), "Product Manager", null, true, true,
                        false, true, DateTimeOffset.UtcNow,
                        [new CommunicationParticipant(productManagerId, "Product Manager", "Agent", "Product Manager")],
                        null, null, 0))))
            .RegisterCapability<object, CommunicationMessage>(
                CommunicationCapabilities.MessageSend,
                (request, _) =>
                {
                    sentToProductManager = JsonSerializer.SerializeToElement(request);
                    return Task.FromResult(new CommunicationMessage(
                        Guid.NewGuid(), 1, Guid.NewGuid(), architectId, "Architect", "Agent",
                        "Sent", DateTimeOffset.UtcNow) with { ChatTurnId = Guid.NewGuid() });
                })
            .RegisterCapability<StartAgentCoordinationRequest, AgentCoordinationSession>(
                CommunicationCapabilities.CoordinationStart,
                (request, _) =>
                {
                    started = request;
                    var now = DateTimeOffset.UtcNow;
                    return Task.FromResult(new AgentCoordinationSession(
                        sessionId, Guid.NewGuid(), conversationId, turnId, messageId,
                        new AgentCoordinationParticipant(architectId, architectInstallationId, "Architect", "Software Architect"),
                        new AgentCoordinationParticipant(productManagerId, productManagerInstallationId, "Product Manager", "Product Manager"),
                        request.Subject, request.Objective, request.SuccessCriteria,
                        AgentCoordinationStatuses.Active, 1, 1, productManagerId, false, null,
                        now, now, []));
                });

        await runtime.DeliverEventAsync(
            new SoftwareArchitectAgent(),
            CommunicationEvents.MessageReceived,
            new CommunicationMessageReceivedEvent(
                Guid.NewGuid(), conversationId.ToString("D"), ceoId.ToString("D"),
                "Please reach out to the Product Manager and populate the kanban board with tickets to get the demo completed.",
                null, turnId, 0, messageId));

        Assert.Null(started);
        Assert.Null(sentToProductManager);
        Assert.Contains("active planning session", runtime.Progress[0].GetProperty("delta").GetString(),
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AuthenticatedProductManagerKickoffStartsDurableReleasePlanning()
    {
        var organizationId = Guid.NewGuid();
        var productManagerId = Guid.NewGuid();
        var productManagerInstallationId = Guid.NewGuid();
        var productManagerRoleId = Guid.NewGuid();
        var architectId = Guid.NewGuid();
        var architectInstallationId = Guid.NewGuid();
        var conversationId = Guid.NewGuid();
        var messageId = Guid.NewGuid();
        var turnId = Guid.NewGuid();
        StartAgentCoordinationRequest? started = null;
        var kickoff = """
<software_team_planning_kickoff>
Board: Product Delivery
Approved product goal: Ship the first release.
</software_team_planning_kickoff>
""";
        var runtime = new AgentTestRuntime()
            .RegisterCapability<object, CommunicationMessages>(
                CommunicationCapabilities.ChatRead,
                (_, _) => Task.FromResult(new CommunicationMessages(
                [
                    new CommunicationMessage(messageId, 1, conversationId, productManagerId,
                        "Product Manager", "Agent", kickoff, DateTimeOffset.UtcNow, turnId)
                ])))
            .RegisterCapability<object, OrganizationSnapshotResponse>(
                PlatformCapabilities.OrganizationSnapshotRead,
                (_, _) => Task.FromResult(new OrganizationSnapshotResponse(
                    organizationId, "Active",
                    [
                        new OrganizationPerson(productManagerId, "Product Manager", "Agent",
                            productManagerRoleId, null, productManagerInstallationId, true),
                        new OrganizationPerson(architectId, "Architect", "Agent",
                            null, productManagerId, architectInstallationId, true)
                    ],
                    [new OrganizationRole(productManagerRoleId, "Software Product Manager", "Owns outcomes.", "[]")],
                    [], [], [], DateTimeOffset.UtcNow)))
            .RegisterCapability<StartAgentCoordinationRequest, AgentCoordinationSession>(
                CommunicationCapabilities.CoordinationStart,
                (request, _) =>
                {
                    started = request;
                    var now = DateTimeOffset.UtcNow;
                    return Task.FromResult(new AgentCoordinationSession(
                        Guid.NewGuid(), Guid.NewGuid(), conversationId, turnId, messageId,
                        new AgentCoordinationParticipant(architectId, architectInstallationId, "Architect", "Software Architect"),
                        new AgentCoordinationParticipant(productManagerId, productManagerInstallationId, "Product Manager", "Software Product Manager"),
                        request.Subject, request.Objective, request.SuccessCriteria,
                        AgentCoordinationStatuses.Active, 1, 1, productManagerId, false, null,
                        now, now, []));
                });

        await runtime.DeliverEventAsync(
            new SoftwareArchitectAgent(),
            CommunicationEvents.MessageReceived,
            new CommunicationMessageReceivedEvent(
                Guid.NewGuid(), conversationId.ToString("D"), productManagerId.ToString("D"),
                kickoff, null, turnId, 0, messageId));

        Assert.Null(started);
        Assert.Equal("Acknowledged.", runtime.Progress[0].GetProperty("delta").GetString());
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
        var sent = new List<JsonElement>();
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
            .RegisterCapability<CreateCommunicationChat, CommunicationAction>(
                CommunicationCapabilities.ChatCreate,
                (_, _) => Task.FromResult(new CommunicationAction(
                    true,
                    null,
                    "Created",
                    new CommunicationChat(
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
            .RegisterCapability<object, CommunicationMessage>(
                CommunicationCapabilities.MessageSend,
                (request, _) =>
                {
                    sent.Add(JsonSerializer.SerializeToElement(request));
                    return Task.FromResult(new CommunicationMessage(
                        Guid.NewGuid(), 1, conversationId, selfId, "Architect", "Agent",
                        "Sent", DateTimeOffset.UtcNow));
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
        Assert.All(sent, message => Assert.Equal(conversationId,
            message.GetProperty("chatId").GetGuid()));
        Assert.Single(sent.Select(x => x.GetProperty("idempotencyKey").GetString()).Distinct());
        Assert.Equal($"software-architect:onboarding:{eventId:N}",
            sent[0].GetProperty("idempotencyKey").GetString());
        Assert.Contains("onboarded", sent[0].GetProperty("content").GetString(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ready to begin", sent[0].GetProperty("content").GetString(), StringComparison.OrdinalIgnoreCase);
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

    private static AgentTestRuntime DesignRuntime() =>
        new AgentTestRuntime().RegisterCapability<TeamRosterRequest, TeamRosterResponse>(
            PlatformCapabilities.TeamRosterRead,
            (_, _) => Task.FromResult(new TeamRosterResponse(new AgentTeamContext(
                Guid.NewGuid().ToString("D"), "delivery", "Delivery", 1,
                Guid.NewGuid().ToString("D"), "Architect",
                [
                    new AgentTeammate(Guid.NewGuid().ToString("D"), "Developer", "Human", null,
                        "Software Developer", "Peer", "Active"),
                    new AgentTeammate(Guid.NewGuid().ToString("D"), "QA", "Agent", null,
                        "Software QA", "Peer", "Active")
                ], [], 2, false))));

    private static TeamRosterResponse Roster(string developerType, string qualityType) =>
        new(new AgentTeamContext(
            Guid.NewGuid().ToString("D"), "delivery", "Delivery", 1,
            Guid.NewGuid().ToString("D"), "Architect",
            [
                new AgentTeammate(Guid.NewGuid().ToString("D"), "Developer", developerType, null,
                    "Software Developer", "Peer", "Active"),
                new AgentTeammate(Guid.NewGuid().ToString("D"), "QA", qualityType, null,
                    "Software QA", "Peer", "Active")
            ], [], 2, false));

    private static ArchitectureDesignResponse FinalizedDesign(ArchitecturePlan plan) =>
        ArchitecturePlanPolicy.FinalizeDraft(
            ValidDesignRequest(),
            plan,
            DateTimeOffset.Parse("2026-08-03T00:00:00Z"),
            new ArchitectureDeliveryProfile(
                "Human-inclusive test team.", 14, true, 1, 2));

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
            RepositoryId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
            BaseBranch = "main",
            FirstSprintSequence = 1,
            AccountableOrganizationUserId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"),
            DeveloperInstallationId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd"),
            QualityInstallationId = Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee")
        };

    private static WorkBoardDetail Board(Guid boardId) =>
        new(
            new WorkBoardSummary(boardId, "Product Team", "Approved board", false, false, 1, [])
            {
                TeamId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb")
            },
            [
                new WorkBoardColumn(Guid.NewGuid(), "Backlog", "ToDo", 0, "None", null),
                new WorkBoardColumn(Guid.NewGuid(), "Ready For Development", "ToDo", 1, "None", null)
            ],
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
            ArchitectureDeliveryProfile deliveryProfile,
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
            ArchitectureDeliveryProfile deliveryProfile,
            AgentRuntimeContext context,
            AgentSettings settings,
            CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(ArchitecturePlanSamples.MinimalValidPlan());
        }
    }
}
