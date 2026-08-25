using System.Text.Json;
using CSweet.WorkManagement.Contracts;

namespace CSweet.Agents.SoftwareArchitect;

internal static class IncrementalPlanningArtifactTypes
{
    public const string ProductBrief = ArchitecturePlanningArtifactTypes.ProductBrief;
    public const string ArchitectureBrief = ArchitecturePlanningArtifactTypes.ArchitectureBrief;
    public const string DesignProposal = ArchitecturePlanningArtifactTypes.DesignProposal;
    public const string ArchitectureDecision = ArchitecturePlanningArtifactTypes.ArchitectureDecision;
    public const string StoryProposal = ArchitecturePlanningArtifactTypes.StoryProposal;
    public const string StoryProposalV2 = ArchitecturePlanningArtifactTypes.StoryProposalV2;
    public const string TaskProposal = ArchitecturePlanningArtifactTypes.TaskProposal;
    public const string TaskProposalV2 = ArchitecturePlanningArtifactTypes.TaskProposalV2;
    public const string SupportRequest = ArchitecturePlanningArtifactTypes.SupportRequest;
    public const string Guidance = ArchitecturePlanningArtifactTypes.Guidance;
    public const string Question = ArchitecturePlanningArtifactTypes.Question;
    public const string QuestionV2 = ArchitecturePlanningArtifactTypes.QuestionV2;
}

public sealed record SoftwareDevelopmentSupportRequest(
    string BlockerCategory,
    IReadOnlyList<string> SanitizedDiagnostics,
    IReadOnlyList<string> AttemptedSteps,
    IReadOnlyList<string> FailedValidations,
    string Question,
    long AssignmentRevision);

public sealed record SoftwareArchitectureGuidance(
    string Diagnosis,
    IReadOnlyList<string> OrderedNextSteps,
    IReadOnlyList<string> Invariants,
    IReadOnlyList<string> RelevantDesignDecisions,
    IReadOnlyList<string> Verification,
    IReadOnlyList<string> RemainingRisks,
    bool RequiresArchitectureApproval,
    string? ApprovalReason);

internal static class IncrementalPlanningJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
}
