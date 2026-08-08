using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CSweet.Agent.SDK;
using CSweet.WorkManagement.Contracts;

namespace CSweet.Agents.SoftwareArchitect;

internal static class ArchitecturePlanPolicy
{
    private const int MaximumListItems = 100;
    private const int MaximumTextLength = 8_000;
    private const int MaximumTickets = 40;
    private static readonly string[] RequiredQualityAttributes =
        ["Security", "Reliability", "Performance", "Observability", "Maintainability", "Testability"];
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    internal static string? ValidateDesignRequest(ArchitectureDesignRequest? request)
    {
        if (request is null)
            return "The request payload is required.";
        if (request.BoardId == Guid.Empty)
            return "boardId is required.";
        if (string.IsNullOrWhiteSpace(request.ProductGoal))
            return "productGoal is required.";
        if (request.ProductGoal.Length > MaximumTextLength)
            return $"productGoal must be at most {MaximumTextLength} characters.";
        if (request.Requirements is null || request.Requirements.Count == 0)
            return "at least one requirement is required.";
        if (request.AcceptanceCriteria is null || request.AcceptanceCriteria.Count == 0)
            return "at least one acceptance criterion is required.";
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
            return "idempotencyKey is required.";
        if (request.SprintLengthDays is < 1 or > 30)
            return "sprintLengthDays must be between 1 and 30.";

        return ValidateList(request.Requirements, "requirements")
            ?? ValidateList(request.AcceptanceCriteria, "acceptanceCriteria")
            ?? ValidateList(request.Constraints, "constraints")
            ?? ValidateList(request.NonGoals, "nonGoals")
            ?? ValidateList(request.QualityAttributes, "qualityAttributes");
    }

    internal static string? ValidatePlan(
        ArchitecturePlan? plan,
        bool forPublication,
        ArchitectureDeliveryProfile? deliveryProfile = null)
    {
        if (plan is null)
            return "The architecture plan is required.";
        if (string.IsNullOrWhiteSpace(plan.Summary))
            return "The architecture plan summary is required.";
        if (string.IsNullOrWhiteSpace(plan.SystemContext))
            return "The system context is required.";
        if (plan.Components is null || plan.Components.Count == 0)
            return "At least one cohesive component is required.";
        if (plan.Interfaces is null || plan.Interfaces.Count == 0)
            return "At least one explicit interface contract is required.";
        if (plan.DataFlows is null || plan.DataFlows.Count == 0)
            return "At least one data flow is required.";
        if (plan.Decisions is null || plan.Decisions.Count == 0)
            return "At least one architecture decision with alternatives is required.";
        if (plan.QualityAttributes is null || plan.QualityAttributes.Count == 0)
            return "At least one quality-attribute strategy is required.";
        if (plan.FailureModes is null || plan.FailureModes.Count == 0)
            return "At least one failure mode and response is required.";
        if (string.IsNullOrWhiteSpace(plan.MigrationPlan))
            return "A migration plan is required.";
        if (string.IsNullOrWhiteSpace(plan.RolloutPlan))
            return "A rollout plan is required.";
        if (string.IsNullOrWhiteSpace(plan.RollbackPlan))
            return "A rollback plan is required.";
        if (plan.Risks is null || plan.Assumptions is null || plan.BlockingQuestions is null)
            return "Risks, assumptions, and unresolved product decisions are required collections.";
        if (forPublication && plan.BlockingQuestions.Count > 0)
            return "The plan has unresolved product decisions and cannot be published.";
        if (plan.Sprints is null || plan.Sprints.Count == 0)
            return "At least one incremental sprint is required.";
        if (plan.Sprints.Any(x => x.Tickets is null))
            return "Every sprint requires a tickets collection.";
        if (plan.Sprints.SelectMany(x => x.Tickets).Count() > MaximumTickets)
            return $"A plan may contain at most {MaximumTickets} tickets.";

        var componentNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var component in plan.Components)
        {
            if (string.IsNullOrWhiteSpace(component.Name) ||
                string.IsNullOrWhiteSpace(component.Responsibility) ||
                string.IsNullOrWhiteSpace(component.DependencyDirection))
                return "Every component requires a name, one cohesive responsibility, and dependency direction.";
            if (!componentNames.Add(component.Name))
                return $"Component name '{component.Name}' is duplicated.";
        }

