using System.Globalization;
using System.Text.Json;
using dEngage.Loyalty.Ledger;
using dEngage.Loyalty.RuleEngine.Models;
using dEngage.Loyalty.Schema;
using dEngage.Loyalty.Shared;
using dEngage.Loyalty.Shared.Events;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace dEngage.Loyalty.RuleEngine.Processing;

// CR-02 (docs/scope-change-rules A3): reads the original posting(s) for tx.originalEventId and
// posts a compensating entry to the SAME account as the original — the target is resolved from
// history, never from rule config (A10 guarantee #6: "reversal reads history, not current
// config", including when the original rule is now Disabled — this processor never re-reads
// the original Rule row at all, only the LedgerEntry it produced).
public sealed class ReversalRuleProcessor(
    LoyaltyDbContext db,
    ILedgerService ledger,
    IOutboxService outbox,
    IRuleFireAuditWriter auditWriter,
    IBudgetReservationService budgetReservation,
    ILogger<ReversalRuleProcessor> logger) : IReversalRuleProcessor
{
    public async Task ProcessAsync(
        string tenantId,
        string eventId,
        IReadOnlyList<CachedRule> matchedReversalRules,
        EvaluationEvent evt,
        ConditionContext context,
        CancellationToken ct)
    {
        // "original_event_id" — same field name OrderRefundedHandler/RefundService already use.
        var originalEventId = ResolveStringField(evt.Data, "original_event_id");
        if (string.IsNullOrWhiteSpace(originalEventId)) return;

        var rule = matchedReversalRules
            .Where(r => GroupedConditionEvaluator.Evaluate(r.Conditions, evt, context))
            .OrderByDescending(r => r.Priority)
            .FirstOrDefault();
        if (rule is null) return;

        var mode = rule.Calculation.Mode ?? "proportional";
        var clampToZero = rule.Calculation.AllowNegative != "allow negative";

        var originalEntries = await db.LedgerEntries
            .Where(x =>
                x.TenantId == tenantId &&
                x.SourceEventId == originalEventId &&
                (x.Reason == LedgerReason.Earn || x.Reason == LedgerReason.StampEarn))
            .ToListAsync(ct);
        if (originalEntries.Count == 0) return;

        var ratio = mode == "full" ? 1m : ResolveRatio(evt.Data);

        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var reversedByAccount = new Dictionary<Guid, decimal>();
        foreach (var entry in originalEntries)
        {
            // Cumulative cap, same shape as RefundService's — but tracked under the distinct
            // rule_reversal reason/metadata key so this path's bookkeeping never mixes with
            // the legacy Refund path's (they are not wired to the same event in this milestone,
            // but the moment they were, sharing a cap would silently under- or over-reverse).
            var entryIdStr = entry.Id.ToString();
            var alreadyReversed = await db.Database.SqlQuery<decimal>($"""
                SELECT COALESCE(ABS(SUM(delta)), 0) AS "Value"
                FROM ledger_entries
                WHERE tenant_id = {tenantId}
                  AND reason = {LedgerReason.RuleReversal}
                  AND metadata->>'reversal_of_entry_id' = {entryIdStr}
                """).FirstAsync(ct);

            var remaining = entry.Delta - alreadyReversed;
            if (remaining <= 0) continue;

            var amount = Math.Round(entry.Delta * ratio, 4);
            if (entry.Reason == LedgerReason.StampEarn) amount = Math.Floor(amount);
            amount = Math.Min(amount, remaining);
            if (amount <= 0) continue;

            if (clampToZero)
            {
                var account = await db.CustomerAccounts.AsNoTracking()
                    .FirstOrDefaultAsync(a => a.Id == entry.CustomerAccountId, ct);
                if (account is not null && account.Balance < amount)
                    amount = Math.Max(0m, account.Balance);
                if (amount <= 0) continue;
            }

            var idempotencyKey = $"{eventId}:{rule.Id}:reversal:{entry.Id}";
            var metadata = JsonSerializer.Serialize(new
            {
                reversal_of_entry_id = entry.Id,
                reversal_ratio = ratio,
                mode
            });

            var reversalEntry = await ledger.AddEntryAsync(
                tenantId: tenantId,
                customerAccountId: entry.CustomerAccountId,
                contactKey: entry.ContactKey,
                delta: -amount,
                reason: LedgerReason.RuleReversal,
                sourceEventId: eventId,
                idempotencyKey: idempotencyKey,
                ruleId: rule.Id,
                metadata: metadata,
                ct: ct);

            reversedByAccount[entry.CustomerAccountId] = reversedByAccount.GetValueOrDefault(entry.CustomerAccountId) + amount;

            // A10 guarantee #5: release the budget the ORIGINAL posting's rule consumed — not
            // this ReversalRule's own budget (it has none in the reversed sense).
            if (entry.RuleId.HasValue)
                await budgetReservation.ReleaseUsageAsync(tenantId, entry.RuleId.Value, amount, ct);

            await auditWriter.RecordAsync(
                tenantId, rule.Id, rule.Version, eventId, entry.ContactKey,
                rule.Conditions, rule.Calculation, -amount, reversalEntry.Id, null, ct);
        }
        await db.SaveChangesAsync(ct);

        if (reversedByAccount.Count > 0)
        {
            await outbox.Enqueue(
                tenantId,
                OutboundEventTypes.PointsReversed,
                originalEntries[0].ContactKey,
                new
                {
                    contact_key = originalEntries[0].ContactKey,
                    source_event_id = eventId,
                    original_event_id = originalEventId,
                    rule_id = rule.Id.ToString(),
                    reversal_ratio = ratio.ToString("0.####", CultureInfo.InvariantCulture),
                    mode
                },
                dedupKey: $"rule_reversal:{eventId}:{rule.Id}");
            await db.SaveChangesAsync(ct);
        }
        else
        {
            logger.LogInformation("RuleEngine: [{Tenant}] ReversalRule [{Rule}] nothing left to reverse for {OriginalEventId}",
                tenantId, rule.Name, originalEventId);
        }

        await tx.CommitAsync(ct);
    }

    // Same payload contract as the legacy OrderRefundedHandler: an explicit refund_ratio, or
    // amount/original_amount to derive one. Clamped to [0,1] — a bad/corrupt payload cannot
    // push the reversal above the original posting.
    private static decimal ResolveRatio(JsonElement data)
    {
        if (data.TryGetProperty("refund_ratio", out var ratioEl) &&
            TryGetDecimal(ratioEl, out var explicitRatio))
            return Math.Clamp(explicitRatio, 0m, 1m);

        if (data.TryGetProperty("amount", out var amountEl) && TryGetDecimal(amountEl, out var refundAmount) &&
            data.TryGetProperty("original_amount", out var origEl) && TryGetDecimal(origEl, out var originalAmount))
        {
            var ratio = originalAmount == 0 ? 1m : Math.Round(refundAmount / originalAmount, 6);
            return Math.Clamp(ratio, 0m, 1m);
        }

        return 1m;
    }

    private static bool TryGetDecimal(JsonElement el, out decimal value)
    {
        value = 0;
        if (el.ValueKind == JsonValueKind.Number) return el.TryGetDecimal(out value);
        if (el.ValueKind == JsonValueKind.String)
            return decimal.TryParse(el.GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out value);
        return false;
    }

    private static string? ResolveStringField(JsonElement root, string path)
    {
        var current = root;
        foreach (var segment in path.Split('.'))
        {
            if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(segment, out var next))
                return null;
            current = next;
        }
        return current.ValueKind == JsonValueKind.String ? current.GetString() : null;
    }
}
