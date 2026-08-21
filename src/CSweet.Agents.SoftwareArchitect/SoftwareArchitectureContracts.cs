namespace CSweet.Agents.SoftwareArchitect;

public sealed record ArchitectureDesignRequest(
    Guid BoardId,
    string? ProductGoal,
    IReadOnlyList<string>? Requirements,
    IReadOnlyList<string>? AcceptanceCriteria,
    string? IdempotencyKey,
    IReadOnlyList<string>? Constraints = null,
    IReadOnlyList<string>? NonGoals = null,
    IReadOnlyList<string>? QualityAttributes = null,
    DateTimeOffset? DesiredStartAt = null,
    int? SprintLengthDays = null,
    Guid? SourceConversationId = null)
{
    public bool OutcomeHierarchyRequired { get; init; }
    public bool RollingRefinement { get; init; }
}

public sealed record ArchitecturePlan(
    string Summary,
    string SystemContext,
    IReadOnlyList<ArchitectureComponent> Components,
    IReadOnlyList<ArchitectureInterface> Interfaces,
    IReadOnlyList<string> DataFlows,
    IReadOnlyList<ArchitectureDecision> Decisions,
    IReadOnlyList<ArchitectureQualityAttribute> QualityAttributes,
    IReadOnlyList<string> FailureModes,
    string MigrationPlan,
    string RolloutPlan,
    string RollbackPlan,
    IReadOnlyList<string> Risks,
    IReadOnlyList<string> Assumptions,
    IReadOnlyList<string> BlockingQuestions,
    IReadOnlyList<ArchitectureRequirementTrace> RequirementTraceability,
    IReadOnlyList<ArchitectureSprintPlan> Sprints)
{
    public IReadOnlyList<ArchitectureEpicPlan> OutcomeEpics { get; init; } = [];
}

public sealed record ArchitectureEpicPlan(
    string Key,
    string Title,
    string Outcome,
    IReadOnlyList<string> AcceptanceCriteria);

public sealed record ArchitectureComponent(
    string Name,
    string Responsibility,
    IReadOnlyList<string> Dependencies,
    string DependencyDirection,
    IReadOnlyList<string> ExposedInterfaces);

public sealed record ArchitectureInterface(
    string Name,
    string Provider,
    string Consumer,
    string Contract,
    string FailureBehavior);

public sealed record ArchitectureDecision(
    string Title,
    string Decision,
    string Rationale,
    IReadOnlyList<string> Alternatives,
    IReadOnlyList<string> Consequences);

public sealed record ArchitectureQualityAttribute(
    string Name,
    string Scenario,
    string Strategy,
    string Verification);

public sealed record ArchitectureRequirementTrace(
    string Requirement,
    IReadOnlyList<string> ComponentNames,
    IReadOnlyList<string> TicketKeys);

public sealed record ArchitectureSprintPlan(
    int Ordinal,
    string Name,
    string Goal,
    DateTimeOffset? StartsAt,
    DateTimeOffset? EndsAt,
    IReadOnlyList<ArchitectureTicketPlan> Tickets);

public sealed record ArchitectureTicketPlan(
    string Key,
    string Title,
    string Kind,
    string Priority,
    string Objective,
    string Context,
    IReadOnlyList<string> Requirements,
    IReadOnlyList<string> AcceptanceCriteria,
    IReadOnlyList<string> Constraints,
    IReadOnlyList<string> InterfaceAndDataChanges,
    IReadOnlyList<string> ImplementationGuidance,
    IReadOnlyList<string> Tests,
    IReadOnlyList<string> Dependencies,
    string MigrationAndRollback,
    decimal? EstimatePoints)
{
    public string? EpicKey { get; init; }
    public string? ParentStoryKey { get; init; }
}

public sealed record ArchitectureDesignResponse(
    Guid PlanId,
    string PlanHash,
    Guid BoardId,
    string ProductGoal,
    ArchitecturePlan Plan,
    DateTimeOffset PreparedAt,
    ArchitectureDeliveryProfile DeliveryProfile)
{
    public bool RollingRefinement { get; init; }
}

public sealed record ArchitectureDeliveryProfile(
    string ScheduleBasis,
    int SprintLengthDays,
    bool UsesHumanEstimates,
    int HumanDeliveryMemberCount,
    int AgentDeliveryMemberCount);

public sealed record ArchitectureAssignmentPrincipal(
    string PrincipalKind,
    Guid? OrganizationUserId = null,
    Guid? AgentInstallationId = null);

public sealed record ArchitectureApproval(
    string? ApproverRole,
    string? Rationale,
    DateTimeOffset ApprovedAt,
    Guid? SourceConversationId = null,
    Guid? SourceMessageId = null);

public sealed record ArchitecturePublishRequest(
    Guid BoardId,
    ArchitectureDesignResponse? Design,
    ArchitectureApproval? Approval,
    string? IdempotencyKey)
{
    public Guid RepositoryId { get; init; }
    public string BaseBranch { get; init; } = string.Empty;
    public int FirstSprintSequence { get; init; }
    public Guid AccountableOrganizationUserId { get; init; }
    public Guid DeveloperInstallationId { get; init; }
    public Guid QualityInstallationId { get; init; }
    public IReadOnlyList<Guid> DeveloperInstallationIds { get; init; } = [];
    public IReadOnlyList<Guid> QualityInstallationIds { get; init; } = [];
    public IReadOnlyList<ArchitectureAssignmentPrincipal> DeveloperAssignments { get; init; } = [];
    public IReadOnlyList<ArchitectureAssignmentPrincipal> QualityAssignments { get; init; } = [];
}

public sealed record ArchitecturePublishResponse(
    Guid PlanId,
    Guid EpicId,
    IReadOnlyList<PublishedSprint> Sprints,
    IReadOnlyList<PublishedTicket> Tickets,
    DateTimeOffset PublishedAt)
{
    public bool DeliveryFinalized { get; init; }
    public IReadOnlyList<PublishedEpic> Epics { get; init; } = [];
}

public sealed record PublishedEpic(string Key, Guid ItemId, string Title);
public sealed record PublishedSprint(int Ordinal, Guid SprintId, string Name);
public sealed record PublishedTicket(string Key, Guid ItemId, Guid SprintId, string Kind);

public sealed record AssistantCapabilityInput(
    Guid ProviderProfileId,
    string ConversationId,
    string Prompt,
    IReadOnlyDictionary<string, string>? Context,
    string? UserId = null,
    Guid MessageId = default,
    Guid ChatTurnId = default);

public sealed record AssistantResponse(
    string ConversationId,
    string Response,
    IReadOnlyList<ProposedAction> ProposedActions,
    DateTimeOffset CreatedAt);

public sealed record ProposedAction(
    string ActionType,
    string Summary,
    string ParametersJson,
    bool RequiresApproval);

public sealed record AssistantResponseChunk(
    string ConversationId,
    int Sequence,
    string Delta,
    bool IsFinal,
    string? Error = null,
    Guid TurnId = default,
    string Kind = "output",
    IReadOnlyDictionary<string, string>? Metadata = null,
    int Attempt = 0);
