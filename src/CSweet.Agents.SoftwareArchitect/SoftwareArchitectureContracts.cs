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
    Guid? SourceConversationId = null);

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
    IReadOnlyList<ArchitectureSprintPlan> Sprints);

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
    decimal? EstimatePoints);

public sealed record ArchitectureDesignResponse(
    Guid PlanId,
    string PlanHash,
    Guid BoardId,
    string ProductGoal,
    ArchitecturePlan Plan,
    DateTimeOffset PreparedAt);

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
    public Guid RepositoryConnectionId { get; init; }
    public string? BaseBranch { get; init; }
    public int FirstSprintSequence { get; init; }
    public Guid AccountableOrganizationUserId { get; init; }
    public Guid DeveloperInstallationId { get; init; }
    public Guid QualityInstallationId { get; init; }
}

public sealed record ArchitecturePublishResponse(
    Guid PlanId,
    Guid EpicId,
    IReadOnlyList<PublishedSprint> Sprints,
    IReadOnlyList<PublishedTicket> Tickets,
    DateTimeOffset PublishedAt);

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

public sealed record UserMessageReceived(
    Guid ProviderProfileId,
    string ConversationId,
    string UserId,
    string Message,
    IReadOnlyDictionary<string, string>? Context,
    Guid TurnId = default,
    int Attempt = 0,
    Guid MessageId = default);

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