        foreach (var contract in plan.Interfaces)
        {
            if (string.IsNullOrWhiteSpace(contract.Name) ||
                string.IsNullOrWhiteSpace(contract.Provider) ||
                string.IsNullOrWhiteSpace(contract.Consumer) ||
                string.IsNullOrWhiteSpace(contract.Contract) ||
                string.IsNullOrWhiteSpace(contract.FailureBehavior))
                return "Every interface requires a name, provider, consumer, contract, and failure behavior.";
        }

        foreach (var decision in plan.Decisions)
        {
            if (string.IsNullOrWhiteSpace(decision.Title) ||
                string.IsNullOrWhiteSpace(decision.Decision) ||
                string.IsNullOrWhiteSpace(decision.Rationale) ||
                decision.Alternatives is null ||
                decision.Alternatives.Count == 0 ||
                decision.Consequences is null ||
                decision.Consequences.Count == 0)
                return "Every architecture decision requires rationale, alternatives, and consequences.";
        }

        foreach (var quality in plan.QualityAttributes)
        {
            if (string.IsNullOrWhiteSpace(quality.Name) ||
                string.IsNullOrWhiteSpace(quality.Scenario) ||
                string.IsNullOrWhiteSpace(quality.Strategy) ||
                string.IsNullOrWhiteSpace(quality.Verification))
                return "Every quality attribute requires a scenario, strategy, and verification.";
        }

        var missingQualityAttributes = RequiredQualityAttributes
            .Where(required => !plan.QualityAttributes.Any(
                quality => quality.Name.Contains(required, StringComparison.OrdinalIgnoreCase)))
            .ToArray();
        if (missingQualityAttributes.Length > 0)
            return $"Quality-attribute treatment is missing: {string.Join(", ", missingQualityAttributes)}.";

        var ticketKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var ordinals = new HashSet<int>();
        foreach (var sprint in plan.Sprints)
        {
            if (sprint.Ordinal < 1 || !ordinals.Add(sprint.Ordinal))
                return "Sprint ordinals must be positive and unique.";
            if (string.IsNullOrWhiteSpace(sprint.Name) || string.IsNullOrWhiteSpace(sprint.Goal))
                return "Every sprint requires a name and demonstrable increment goal.";
            if (sprint.StartsAt is not null && sprint.EndsAt is not null &&
                sprint.EndsAt <= sprint.StartsAt)
                return $"Sprint {sprint.Ordinal} must end after it starts.";
            if (deliveryProfile is not null && sprint.StartsAt is not null && sprint.EndsAt is not null &&
                sprint.EndsAt - sprint.StartsAt > TimeSpan.FromDays(deliveryProfile.SprintLengthDays))
                return $"Sprint {sprint.Ordinal} exceeds the {deliveryProfile.SprintLengthDays}-day team-aware execution window.";
            if (sprint.Tickets is null || sprint.Tickets.Count == 0)
                return $"Sprint {sprint.Ordinal} requires at least one independently testable ticket.";
            if (!sprint.Tickets.Any(x => x.Kind == WorkItemKinds.Story))
                return $"Sprint {sprint.Ordinal} requires a vertical, independently testable Story.";

            foreach (var ticket in sprint.Tickets)
            {
                if (string.IsNullOrWhiteSpace(ticket.Key) || !ticketKeys.Add(ticket.Key))
                    return "Ticket keys must be non-empty and unique.";
                if (string.IsNullOrWhiteSpace(ticket.Title) ||
                    string.IsNullOrWhiteSpace(ticket.Objective) ||
                    string.IsNullOrWhiteSpace(ticket.Context))
                    return $"Ticket '{ticket.Key}' requires a title, objective, and context.";
                if (ticket.Kind is not (WorkItemKinds.Story or WorkItemKinds.Task))
                    return $"Ticket '{ticket.Key}' must be a Story or Task.";
                if (ticket.Priority is not (
                    WorkPriorities.Low or WorkPriorities.Medium or
                    WorkPriorities.High or WorkPriorities.Critical))
                    return $"Ticket '{ticket.Key}' has an unsupported priority.";
                if (ticket.Requirements.Count == 0 || ticket.AcceptanceCriteria.Count == 0)
                    return $"Ticket '{ticket.Key}' requires requirements and acceptance criteria.";
                if (ticket.InterfaceAndDataChanges.Count == 0)
                    return $"Ticket '{ticket.Key}' requires explicit interface and data-change guidance.";
                if (ticket.ImplementationGuidance.Count == 0)
                    return $"Ticket '{ticket.Key}' requires ordered implementation guidance.";
                if (ticket.Tests.Count == 0)
                    return $"Ticket '{ticket.Key}' requires test guidance.";
                if (string.IsNullOrWhiteSpace(ticket.MigrationAndRollback))
                    return $"Ticket '{ticket.Key}' requires migration and rollback guidance.";
                if ((deliveryProfile?.UsesHumanEstimates ?? true) && ticket.EstimatePoints is null)
                    return $"Ticket '{ticket.Key}' requires a positive human-inclusive story-point estimate.";
                if (deliveryProfile?.UsesHumanEstimates == false && ticket.EstimatePoints is not null)
                    return $"Ticket '{ticket.Key}' must not use human story-point estimates for an agent-only team.";
                if (ticket.EstimatePoints is <= 0 or > 100)
                    return $"Ticket '{ticket.Key}' estimatePoints must be greater than 0 and at most 100 when provided.";
            }
        }

