namespace dEngage.Loyalty.Api.Tiers;

public sealed record CreateTierRequest(
    string Name, string DisplayName, decimal MinPoints,
    string QualifyingModel, int? QualifyingPeriodDays, int GraceDays, int SortOrder);

public sealed record UpdateTierRequest(
    string? Name, string? DisplayName, decimal? MinPoints,
    string? QualifyingModel, int? QualifyingPeriodDays, int? GraceDays);

public sealed record TierResponse(
    Guid Id, string Name, string DisplayName, decimal MinPoints,
    string QualifyingModel, int? QualifyingPeriodDays, int GraceDays, int SortOrder, DateTime CreatedAt,
    bool HasAssignedAccounts);

public sealed record ReorderTiersRequest(List<Guid> TierIds);
