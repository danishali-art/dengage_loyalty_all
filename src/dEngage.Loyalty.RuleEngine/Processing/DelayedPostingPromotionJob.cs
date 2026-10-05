using dEngage.Loyalty.Ledger;
using dEngage.Loyalty.Schema;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace dEngage.Loyalty.RuleEngine.Processing;

// CR-08 (docs/scope-change-rules A8): promotes HeldPosting rows past their HoldUntil into real
// ledger entries. Same self-scheduling BackgroundService + idempotent-SQL shape as
// PointsExpirationJob/Worker (Ledger project) — reusing an already-correct pattern rather than
// inventing a new one for this new concurrency surface. Runs hourly (Consumer's
// DelayedPostingPromotionWorker), not nightly like the expiry job — a hold measured in
// fractional days needs timelier promotion than a once-a-day sweep would give.
public sealed class DelayedPostingPromotionJob(
    LoyaltyDbContext db,
    ILedgerService ledger,
    ILogger<DelayedPostingPromotionJob> logger)
{
    public async Task<int> RunAsync(CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var due = await db.HeldPostings
            .Where(x => x.PostedAt == null && x.HoldUntil <= now)
            .OrderBy(x => x.CreatedAt)
            .Take(500) // bounded batch per run — the next hourly tick picks up the rest
            .ToListAsync(ct);

        if (due.Count == 0) return 0;

        // Resolve each held posting's tenant slug once (LedgerService.AddEntryAsync takes the
        // slug, not the Guid HeldPosting.TenantId is stored as).
        var tenantGuids = due.Select(x => x.TenantId).Distinct().ToList();
        var slugsByGuid = await db.Tenants
            .Where(t => tenantGuids.Contains(t.Id))
            .ToDictionaryAsync(t => t.Id, t => t.Slug, ct);

        var posted = 0;
        foreach (var held in due)
        {
            ct.ThrowIfCancellationRequested();
            if (!slugsByGuid.TryGetValue(held.TenantId, out var tenantSlug))
            {
                logger.LogWarning("DelayedPostingPromotion: tenant {TenantId} not found for held posting {Id} — skipping", held.TenantId, held.Id);
                continue;
            }

            try
            {
                var entry = await ledger.AddEntryAsync(
                    tenantId: tenantSlug,
                    customerAccountId: held.CustomerAccountId,
                    contactKey: held.ContactKey,
                    delta: held.Delta,
                    reason: held.Reason,
                    sourceEventId: held.SourceEventId,
                    idempotencyKey: held.IdempotencyKey,
                    ruleId: held.RuleId,
                    metadata: held.Metadata,
                    ct: ct);

                held.PostedAt = DateTime.UtcNow;
                held.LedgerEntryId = entry.Id;
                posted++;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "DelayedPostingPromotion: failed to post held posting {Id} (rule={RuleId} contact={Contact}) — will retry next run",
                    held.Id, held.RuleId, held.ContactKey);
            }
        }

        await db.SaveChangesAsync(ct);
        logger.LogInformation("DelayedPostingPromotion: posted {Posted}/{Due} held postings", posted, due.Count);
        return posted;
    }
}
