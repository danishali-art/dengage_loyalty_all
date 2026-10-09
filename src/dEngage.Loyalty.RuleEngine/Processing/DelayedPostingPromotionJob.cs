using System.Globalization;
using System.Text.Json;
using dEngage.Loyalty.Ledger;
using dEngage.Loyalty.RuleEngine.Models;
using dEngage.Loyalty.Schema;
using dEngage.Loyalty.Schema.Entities;
using dEngage.Loyalty.Shared.Events;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace dEngage.Loyalty.RuleEngine.Processing;

// CR-08 (docs/scope-change-rules A8): promotes HeldPosting rows past their HoldUntil into real
// ledger entries. Same self-scheduling BackgroundService + idempotent-SQL shape as
// PointsExpirationJob/Worker (Ledger project) — reusing an already-correct pattern rather than
// inventing a new one for this new concurrency surface. Runs hourly (Consumer's
// DelayedPostingPromotionWorker), not nightly like the expiry job — a hold measured in
// fractional days needs timelier promotion than a once-a-day sweep would give.
//
// CR 2026-10-06 (docs/scope-changes/2026-10-05-rule-config-limits-by-trigger.md §3.4):
// - H1: posts Delta minus what refunds took back during the hold (HeldPostingRefund), and never
//   posts a cancelled row. Each row is promoted in its own transaction under a row lock that
//   RefundService takes too, so a refund and a promotion never both act on the same row.
// - H2: the release is announced like an immediate award — points.earned for this posting, and
//   loyalty.rule.awarded when the rule has Notify on award.
// - H3: the tier is re-evaluated when the posted wallet is the program's tier-qualifying one
//   (a failure never rolls back the posting, as in RuleEngine).
// - D15: rule and program status are deliberately NOT rechecked — the award was decided when the
//   event happened.
public sealed class DelayedPostingPromotionJob(
    LoyaltyDbContext db,
    ILedgerService ledger,
    IOutboxService outbox,
    ITierEvaluationService tierEval,
    ILogger<DelayedPostingPromotionJob> logger)
{
    public async Task<int> RunAsync(CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var due = await db.HeldPostings
            .Where(x => x.PostedAt == null && x.CancelledAt == null && x.HoldUntil <= now)
            .OrderBy(x => x.CreatedAt)
            .Take(500) // bounded batch per run — the next hourly tick picks up the rest
            .Select(x => new { x.Id, x.TenantId })
            .ToListAsync(ct);

        if (due.Count == 0) return 0;

        // Resolve each held posting's tenant slug once (LedgerService.AddEntryAsync takes the
        // slug, not the Guid HeldPosting.TenantId is stored as).
        var tenantGuids = due.Select(x => x.TenantId).Distinct().ToList();
        var slugsByGuid = await db.Tenants
            .Where(t => tenantGuids.Contains(t.Id))
            .ToDictionaryAsync(t => t.Id, t => t.Slug, ct);

        var posted = 0;
        foreach (var item in due)
        {
            ct.ThrowIfCancellationRequested();
            if (!slugsByGuid.TryGetValue(item.TenantId, out var tenantSlug))
            {
                logger.LogWarning("DelayedPostingPromotion: tenant {TenantId} not found for held posting {Id} — skipping", item.TenantId, item.Id);
                continue;
            }

            try
            {
                if (await PromoteAsync(tenantSlug, item.Id, ct))
                    posted++;
            }
            catch (Exception ex)
            {
                // The row stays unposted and is retried on the next run; its transaction rolled back.
                db.ChangeTracker.Clear();
                logger.LogError(ex, "DelayedPostingPromotion: failed to post held posting {Id} — will retry next run", item.Id);
            }
        }

        logger.LogInformation("DelayedPostingPromotion: posted {Posted}/{Due} held postings", posted, due.Count);
        return posted;
    }

    private async Task<bool> PromoteAsync(string tenantSlug, Guid heldId, CancellationToken ct)
    {
        HeldPosting held;
        CustomerAccount account;
        decimal amount;

        await using (var tx = await db.Database.BeginTransactionAsync(ct))
        {
            // Same lock RefundService takes before it reads the order's held postings (H1).
            // Sqlite (fast tests) has no FOR UPDATE.
            if (db.Database.ProviderName?.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) == true)
                await db.Database.ExecuteSqlInterpolatedAsync($"SELECT id FROM held_postings WHERE id = {heldId} FOR UPDATE", ct);

            held = await db.HeldPostings.SingleAsync(x => x.Id == heldId, ct);
            if (held.PostedAt is not null || held.CancelledAt is not null)
                return false; // a refund or another run got there first

            // Summed in memory: EF's Sqlite provider (fast tests) can't aggregate decimals.
            var refunded = (await db.HeldPostingRefunds
                    .Where(r => r.TenantId == held.TenantId && r.HeldPostingId == held.Id)
                    .Select(r => r.Delta)
                    .ToListAsync(ct))
                .Sum();
            amount = held.Delta - refunded;
            if (amount <= 0)
            {
                held.CancelledAt = DateTime.UtcNow;
                await db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
                return false;
            }

            // Read once: the override dates the posting, the name and Notify flag announce it.
            var rule = await db.Rules.AsNoTracking()
                .Where(r => r.TenantId == held.TenantId && r.Id == held.RuleId)
                .Select(r => new { r.Name, r.Configuration })
                .SingleOrDefaultAsync(ct);
            var settings = rule?.Configuration is null ? null : JsonSerializer.Deserialize<RuleSettings>(rule.Configuration);

            var entry = await ledger.AddEntryAsync(
                tenantId: tenantSlug,
                customerAccountId: held.CustomerAccountId,
                contactKey: held.ContactKey,
                delta: amount,
                reason: held.Reason,
                sourceEventId: held.SourceEventId,
                idempotencyKey: held.IdempotencyKey,
                ruleId: held.RuleId,
                metadata: held.Metadata,
                ct: ct,
                // CR 2026-10-06 Phase 5: expiry counts from the release, not from the event (§3.9.6),
                // with the rule's override as it stands at release.
                expiresAt: settings?.ExpiryOverrideDays is int days && days > 0 ? DateTime.UtcNow.AddDays(days) : null);

            held.PostedAt = DateTime.UtcNow;
            held.LedgerEntryId = entry.Id;

            // Read fresh — AddEntryAsync updates the balance via ExecuteUpdate.
            account = await db.CustomerAccounts.AsNoTracking()
                .Include(a => a.AccountType)
                .SingleAsync(a => a.Id == held.CustomerAccountId, ct);

            await EnqueueAnnouncementsAsync(tenantSlug, held, account, amount, rule?.Name ?? "", settings, ct);

            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }

        // H3 — after the commit, like RuleEngine: a tier failure never rolls back the posting.
        if (account.AccountType.IsTierQualifying)
        {
            try
            {
                await tierEval.EvaluateAsync(tenantSlug, held.ContactKey, account.AccountType.ProgramId,
                    account.AccountTypeId, held.SourceEventId, ct);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "DelayedPostingPromotion: tier evaluation failed after posting held posting {Id}", held.Id);
            }
        }

        return true;
    }

    // H2: the same messages an immediate award sends (LedgerPoster). points.earned for the
    // source event may already exist (the event's immediate rules), so a released posting gets
    // its own dedupe key; loyalty.rule.awarded was never sent at hold time, so it keeps
    // LedgerPoster's key. Both are checked first, so a rerun never enqueues twice.
    private async Task EnqueueAnnouncementsAsync(string tenantSlug, HeldPosting held, CustomerAccount account, decimal amount,
        string ruleName, RuleSettings? settings, CancellationToken ct)
    {
        var delta = amount.ToString("F2", CultureInfo.InvariantCulture);

        var earnedDedup = $"points_earned:{held.SourceEventId}:held:{held.Id}";
        if (!await db.OutboxEvents.AnyAsync(x => x.TenantId == held.TenantId && x.DedupKey == earnedDedup, ct))
        {
            await outbox.Enqueue(
                tenantSlug,
                OutboundEventTypes.PointsEarned,
                held.ContactKey,
                new
                {
                    contact_key = held.ContactKey,
                    source_event_id = held.SourceEventId,
                    accounts = new[]
                    {
                        new
                        {
                            account_type = account.AccountType.Type,
                            code = account.AccountType.Name,
                            delta,
                            balance = account.Balance.ToString("F2", CultureInfo.InvariantCulture)
                        }
                    },
                    applied_rules = new[]
                    {
                        new { rule_id = held.RuleId.ToString(), name = ruleName, delta }
                    }
                },
                dedupKey: earnedDedup);
        }

        if (settings?.NotifyOnAward != true) return;

        var awardedDedup = $"rule_awarded:{held.SourceEventId}:{held.RuleId}";
        if (await db.OutboxEvents.AnyAsync(x => x.TenantId == held.TenantId && x.DedupKey == awardedDedup, ct))
            return;

        await outbox.Enqueue(
            tenantSlug,
            OutboundEventTypes.RuleAwarded,
            held.ContactKey,
            new
            {
                contact_key = held.ContactKey,
                rule_id = held.RuleId.ToString(),
                rule_name = ruleName,
                delta,
                source_event_id = held.SourceEventId
            },
            dedupKey: awardedDedup);
    }
}
