namespace dEngage.Loyalty.Api.Customers;

// CR 2026-10-02 (Customer 360): ProgramId/ProgramName added (additive) so the portal can build the
// program and wallet filters without reading the programs feature.
public sealed record AccountBalanceResponse(
    Guid AccountTypeId, string AccountTypeName, string AccountTypeType, decimal Balance, int? ExpirationDays,
    Guid ProgramId, string ProgramName);

public sealed record TierProgressResponse(
    string? CurrentTierName, string? CurrentTierDisplayName,
    string? NextTierName, string? NextTierDisplayName, decimal? NextTierMinPoints,
    decimal QualifyingPoints, DateOnly? PeriodStart, DateOnly? ExpiresAt);

// Summary / Programs: CR 2026-10-02 (Customer 360) P2, additive — the header and the per-program
// Overview. TierProgress stays as it was (frozen): the first account that has a tier.
public sealed record CustomerProfileResponse(
    string ContactKey, IReadOnlyList<AccountBalanceResponse> Balances, TierProgressResponse? TierProgress,
    CustomerSummaryInfoResponse? Summary = null, IReadOnlyList<ProgramOverviewResponse>? Programs = null);

// CR 2026-10-02 (Customer 360): everything after CreatedAt is additive — where the posting came
// from (event, rule + the version it was posted under, or streak campaign) and which wallet and
// program it hit. EventType is null when no inbound event exists (scheduled jobs).
public sealed record LedgerEntryResponse(
    Guid Id, string Reason, decimal Delta, Guid AccountTypeId, string? Metadata, DateTime CreatedAt,
    string SourceEventId, string? EventType,
    Guid? RuleId, string? RuleName, int? RuleVersion,
    Guid? CampaignId, string? CampaignName,
    string AccountTypeName, string AccountTypeType, Guid ProgramId, string ProgramName);

// Query filters for GET customers/{contactKey}/ledger. From/To are UTC, inclusive.
// RuleId: CR 2026-10-02 P3, additive (a card bucket's postings).
public sealed record LedgerFilter(
    Guid? AccountTypeId = null, Guid? ProgramId = null, string? ReasonGroup = null,
    DateTime? From = null, DateTime? To = null, string? EventId = null, Guid? RuleId = null);

// ── CR 2026-10-02 (Customer 360): events and the event drawer ──

// Query filters for GET customers/{contactKey}/events, on received_at. From/To are UTC, inclusive.
public sealed record CustomerEventFilter(
    string? EventType = null, string? Status = null, DateTime? From = null, DateTime? To = null);

public sealed record EventWalletOutcomeResponse(
    Guid AccountTypeId, string AccountTypeName, string AccountTypeType, int Postings, decimal NetDelta);

public sealed record CustomerEventResponse(
    string EventId, string EventType, DateTime? OccurredAt, DateTime ReceivedAt, DateTime? ProcessedAt,
    string Status, string? Error, IReadOnlyList<EventWalletOutcomeResponse> Outcome);

// Data is the event's data with sensitive fields masked (CustomerPayloadMasker); keys keep the
// snake_case the event was sent with.
public sealed record InboundEventResponse(
    string EventId, string EventType, DateTime? OccurredAt, DateTime ReceivedAt, DateTime? ProcessedAt,
    string Status, string? Error, System.Text.Json.JsonElement? Data);

// A posting made for the event. ContactKey differs from the customer's only for a transfer's
// counterparty.
public sealed record EventPostingResponse(
    Guid Id, string ContactKey, string Reason, decimal Delta, string? Metadata, DateTime CreatedAt,
    Guid? RuleId, string? RuleName, int? RuleVersion, Guid? CampaignId, string? CampaignName,
    Guid AccountTypeId, string AccountTypeName, string AccountTypeType, Guid ProgramId, string ProgramName);

public sealed record HeldPostingResponse(
    Guid Id, Guid RuleId, string? RuleName, Guid AccountTypeId, string AccountTypeName, string Reason,
    decimal Delta, DateTime HoldUntil, DateTime? PostedAt);

