using System.Text.Json;
using dEngage.Loyalty.Schema;
using dEngage.Loyalty.Schema.Entities;
using dEngage.Loyalty.Shared;
using dEngage.Loyalty.Shared.Events;
using Microsoft.EntityFrameworkCore;

namespace dEngage.Loyalty.Ledger;

public class RefundService(LoyaltyDbContext db, ILedgerService ledger, IOutboxService outbox, ITenantSlugResolver tenantSlugResolver) : IRefundService
{
    public async Task<Result> ProcessRefundAsync(
        string tenantId,
        string refundEventId,
        string originalEventId,
        decimal refundRatio,
        CancellationToken ct = default)
    {
        if (db.Database.CurrentTransaction is not null)
            return await ProcessCoreAsync(tenantId, refundEventId, originalEventId, refundRatio, ct);

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var result = await ProcessCoreAsync(tenantId, refundEventId, originalEventId, refundRatio, ct);
        await tx.CommitAsync(ct);
        return result;
    }

    private static bool IsExplicitlyNonReversible(string? configJson)
    {
        if (string.IsNullOrWhiteSpace(configJson)) return false;
        try
        {
            using var doc = JsonDocument.Parse(configJson);
            return doc.RootElement.TryGetProperty("reversible", out var el) &&
                   el.ValueKind == JsonValueKind.False;
        }
        catch (JsonException)
        {
            return false; // malformed config must not silently block refunds
        }
    }

    private async Task<Result> ProcessCoreAsync(
        string tenantId,
        string refundEventId,
        string originalEventId,
        decimal refundRatio,
        CancellationToken ct)
    {
        var tenantGuid = await tenantSlugResolver.ResolveAsync(tenantId, ct);
        var isPostgres = db.Database.ProviderName?.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) == true;

