using dEngage.Loyalty.RuleEngine.Models;

namespace dEngage.Loyalty.Api.CardBuckets;

// Structured fields an admin actually thinks in (MCC list, amount range, geo, time window,
// frequency cap) rather than raw condition-tree JSON. CardBucketConditionMapper translates
// these to/from the same ConditionTree/RuleLimits shape RulesDtos uses under the hood — a
// card bucket IS a FixedBonusRule row (Rule.Template = "card_bucket"), just authored through a
// purpose-built form instead of the generic Rules screen.
public sealed record CreateCardBucketRequest(
    string Name,
    List<string>? MccCodes,
    decimal? AmountMin,
    decimal? AmountMax,
    string? CountryMode, // "in" | "not_in" — required when Countries is non-empty
    List<string>? Countries,
    bool RequireCaptured,
    int? HourFrom,
    int? HourTo,
    List<string>? DaysOfWeek,
    decimal? PerCustomerPerDay,
    decimal? PerCustomerTotal,
    Guid TargetAccountTypeId,
    decimal RewardAmount,
    int Priority,
    DateTime? ActiveFrom,
    DateTime? ActiveTo,
    List<ConditionLeaf>? AdditionalConditions);

public sealed record UpdateCardBucketRequest(
    string? Name,
    List<string>? MccCodes,
    decimal? AmountMin,
    decimal? AmountMax,
    string? CountryMode,
    List<string>? Countries,
    bool? RequireCaptured,
    int? HourFrom,
    int? HourTo,
    List<string>? DaysOfWeek,
    decimal? PerCustomerPerDay,
    decimal? PerCustomerTotal,
    Guid? TargetAccountTypeId,
    decimal? RewardAmount,
    int? Priority,
    DateTime? ActiveFrom,
    DateTime? ActiveTo,
    List<ConditionLeaf>? AdditionalConditions);

public sealed record CardBucketResponse(
    Guid Id,
    string Name,
    List<string>? MccCodes,
    decimal? AmountMin,
    decimal? AmountMax,
    string? CountryMode,
    List<string>? Countries,
    bool RequireCaptured,
    int? HourFrom,
    int? HourTo,
    List<string>? DaysOfWeek,
    decimal? PerCustomerPerDay,
    decimal? PerCustomerTotal,
    Guid TargetAccountTypeId,
    decimal RewardAmount,
    int Priority,
    DateTime? ActiveFrom,
    DateTime? ActiveTo,
    List<ConditionLeaf>? AdditionalConditions,
    string Status,
    DateTime CreatedAt,
    DateTime UpdatedAt);

public sealed record SetCardBucketStatusRequest(string Status);

public sealed record CardBucketListFilter(string? Status);
