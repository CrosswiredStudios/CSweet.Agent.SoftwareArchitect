using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CSweet.Agent.SDK;
using CSweet.WorkManagement.Contracts;

namespace CSweet.Agents.SoftwareArchitect;

public sealed record SoftwareArchitectAssessment(
    string MandateHealth,
    string PlanningHealth,
    string ArchitectureHealth,
    string DeliveryTechnicalHealth,
    string SupportHealth,
    IReadOnlyList<string> Conditions,
    IReadOnlyList<Guid> BoardIds,
    int OpenCoordinationCount,
    int BlockedStageCount,
    DateTimeOffset AssessedAt);

internal static class SoftwareArchitectConditionCodes
{
    public const string ManagerUnavailable = "manager-unavailable";
    public const string TeamMismatch = "team-mismatch";
    public const string PlanningUnconfigured = "planning-unconfigured";
    public const string AwaitingDesignApproval = "awaiting-design-approval";
    public const string ArchitectureDrift = "architecture-drift";
    public const string BacklogIncomplete = "backlog-incomplete";
    public const string DeveloperBlocked = "developer-blocked";
    public const string QaReworkRepeated = "qa-rework-repeated";
    public const string CoordinationStalled = "coordination-stalled";
    public const string CapabilityMissing = "capability-missing";
    public const string Healthy = "healthy";
}

internal static class SoftwareArchitectOperatingState
{
    public const string StateKey = "software-architect.assessment";
    public const string SchemaId = "com.csweet.software-architect.assessment";
    public const int SchemaVersion = 1;

    public static string Fingerprint(
        IReadOnlyDictionary<string, string> revisions,
        IReadOnlyList<string> conditions)
    {
        var canonical = string.Join("\n", revisions.OrderBy(x => x.Key, StringComparer.Ordinal)
            .Select(x => $"{x.Key}={x.Value}")) + "\n" +
            string.Join("\n", conditions.Order(StringComparer.Ordinal));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
    }

    public static string SourceDigest<T>(T value)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(
            value, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }
}
