using System.Text.Json;
using dEngage.Loyalty.Schema;
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
        var originalEntries = await db.LedgerEntries
            .Where(x =>
                x.TenantId == tenantId &&
                x.SourceEventId == originalEventId &&
                (x.Reason == LedgerReason.Earn || x.Reason == LedgerReason.StampEarn))
            .ToListAsync(ct);

        if (originalEntries.Count == 0)
            return Result.Fail("original_entries_not_found");

        // CR-08 (A8): Configuration.reversible (default true). Read as raw JSON rather than
        // deserializing the full RuleEngine.Models.RuleSettings type — Ledger has no reference
        // to RuleEngine (RuleEngine already depends on Ledger; the reverse would be circular),
        // and this is the one field this path needs.
        var ruleIds = originalEntries.Where(x => x.RuleId.HasValue).Select(x => x.RuleId!.Value).Distinct().ToList();
        var nonReversibleRuleIds = ruleIds.Count == 0 ? new HashSet<Guid>() : (await db.Rules
            .Where(r => ruleIds.Contains(r.Id) && r.Configuration != null)
            .Select(r => new { r.Id, r.Configuration })
            .ToListAsync(ct))
            .Where(r => IsExplicitlyNonReversible(r.Configuration))
            .Select(r => r.Id)
            .ToHashSet();

        if (nonReversibleRuleIds.Count > 0)
            originalEntries = originalEntries.Where(x => x.RuleId is null || !nonReversibleRuleIds.Contains(x.RuleId.Value)).ToList();

        if (originalEntries.Count == 0)
            return Result.Fail("original_entries_not_reversible");

        var ratio = Math.Clamp(refundRatio, 0m, 1m);
        var refundedByAccount = new Dictionary<Guid, decimal>();

        foreach (var entry in originalEntries)
        {
            // Cumulative cap: combined with previous refunds against the same entry, the
            // total cannot exceed the original earning (partial refunds cannot sum past 100%).
            var entryIdStr = entry.Id.ToString();
            var alreadyRefunded = await db.Database.SqlQuery<decimal>($"""
                SELECT COALESCE(ABS(SUM(delta)), 0) AS "Value"
                FROM ledger_entries
                WHERE tenant_id = {tenantId}
                  AND reason = {LedgerReason.Refund}
                  AND metadata->>'refund_of_entry_id' = {entryIdStr}
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
            {
                var tenantGuid = await tenantSlugResolver.ResolveAsync(tenantId, ct);
                // GREATEST (Postgres, production) vs MAX as a scalar 2-arg function (Sqlite,
                // used by fast in-memory tests) — same "floor at 0" clamp either way.
                var isPostgres = db.Database.ProviderName?.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) == true;
                var clampFn = isPostgres ? "GREATEST" : "MAX";
                var sql = $"UPDATE rule_limit_counters SET value = {clampFn}(value - {{0}}, 0), updated_at = {{1}} " +
                          "WHERE tenant_id = {2} AND rule_id = {3}";
                await db.Database.ExecuteSqlRawAsync(sql, [amount, DateTime.UtcNow, tenantGuid, entry.RuleId.Value], ct);
            }
        }

        // points.reversed — in the same tx as the refund entries. On redelivery the
        // cumulative cap makes all entries remaining=0 → the dict stays empty and the
        // event is not produced again; the dedup check remains a second safety net.
        if (refundedByAccount.Count > 0)
        {
            var dedupKey = $"points_reversed:{refundEventId}";
            var tenantGuid = await tenantSlugResolver.ResolveAsync(tenantId, ct);
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
}