        // CR 2026-10-06 H1: lock the order's held postings BEFORE reading anything, so a refund
        // and DelayedPostingPromotionJob (which takes the same lock) never both act on one row:
        // either the job posted it first — then it is a ledger entry below — or this refund
        // takes it back while it is still held. Sqlite (fast tests) has no FOR UPDATE.
        if (isPostgres)
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                SELECT id FROM held_postings
                WHERE tenant_id = {tenantGuid} AND source_event_id = {originalEventId}
                FOR UPDATE
                """, ct);

        var originalEntries = await db.LedgerEntries
            .Where(x =>
                x.TenantId == tenantId &&
                x.SourceEventId == originalEventId &&
                (x.Reason == LedgerReason.Earn || x.Reason == LedgerReason.StampEarn))
            .ToListAsync(ct);

        // CR 2026-10-06 H1: points a Delayed rule still holds for the order have no ledger entry
        // yet. Before this they made the refund fail (original_entries_not_found, dead-lettered)
        // and were posted anyway when the hold ended. Already-cancelled rows stay in the list (with
        // nothing left to take back) so a redelivered refund still finds its order and succeeds.
        var heldPostings = await db.HeldPostings
            .Where(h =>
                h.TenantId == tenantGuid &&
                h.SourceEventId == originalEventId &&
                h.PostedAt == null &&
                (h.Reason == LedgerReason.Earn || h.Reason == LedgerReason.StampEarn))
            .ToListAsync(ct);

        if (originalEntries.Count == 0 && heldPostings.Count == 0)
            return Result.Fail("original_entries_not_found");

        // CR-08 (A8): Configuration.reversible (default true). Read as raw JSON rather than
        // deserializing the full RuleEngine.Models.RuleSettings type — Ledger has no reference
        // to RuleEngine (RuleEngine already depends on Ledger; the reverse would be circular),
        // and this is the one field this path needs.
        var ruleIds = originalEntries.Where(x => x.RuleId.HasValue).Select(x => x.RuleId!.Value)
            .Concat(heldPostings.Select(h => h.RuleId))
            .Distinct().ToList();
        var nonReversibleRuleIds = ruleIds.Count == 0 ? new HashSet<Guid>() : (await db.Rules
            .Where(r => ruleIds.Contains(r.Id) && r.Configuration != null)
            .Select(r => new { r.Id, r.Configuration })
            .ToListAsync(ct))
            .Where(r => IsExplicitlyNonReversible(r.Configuration))
            .Select(r => r.Id)
            .ToHashSet();

        if (nonReversibleRuleIds.Count > 0)
        {
            originalEntries = originalEntries.Where(x => x.RuleId is null || !nonReversibleRuleIds.Contains(x.RuleId.Value)).ToList();
            heldPostings = heldPostings.Where(h => !nonReversibleRuleIds.Contains(h.RuleId)).ToList();
        }

        if (originalEntries.Count == 0 && heldPostings.Count == 0)
            return Result.Fail("original_entries_not_reversible");

        var ratio = Math.Clamp(refundRatio, 0m, 1m);
        var refundedByAccount = new Dictionary<Guid, decimal>();

        await TakeBackHeldPostingsAsync(tenantGuid, refundEventId, heldPostings, ratio, isPostgres, ct);

        foreach (var entry in originalEntries)
        {
            // Cumulative cap: combined with previous refunds against the same entry, the
            // total cannot exceed the original earning (partial refunds cannot sum past 100%).
            // CR 2026-10-06 D22: rule reversals (ReversalRuleProcessor) of the same entry count
            // too, so the two reversal paths can never take back more than the earn between them.
            var entryIdStr = entry.Id.ToString();
            var alreadyRefunded = await db.Database.SqlQuery<decimal>($"""
                SELECT COALESCE(ABS(SUM(delta)), 0) AS "Value"
                FROM ledger_entries
                WHERE tenant_id = {tenantId}
                  AND ((reason = {LedgerReason.Refund} AND metadata->>'refund_of_entry_id' = {entryIdStr})
                    OR (reason = {LedgerReason.RuleReversal} AND metadata->>'reversal_of_entry_id' = {entryIdStr}))
                """).FirstAsync(ct);

            var remaining = entry.Delta - alreadyRefunded;
            if (remaining <= 0)
                continue;

            var amount = Math.Round(entry.Delta * ratio, 4);

            // Stamps cannot be fractional — round down
            if (entry.Reason == LedgerReason.StampEarn)
                amount = Math.Floor(amount);

            amount = Math.Min(amount, remaining);
            if (amount <= 0)
                continue;

            var idempotencyKey = $"{refundEventId}:refund:{entry.Id}";
            var metadata = System.Text.Json.JsonSerializer.Serialize(new
            {
                refund_of_entry_id = entry.Id,
                refund_ratio = ratio
            });

            await ledger.AddEntryAsync(
                tenantId: tenantId,
                customerAccountId: entry.CustomerAccountId,
                contactKey: entry.ContactKey,
                delta: -amount,
                reason: LedgerReason.Refund,
                sourceEventId: refundEventId,
                idempotencyKey: idempotencyKey,
                ruleId: entry.RuleId,
                metadata: metadata,
                ct: ct);

            refundedByAccount[entry.CustomerAccountId] =
                refundedByAccount.GetValueOrDefault(entry.CustomerAccountId) + amount;

            // A10 guarantee #5: release the budget the original posting's rule consumed. Raw
            // SQL against rule_limit_counters directly (same reasoning as
            // IsExplicitlyNonReversible above — no RuleEngine reference from Ledger); a no-op
            // if the rule never had budget tracking (no counter rows exist for it).
            if (entry.RuleId.HasValue)
                await ReleaseBudgetAsync(tenantGuid, entry.RuleId.Value, amount, isPostgres, ct);
        }

        // points.reversed — in the same tx as the refund entries. On redelivery the
        // cumulative cap makes all entries remaining=0 → the dict stays empty and the
        // event is not produced again; the dedup check remains a second safety net.
        // Held postings taken back above changed no balance, so they're not in this event.
        if (refundedByAccount.Count > 0)
        {
            var dedupKey = $"points_reversed:{refundEventId}";
            var alreadyEnqueued = await db.OutboxEvents.AnyAsync(
                x => x.TenantId == tenantGuid && x.DedupKey == dedupKey, ct);
            if (!alreadyEnqueued)
            {
                var ids = refundedByAccount.Keys.ToList();
                var accountRows = await db.CustomerAccounts
                    .AsNoTracking()
                    .Include(x => x.AccountType)
                    .Where(x => ids.Contains(x.Id))
                    .ToListAsync(ct);

                await outbox.Enqueue(
                    tenantId,
                    OutboundEventTypes.PointsReversed,
                    originalEntries[0].ContactKey,
                    new
                    {
                        contact_key = originalEntries[0].ContactKey,
                        source_event_id = refundEventId,
                        original_event_id = originalEventId,
                        refund_ratio = ratio.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture),
                        accounts = accountRows
                            .OrderBy(a => a.AccountType.Name)
                            .Select(a => new
                            {
                                account_type = a.AccountType.Type,
                                code = a.AccountType.Name,
                                delta = (-refundedByAccount[a.Id])
                                    .ToString("F2", System.Globalization.CultureInfo.InvariantCulture),
                                balance = a.Balance
                                    .ToString("F2", System.Globalization.CultureInfo.InvariantCulture)
                            })
                            .ToArray()
                    },
                    dedupKey: dedupKey);

                await db.SaveChangesAsync(ct);
            }
        }

        return Result.Ok();
    }

    // CR 2026-10-06 H1: a refund during the hold takes the held points back — the same ratio and
    // cumulative cap as a posted entry — by recording a HeldPostingRefund. Nothing touches the
    // ledger or a balance (the points were never posted). Once the whole Delta is taken back the
    // row is cancelled and DelayedPostingPromotionJob never posts it; otherwise it later posts
    // what remains. One row per (held posting, refund event), so a redelivered refund is a no-op.
    private async Task TakeBackHeldPostingsAsync(
        Guid tenantGuid, string refundEventId, List<HeldPosting> heldPostings, decimal ratio, bool isPostgres, CancellationToken ct)
    {
        if (heldPostings.Count == 0) return;

        var heldIds = heldPostings.Select(h => h.Id).ToList();
        var previous = await db.HeldPostingRefunds
            .Where(r => r.TenantId == tenantGuid && heldIds.Contains(r.HeldPostingId))
            .ToListAsync(ct);

        foreach (var held in heldPostings)
        {
            var refunds = previous.Where(r => r.HeldPostingId == held.Id).ToList();
            if (refunds.Any(r => r.RefundEventId == refundEventId))
                continue;

            // Summed in memory: EF's Sqlite provider (fast tests) can't aggregate decimals.
            var remaining = held.Delta - refunds.Sum(r => r.Delta);
            if (remaining <= 0)
                continue;

            var amount = Math.Round(held.Delta * ratio, 4);
            if (held.Reason == LedgerReason.StampEarn)
                amount = Math.Floor(amount);
            amount = Math.Min(amount, remaining);
            if (amount <= 0)
                continue;

            db.HeldPostingRefunds.Add(new HeldPostingRefund
            {
                Id = UUIDNext.Uuid.NewDatabaseFriendly(UUIDNext.Database.PostgreSql),
                TenantId = tenantGuid,
                HeldPostingId = held.Id,
                RefundEventId = refundEventId,
                Delta = amount,
                CreatedAt = DateTime.UtcNow
            });
            if (amount >= remaining)
                held.CancelledAt = DateTime.UtcNow;

            // The budget was reserved when the posting was held (LedgerPoster); give it back.
            await ReleaseBudgetAsync(tenantGuid, held.RuleId, amount, isPostgres, ct);
        }

        await db.SaveChangesAsync(ct);
    }

    // A10 guarantee #5: release the budget the original posting's rule consumed. Raw SQL against
    // rule_limit_counters directly (same reasoning as IsExplicitlyNonReversible above — no
    // RuleEngine reference from Ledger); a no-op if the rule never had budget tracking (no
    // counter rows exist for it).
    private async Task ReleaseBudgetAsync(Guid tenantGuid, Guid ruleId, decimal amount, bool isPostgres, CancellationToken ct)
    {
        // GREATEST (Postgres, production) vs MAX as a scalar 2-arg function (Sqlite, used by fast
        // in-memory tests) — same "floor at 0" clamp either way.
        var clampFn = isPostgres ? "GREATEST" : "MAX";
        var sql = $"UPDATE rule_limit_counters SET value = {clampFn}(value - {{0}}, 0), updated_at = {{1}} " +
                  "WHERE tenant_id = {2} AND rule_id = {3}";
        await db.Database.ExecuteSqlRawAsync(sql, [amount, DateTime.UtcNow, tenantGuid, ruleId], ct);
    }
}
