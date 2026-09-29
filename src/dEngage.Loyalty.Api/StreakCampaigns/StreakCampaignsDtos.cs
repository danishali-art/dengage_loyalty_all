using dEngage.Loyalty.RuleEngine.Campaigns.Streak;
using dEngage.Loyalty.RuleEngine.Models;

namespace dEngage.Loyalty.Api.StreakCampaigns;

// Reuses RuleEngine types (ConditionClause, StreakConfig) as the wire DTOs — same rationale as
// RulesDtos.cs: these already carry the exact [JsonPropertyName] contract the engine
// (CampaignConfigCacheService, StreakConfig.Validate) reads back out of the DB.
public sealed record CreateStreakCampaignRequest(
    string Name, string Trigger, Guid TargetAccountTypeId,
    List<ConditionClause>? Conditions, StreakConfig Config,
    DateTime? ActiveFrom, DateTime? ActiveTo);

public sealed record UpdateStreakCampaignRequest(
    string? Name, string? Trigger, Guid? TargetAccountTypeId,
    List<ConditionClause>? Conditions, StreakConfig? Config,
    DateTime? ActiveFrom, DateTime? ActiveTo);

public sealed record StreakCampaignResponse(
    Guid Id, string Name, string Trigger, Guid TargetAccountTypeId,
    List<ConditionClause>? Conditions, StreakConfig Config,
    DateTime? ActiveFrom, DateTime? ActiveTo,
    string Status, DateTime CreatedAt, DateTime UpdatedAt);

public sealed record SetStreakCampaignStatusRequest(string Status);

public sealed record StreakCampaignListFilter(string? Event, string? Status);
