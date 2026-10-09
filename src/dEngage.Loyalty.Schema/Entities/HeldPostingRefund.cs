namespace dEngage.Loyalty.Schema.Entities;

// CR 2026-10-06 H1: the part of a HeldPosting taken back by a refund while it was still held.
// Append-only, one row per (held posting, refund event) — the unique key makes a redelivered
// refund a no-op, the same guarantee ledger idempotency keys give refunds of posted entries.
// The amount still to post is HeldPosting.Delta minus the sum of these rows.
public class HeldPostingRefund
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid HeldPostingId { get; set; }
    public string RefundEventId { get; set; } = default!;
    public decimal Delta { get; set; }
    public DateTime CreatedAt { get; set; }
}
