namespace dEngage.Loyalty.Schema.Entities;

public class CustomerAccount
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string ContactKey { get; set; } = default!;
    public Guid AccountTypeId { get; set; }
    public decimal Balance { get; set; }
    public DateTime UpdatedAt { get; set; }

    public Guid? TierId { get; set; }
    public decimal TierQualifyingPts { get; set; }
    public DateOnly? TierPeriodStart { get; set; }
    public DateOnly? TierExpiresAt { get; set; }

    // CR 2026-09-30 (§3.6): set by a tier-upgrade reward with duration_days. TierDowngradeJob
    // leaves the account alone until this date (UTC); after it, the normal grace/downgrade
    // path applies again. Deliberately not TierExpiresAt, which means "grace ends".
    public DateOnly? TierLockedUntil { get; set; }

    public AccountType AccountType { get; set; } = default!;
    public TierDefinition? Tier { get; set; }
    public ICollection<LedgerEntry> LedgerEntries { get; set; } = [];
}
