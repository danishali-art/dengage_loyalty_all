namespace dEngage.Loyalty.Api.ConfigVersions;

public sealed record ConfigVersionSummary(
    Guid Id, string EntityType, Guid EntityId, int VersionNumber,
    string ChangeType, string? ChangeSummary, string ChangedBy, DateTime ChangedAt);

public sealed record ConfigVersionDetail(
    Guid Id, string EntityType, Guid EntityId, int VersionNumber,
    string ChangeType, string? ChangeSummary, string ChangedBy, DateTime ChangedAt, string Snapshot);