        if (!ordinals.SetEquals(Enumerable.Range(1, plan.Sprints.Count)))
            return "Sprint ordinals must be sequential beginning at 1.";
        var datedSprints = plan.Sprints.OrderBy(x => x.Ordinal).ToList();
        for (var index = 1; index < datedSprints.Count; index++)
        {
            var previous = datedSprints[index - 1];
            var current = datedSprints[index];
            if (previous.EndsAt.HasValue && current.StartsAt.HasValue && current.StartsAt < previous.EndsAt)
                return $"Sprint {current.Ordinal} overlaps sprint {previous.Ordinal}.";
        }

        var ticketSequence = plan.Sprints
            .SelectMany(sprint => sprint.Tickets.Select(ticket => new
            {
                Ticket = ticket,
                Sprint = sprint.Ordinal
            }))
            .ToDictionary(x => x.Ticket.Key, StringComparer.OrdinalIgnoreCase);
        foreach (var entry in ticketSequence.Values)
        {
            foreach (var dependencyKey in entry.Ticket.Dependencies)
            {
                if (!ticketSequence.TryGetValue(dependencyKey, out var dependency))
                    return $"Ticket '{entry.Ticket.Key}' references unknown dependency '{dependencyKey}'.";
                if (entry.Sprint < dependency.Sprint)
                    return $"Ticket '{entry.Ticket.Key}' cannot depend on later-sprint ticket '{dependencyKey}'.";
            }
        }
        var processed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (processed.Count < ticketSequence.Count)
        {
            var ready = ticketSequence.Values
                .Where(x => !processed.Contains(x.Ticket.Key) &&
                            x.Ticket.Dependencies.All(processed.Contains))
                .Select(x => x.Ticket.Key)
                .ToArray();
            if (ready.Length == 0)
                return "Ticket dependencies must be acyclic.";
            processed.UnionWith(ready);
        }

