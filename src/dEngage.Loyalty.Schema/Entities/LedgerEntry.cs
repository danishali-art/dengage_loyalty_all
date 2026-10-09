namespace dEngage.Loyalty.Schema.Entities;

public class LedgerEntry
{
    public Guid Id { get; set; }
    public string TenantId { get; set; } = default!;
    public Guid CustomerAccountId { get; set; }
    public string ContactKey { get; set; } = default!;
    public decimal Delta { get; set; }
    public string Reason { get; set; } = default!;
    public string SourceEventId { get; set; } = default!;
    public Guid? RuleId { get; set; }
    public string IdempotencyKey { get; set; } = default!;
    public string? Metadata { get; set; }
    public DateTime CreatedAt { get; set; }

    // CR 2026-10-06 Phase 5 (expiry override): when the points of an earn / transfer_in entry
    // expire — the earning rule's Configuration.expiryOverrideDays, otherwise the POINTS wallet's
    // expiration_days, counted from the posting. Set once, on insert; null means "earn date +
    // the wallet's current expiration_days" (every entry written before this change) or, with no
    // wallet expiry either, never. Not set on other reasons.
    public DateTime? ExpiresAt { get; set; }

    public CustomerAccount CustomerAccount { get; set; } = default!;
    public Rule? Rule { get; set; }
}