public sealed record RuleFireResponse(
    Guid Id, Guid RuleId, string? RuleName, int RuleVersion, decimal ResultingDelta, Guid? LedgerEntryId,
    string? ConditionsSnapshot, string CalculationSnapshot, string? ResolutionSnapshot, DateTime CreatedAt);

public sealed record StreakAppliedResponse(Guid CampaignId, string? CampaignName, DateTime AppliedAt);

public sealed record StreakCompletionResponse(
    Guid CampaignId, string? CampaignName, int CompletionNo, DateOnly CompletedPeriod, int Periods,
    string RewardKind, string? RewardRef, DateTime CreatedAt);

public sealed record RewardLogResponse(
    Guid Id, string RewardName, Guid? RewardDefinitionId, string Status, int CompletionCount,
    DateTime CreatedAt, DateTime? DeliveredAt);

public sealed record TierChangeResponse(
    Guid Id, string? FromTierName, string ToTierName, decimal QualifyingPoints, DateTime CreatedAt);

// D5: no payload — type, status and delivery facts only. Addendum C (2026-10-06, D5 amended):
// plus the payload's `reason`, the one field that says why a *_failed message was sent
// (e.g. "no_rule"); null for messages without one. Nothing else from the payload is exposed.
public sealed record SentMessageResponse(
    Guid EventId, string EventType, string Status, int Attempts, string? DedupKey,
    DateTime CreatedAt, DateTime? PublishedAt, string? Reason = null);

// Event is null when the id has no inbound event (a scheduled job's postings).
public sealed record CustomerEventDetailResponse(
    string EventId, InboundEventResponse? Event,
    IReadOnlyList<EventPostingResponse> Postings,
    IReadOnlyList<HeldPostingResponse> HeldPostings,
    IReadOnlyList<RuleFireResponse> RuleFires,
    IReadOnlyList<StreakAppliedResponse> StreaksApplied,
    IReadOnlyList<StreakCompletionResponse> StreakCompletions,
    IReadOnlyList<RewardLogResponse> Rewards,
    IReadOnlyList<TierChangeResponse> TierChanges,
    IReadOnlyList<SentMessageResponse> Messages);

// ProgramId..SourceEventId: CR 2026-10-02 P2, additive. Cause is "points" (an inbound event),
// "reward" (a tier-upgrade reward) or "downgrade" (the nightly job) — see TierChangeCause.
public sealed record TierHistoryEntryResponse(
    Guid Id, string? FromTierName, string ToTierName, decimal QualifyingPoints, DateTime CreatedAt,
    Guid? ProgramId = null, string? ProgramName = null, string? Cause = null, string? SourceEventId = null);

public sealed record CustomerSummaryResponse(
    string ContactKey, int AccountCount, DateTime LastActivityAt);

// CR-10 (A11): MonthDay only — "MM-DD", never a full date (see CustomerBirthday remarks).
public sealed record RegisterBirthdayRequest(string MonthDay);
public sealed record BirthdayResponse(string ContactKey, string MonthDay);

// ── CR 2026-10-02 (Customer 360) P2: header, per-program overview, rules & caps, streaks, rewards ──

// FirstSeenAt: the customer's earliest posting (absent when there is none). FailedEventsLast7Days
// counts inbox rows carrying the customer's contact_key, i.e. events received after the CR.
public sealed record CustomerSummaryInfoResponse(
    DateTime? FirstSeenAt, DateTime LastActivityAt, int FailedEventsLast7Days, int ActiveStreakCount);

public sealed record ProgramOverviewResponse(
    Guid ProgramId, string ProgramName,
    IReadOnlyList<WalletOverviewResponse> Wallets,
    ProgramTierStatusResponse? Tier,
    IReadOnlyList<StreakSummaryResponse> Streaks);

// PendingAmount: delayed postings not yet released. ExpiringAmount/ExpiresOn: the same FIFO
// figure PointsExpiringDetectorJob warns about — only for POINTS wallets with expiration_days and
// warning_days; absent otherwise or when nothing is about to expire.
public sealed record WalletOverviewResponse(
    Guid AccountTypeId, string Name, string Type, decimal Balance, string? Currency,
    decimal PendingAmount, decimal? ExpiringAmount, DateOnly? ExpiresOn);

