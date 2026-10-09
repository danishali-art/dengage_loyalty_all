namespace dEngage.Loyalty.Schema.Entities;

// CR-08 (docs/scope-change-rules A8): a rule fire whose Configuration.posting is "Delayed" —
// the calculation already ran (Delta is fixed at hold time), but the ledger entry itself is
// deferred until HoldUntil, when DelayedPostingPromotionJob posts it for real. Plain entity
// (not ITenantScopedEntity/IRepository<T>) — same "loose references, no DB-level FK" pattern
// RuleFireAudit already uses, accessed directly via LoyaltyDbContext.
public class HeldPosting
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid RuleId { get; set; }
    public Guid CustomerAccountId { get; set; }
    public string ContactKey { get; set; } = default!;
    public decimal Delta { get; set; }
    public string Reason { get; set; } = default!;
    public string SourceEventId { get; set; } = default!;
    public string IdempotencyKey { get; set; } = default!;
    public string? Metadata { get; set; }
    public DateTime HoldUntil { get; set; }
    public DateTime CreatedAt { get; set; }

    // Set by DelayedPostingPromotionJob once actually posted — null means still held.
    public DateTime? PostedAt { get; set; }
    public Guid? LedgerEntryId { get; set; }

    // CR 2026-10-06 H1: set when refunds (HeldPostingRefund rows) have taken back the whole
    // Delta during the hold — the promotion job never posts a cancelled row. A partly refunded
    // row stays null and is posted for Delta minus what was refunded.
    public DateTime? CancelledAt { get; set; }
}
