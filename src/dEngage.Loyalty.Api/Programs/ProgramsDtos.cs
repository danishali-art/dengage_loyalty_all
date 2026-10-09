using dEngage.Loyalty.Shared;
using System.Text.Json;

namespace dEngage.Loyalty.Api.Programs;

// 1.3.CL: Status on create, QualifyingAccountTypeId and WarningDays must now be omitted (see
// ProgramsValidators) — a new program always starts Draft + inactive, and the two settings moved
// to the account type. They stay on the records so old clients get a clear 400.
// Slug: CR 2026-09-30 (A5) — the program slug that prefixes reward names. Optional on create
// (derived from the name when omitted); changeable only while the program is a Draft.
// DefaultRounding: CR 2026-10-06 Phase 4 — the direction rules inherit (down | nearest | up);
// optional, "down" when omitted.
public sealed record CreateProgramRequest(string Name, string? Description, string? Status = null, Guid? QualifyingAccountTypeId = null, int? WarningDays = null, string? Slug = null, string? DefaultRounding = null);
public sealed record UpdateProgramRequest(string? Name, string? Description, string? Status, Guid? QualifyingAccountTypeId = null, int? WarningDays = null, string? Slug = null, string? DefaultRounding = null);

// QualifyingAccountTypeId / WarningDays are deprecated (derived from the account type flag /
// always null) for one release. PublicationStatus..PublishedBy are 1.3.CL item 8.
public sealed record ProgramResponse(
    Guid Id, string Name, string? Description, string Status,
    Guid? QualifyingAccountTypeId, int? WarningDays, DateTime CreatedAt,
    int AccountTypeCount, int RuleCount,
    string PublicationStatus, bool HasUnpublishedChanges, int? PublishedVersion,
    DateTime? PublishedAt, string? PublishedBy, string Slug, string DefaultRounding = RoundingDirection.Down);

// 1.3.CL item 9: the aggregate ConfigVersion snapshot written on Publish (EntityType
// "ProgramPublication"). Built from these projections — never from tracked EF entities — so no
// navigation property can leak into the audit record. Money/points are strings, per the
// decimal-on-the-wire contract; jsonb configs are embedded as JSON, not escaped strings.
public sealed record ProgramPublicationSnapshot(
    PublishedProgram Program,
    IReadOnlyList<PublishedAccountType> AccountTypes,
    IReadOnlyList<PublishedTier> Tiers,
    IReadOnlyList<PublishedReward> Rewards,
    IReadOnlyList<PublishedRule> Rules,
    IReadOnlyList<PublishedStreakCampaign> StreakCampaigns);

public sealed record PublishedProgram(Guid Id, string Name, string? Description, string Status, string Slug, string DefaultRounding = RoundingDirection.Down);
public sealed record PublishedAccountType(Guid Id, string Type, string Name, JsonElement Config, bool IsTierQualifying);
public sealed record PublishedTier(
    Guid Id, string Name, string DisplayName, string MinPoints, int? QualifyingDays, int GraceDays, int SortOrder);
public sealed record PublishedReward(
    Guid Id, string Name, string DisplayName, string Acquisition, string RewardType,
    string? PointsPrice, Guid? PointsAccountTypeId, JsonElement TypeConfig, bool IsActive,
    string Status);
// RuleId + Version is the same pair ledger postings reference (CR-09), so a publish can be
// matched to exactly the rule versions that were live.
public sealed record PublishedRule(
    Guid RuleId, int Version, string Name, string Type, string Trigger, string Status, string? Template,
    Guid? TargetAccountTypeId, int Priority, bool Stackable,
    JsonElement? Conditions, JsonElement Calculation, JsonElement? Limits, JsonElement? Configuration,
    DateTime? ActiveFrom, DateTime? ActiveTo);
public sealed record PublishedStreakCampaign(
    Guid Id, string Name, string Trigger, string Status, Guid TargetAccountTypeId,
    JsonElement? Conditions, JsonElement Config, DateTime? ActiveFrom, DateTime? ActiveTo);
