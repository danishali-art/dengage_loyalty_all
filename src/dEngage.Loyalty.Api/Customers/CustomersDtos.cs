namespace dEngage.Loyalty.Api.Customers;

public sealed record AccountBalanceResponse(
    Guid AccountTypeId, string AccountTypeName, string AccountTypeType, decimal Balance, int? ExpirationDays);

public sealed record TierProgressResponse(
    string? CurrentTierName, string? CurrentTierDisplayName,
    string? NextTierName, string? NextTierDisplayName, decimal? NextTierMinPoints,
    decimal QualifyingPoints, DateOnly? PeriodStart, DateOnly? ExpiresAt);

public sealed record CustomerProfileResponse(
    string ContactKey, IReadOnlyList<AccountBalanceResponse> Balances, TierProgressResponse? TierProgress);

public sealed record LedgerEntryResponse(
    Guid Id, string Reason, decimal Delta, Guid AccountTypeId, string? Metadata, DateTime CreatedAt);

public sealed record TierHistoryEntryResponse(
    Guid Id, string? FromTierName, string ToTierName, decimal QualifyingPoints, DateTime CreatedAt);

public sealed record CustomerSummaryResponse(
    string ContactKey, int AccountCount, DateTime LastActivityAt);

// CR-10 (A11): MonthDay only — "MM-DD", never a full date (see CustomerBirthday remarks).
public sealed record RegisterBirthdayRequest(string MonthDay);
public sealed record BirthdayResponse(string ContactKey, string MonthDay);