        if (plan.RequirementTraceability is null || plan.RequirementTraceability.Count == 0)
            return "Requirement traceability is required.";
        foreach (var trace in plan.RequirementTraceability)
        {
            if (string.IsNullOrWhiteSpace(trace.Requirement) ||
                trace.ComponentNames.Count == 0 ||
                trace.TicketKeys.Count == 0)
                return "Every requirement trace requires a requirement, component, and ticket.";
            if (trace.ComponentNames.Any(x => !componentNames.Contains(x)))
                return $"Requirement trace '{trace.Requirement}' references an unknown component.";
            if (trace.TicketKeys.Any(x => !ticketKeys.Contains(x)))
                return $"Requirement trace '{trace.Requirement}' references an unknown ticket.";
        }

        return null;
    }

    internal static string? ValidatePublication(ArchitecturePublishRequest? request)
    {
        if (request is null)
            return "The publication request is required.";
        if (request.BoardId == Guid.Empty)
            return "boardId is required.";
        if (request.Design is null)
            return "design is required.";
        if (request.Design.BoardId != request.BoardId)
            return "The approved design belongs to a different board.";
        if (request.Design.PlanId == Guid.Empty)
            return "The approved plan ID is required.";
        if (request.Design.DeliveryProfile is null ||
            string.IsNullOrWhiteSpace(request.Design.DeliveryProfile.ScheduleBasis) ||
            request.Design.DeliveryProfile.SprintLengthDays is < 1 or > 30 ||
            request.Design.DeliveryProfile.HumanDeliveryMemberCount < 0 ||
            request.Design.DeliveryProfile.AgentDeliveryMemberCount < 0 ||
            request.Design.DeliveryProfile.HumanDeliveryMemberCount +
            request.Design.DeliveryProfile.AgentDeliveryMemberCount == 0 ||
            request.Design.DeliveryProfile.UsesHumanEstimates !=
            (request.Design.DeliveryProfile.HumanDeliveryMemberCount > 0))
            return "The approved delivery profile is invalid.";
        if (!FixedTimeEquals(request.Design.PlanHash,
                ComputeHash(request.Design.Plan, request.Design.DeliveryProfile)))
            return "The approved plan hash does not match the plan content.";
        if (request.Approval is null)
            return "Product Manager approval is required.";
        if (string.IsNullOrWhiteSpace(request.Approval.ApproverRole) ||
            !(request.Approval.ApproverRole.Contains("Product Manager", StringComparison.OrdinalIgnoreCase) ||
              request.Approval.ApproverRole.Contains("Project Manager", StringComparison.OrdinalIgnoreCase)))
            return "Approval must come from an accountable Product or Project Manager.";
        if (string.IsNullOrWhiteSpace(request.Approval.Rationale))
            return "Approval rationale is required.";
        if (request.Approval.ApprovedAt == default)
            return "Approval time is required.";
        if (request.RepositoryId == Guid.Empty)
            return "repositoryId is required for developer-ready tickets.";
        if (request.FirstSprintSequence <= 0)
            return "firstSprintSequence must be positive.";
        if (request.AccountableOrganizationUserId == Guid.Empty)
            return "accountableOrganizationUserId is required for executable tickets.";
        var developers = NormalizeAssignmentPool(
            request.DeveloperAssignments, request.DeveloperInstallationIds, request.DeveloperInstallationId);
        var quality = NormalizeAssignmentPool(
            request.QualityAssignments, request.QualityInstallationIds, request.QualityInstallationId);
        if (developers.Count == 0)
            return "At least one Developer assignment is required for executable tickets.";
        if (quality.Count == 0)
            return "At least one Software QA assignment is required for executable tickets.";
        if (developers.Intersect(quality).Any())
            return "Developer and Software QA assignment pools must use different team members.";
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
            return "idempotencyKey is required.";
        return ValidatePlan(request.Design.Plan, forPublication: true, request.Design.DeliveryProfile);
    }

    internal static ArchitectureDeliveryProfile BuildDeliveryProfile(
        TeamRosterResponse roster,
        int? requestedSprintLengthDays,
        int defaultHumanSprintLengthDays)
    {
        var deliveryMembers = roster.Team?.Members
            .Where(x => !x.Presence.Equals("Inactive", StringComparison.OrdinalIgnoreCase) &&
                        (NormalizeRole(x.TeamRole ?? x.CompanyRole ?? string.Empty) == NormalizeRole("Software Developer") ||
                         NormalizeRole(x.TeamRole ?? x.CompanyRole ?? string.Empty) == NormalizeRole("Software QA")))
            .ToList() ?? [];
        if (deliveryMembers.Count == 0)
            throw new ArchitectureDesignException(
                "The active approved Developer and Software QA team composition is required before scheduling delivery.");
        var humans = deliveryMembers.Count(x =>
            x.EmployeeType.Equals("Human", StringComparison.OrdinalIgnoreCase));
        var agents = deliveryMembers.Count(x =>
            x.EmployeeType.Equals("Agent", StringComparison.OrdinalIgnoreCase));
        if (humans + agents != deliveryMembers.Count)
            throw new ArchitectureDesignException(
                "Every active delivery member must be identified as a Human or Agent before scheduling delivery.");
        var usesHumanEstimates = humans > 0;
        var sprintLength = usesHumanEstimates
            ? requestedSprintLengthDays ?? defaultHumanSprintLengthDays
            : SoftwareArchitectProfile.DefaultAgentOnlySprintLengthDays;
        var basis = usesHumanEstimates
            ? $"Human-inclusive delivery team ({humans} human, {agents} agent); use human story points and a {sprintLength}-day cadence."
            : $"Agent-only delivery team ({agents} agents); use dependency depth and safe parallelism with {sprintLength}-day execution windows, without human story points or velocity assumptions.";
        return new ArchitectureDeliveryProfile(basis, sprintLength, usesHumanEstimates, humans, agents);
    }

    internal static IReadOnlyList<ArchitectureAssignmentPrincipal> NormalizeAssignmentPool(
        IReadOnlyList<ArchitectureAssignmentPrincipal>? assignments,
        IReadOnlyList<Guid>? installationIds,
        Guid legacyInstallationId)
    {
        var legacy = (installationIds ?? [])
            .Where(x => x != Guid.Empty)
            .Append(legacyInstallationId)
            .Where(x => x != Guid.Empty)
            .Select(x => new ArchitectureAssignmentPrincipal(
                WorkOrchestrationPrincipalKinds.AgentInstallation,
                AgentInstallationId: x));
        var values = (assignments ?? [])
            .Concat(legacy)
            .Where(IsValidAssignment)
            .Distinct()
            .OrderBy(AssignmentKey, StringComparer.Ordinal)
            .ToList();
        return values;
    }

    internal static ArchitectureAssignmentPrincipal AssignLeastLoaded(
        IReadOnlyList<ArchitectureAssignmentPrincipal> pool,
        IDictionary<string, decimal> assignedPoints,
        decimal estimatePoints)
    {
        decimal Load(ArchitectureAssignmentPrincipal assignment) =>
            assignedPoints.TryGetValue(AssignmentKey(assignment), out var points) ? points : 0m;
        var selected = pool
            .OrderBy(Load)
            .ThenBy(AssignmentKey, StringComparer.Ordinal)
            .First();
        assignedPoints[AssignmentKey(selected)] = Load(selected) + estimatePoints;
        return selected;
    }

    internal static string AssignmentKey(ArchitectureAssignmentPrincipal assignment) =>
        $"{assignment.PrincipalKind}:{assignment.OrganizationUserId:D}:{assignment.AgentInstallationId:D}";

    private static bool IsValidAssignment(ArchitectureAssignmentPrincipal assignment) =>
        assignment.PrincipalKind switch
        {
            WorkOrchestrationPrincipalKinds.Human =>
                assignment.OrganizationUserId is { } userId && userId != Guid.Empty &&
                !assignment.AgentInstallationId.HasValue,
            WorkOrchestrationPrincipalKinds.AgentInstallation =>
                assignment.AgentInstallationId is { } installationId && installationId != Guid.Empty &&
                !assignment.OrganizationUserId.HasValue,
            _ => false
        };

    internal static ArchitectureDesignResponse FinalizeDraft(
        ArchitectureDesignRequest request,
        ArchitecturePlan plan,
        DateTimeOffset preparedAt,
        ArchitectureDeliveryProfile deliveryProfile)
    {
        var hash = ComputeHash(plan, deliveryProfile);
        var planId = DeterministicGuid($"{request.IdempotencyKey}:{hash}");
        return new ArchitectureDesignResponse(
            planId,
            hash,
            request.BoardId,
            request.ProductGoal!.Trim(),
            plan,
            preparedAt,
            deliveryProfile);
    }

    internal static string ComputeHash(
        ArchitecturePlan plan,
        ArchitectureDeliveryProfile deliveryProfile)
    {
        var canonical = JsonSerializer.Serialize(new { Plan = plan, DeliveryProfile = deliveryProfile }, JsonOptions);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
    }

    internal static Guid DeterministicGuid(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return new Guid(bytes.AsSpan(0, 16));
    }

    internal static string BuildEpicDescription(ArchitectureDesignResponse design)
    {
        var plan = design.Plan;
        var decisions = plan.Decisions.Select(x => $"- **{x.Title}:** {x.Decision}");
        var qualities = plan.QualityAttributes.Select(x => $"- **{x.Name}:** {x.Strategy}");
        return $"""
# Objective
{design.ProductGoal}

## Architecture summary
{plan.Summary}

## Delivery timeline basis
{design.DeliveryProfile.ScheduleBasis}

## System context
{plan.SystemContext}

## Decisions
{string.Join(Environment.NewLine, decisions)}

## Quality attributes
{string.Join(Environment.NewLine, qualities)}

## Migration
{plan.MigrationPlan}

## Rollout
{plan.RolloutPlan}

## Rollback
{plan.RollbackPlan}

## Risks
{Bullets(plan.Risks)}

## Definition of done
- Every child ticket meets its acceptance criteria.
- The documented component boundaries and dependency direction are preserved.
- Quality-attribute verification and rollback evidence are attached.
""";
    }

    internal static string BuildTicketDescription(ArchitectureTicketPlan ticket)
    {
        return $"""
# Objective
{ticket.Objective}

## Context
{ticket.Context}

## Requirements
{Bullets(ticket.Requirements)}

## Acceptance criteria
{Bullets(ticket.AcceptanceCriteria)}

## Interfaces and data
{Bullets(ticket.InterfaceAndDataChanges)}

## Ordered implementation guidance
{Bullets(ticket.ImplementationGuidance)}

## Tests
{Bullets(ticket.Tests)}

## Dependencies
{Bullets(ticket.Dependencies)}

## Constraints
{Bullets(ticket.Constraints)}

## Migration and rollback
{ticket.MigrationAndRollback}

## Definition of done
- The acceptance criteria pass with recorded evidence.
- Focused tests and risk-proportionate broader validation pass.
- Public contracts, migration behavior, observability, and rollback are documented.
- No unrelated scope is introduced.
""";
    }

    private static string? ValidateList(IReadOnlyList<string>? values, string name)
    {
        if (values is null)
            return null;
        if (values.Count > MaximumListItems)
            return $"{name} must contain at most {MaximumListItems} items.";
        if (values.Any(string.IsNullOrWhiteSpace))
            return $"{name} cannot contain empty items.";
        if (values.Any(x => x.Length > MaximumTextLength))
            return $"{name} items must be at most {MaximumTextLength} characters.";
        return null;
    }

    private static bool FixedTimeEquals(string? left, string right)
    {
        if (string.IsNullOrWhiteSpace(left))
            return false;
        var leftBytes = Encoding.UTF8.GetBytes(left);
        var rightBytes = Encoding.UTF8.GetBytes(right);
        return leftBytes.Length == rightBytes.Length &&
               CryptographicOperations.FixedTimeEquals(leftBytes, rightBytes);
    }

    private static string Bullets(IReadOnlyList<string> values) =>
        values.Count == 0 ? "- None." : string.Join(Environment.NewLine, values.Select(x => $"- {x}"));

    private static string NormalizeRole(string value) =>
        new(value.Trim().ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());
}