// The program's tier-qualifying account. GraceEndsAt is customer_accounts.tier_expires_at (the
// date the nightly downgrade job reviews the tier); LockedUntil is a tier-upgrade reward's lock.
public sealed record ProgramTierStatusResponse(
    string? TierName, string? TierDisplayName,
    string? NextTierName, string? NextTierDisplayName, decimal? NextTierMinPoints,
    decimal QualifyingPoints, DateOnly? PeriodStart, DateOnly? GraceEndsAt, DateOnly? LockedUntil);

public sealed record StreakSummaryResponse(
    Guid CampaignId, string CampaignName, int StreakCount, int TargetPeriods, int Completions, string Status);

// GET customers/{contactKey}/rule-fires filters. From/To are UTC, inclusive, on created_at.
public sealed record RuleFireFilter(Guid? RuleId = null, DateTime? From = null, DateTime? To = null);

public sealed record CustomerRuleFireResponse(
    Guid Id, Guid RuleId, string? RuleName, int RuleVersion, string SourceEventId, string? EventType,
    decimal ResultingDelta, Guid? LedgerEntryId,
    string? ConditionsSnapshot, string CalculationSnapshot, string? ResolutionSnapshot, DateTime CreatedAt);

// D7: usage is computed from the ledger the same way the engine counts it (earn, stamp_earn and
// refund postings for this rule and customer): today = the current UTC day, period = the rule's
// limits.period / reset_window window (PeriodWindow), total = all time.
public sealed record RuleCapUsageResponse(
    Guid RuleId, string RuleName, Guid ProgramId, string ProgramName, string Status, bool IsCardBucket,
    decimal? PerCustomerTotal, decimal UsedTotal,
    decimal? PerCustomerPerDay, decimal UsedToday,
    decimal? PerCustomerPerPeriod, string? Period, string? ResetWindow, DateTime? PeriodStart, decimal? UsedThisPeriod);

// Latest*: the most recent period the engine recorded for the customer (streak_period_state) —
// not necessarily the current one; LastMetPeriod / StreakCount are shown as stored.
public sealed record CustomerStreakResponse(
    Guid CampaignId, string CampaignName, Guid ProgramId, string ProgramName, string CampaignStatus,
    string Period, int TargetPeriods, string Metric, decimal Threshold, string RewardKind,
    int StreakCount, DateOnly? LastMetPeriod, int Completions, string Status,
    DateOnly? LatestPeriodStart, decimal? LatestAggSum, int? LatestAggCount, bool? LatestPeriodMet,
    IReadOnlyList<StreakCompletionResponse> History);

// Source: "points_purchase" | "stamp_completion" (reward_log) or "streak_completion" (a streak
// granting a reward definition, recorded in streak_log). Outcome: "cash_credited",
// "tier_upgraded" or "none" (nothing paid: e.g. pending approval, program not live, already at
// or above the tier, or a stamp reward fulfilled outside the platform).
public sealed record CustomerRewardResponse(
    string Source, string RewardName, Guid? RewardDefinitionId, string? RewardType, string SourceEventId,
    decimal? Cost, string? CostWalletName,
    string Outcome, decimal? CashAmount, string? CashWalletName, string? TierName,
    string? Status, DateTime CreatedAt);

// ── CR 2026-10-02 (Customer 360) P3: card buckets, messages sent ──

// Every non-deleted card bucket in the customer's programs. Usage as RuleCapUsageResponse (D7):
// this customer's earn + stamp_earn + refund postings for the bucket's rule.
public sealed record CustomerCardBucketResponse(
    Guid RuleId, string Name, Guid ProgramId, string ProgramName, string Status, decimal? RewardAmount,
    decimal? PerCustomerPerDay, decimal UsedToday, decimal? PerCustomerTotal, decimal UsedTotal,
    int Postings, DateTime? LastPostedAt);

// GET customers/{contactKey}/messages filters. From/To are UTC, inclusive, on created_at.
public sealed record MessageFilter(string? EventType = null, string? Status = null, DateTime? From = null, DateTime? To = null);
