using System.Text.Json;
using dEngage.Loyalty.Ledger;
using dEngage.Loyalty.RuleEngine.Models;
using dEngage.Loyalty.Schema;
using dEngage.Loyalty.Shared;
using dEngage.Loyalty.Shared.Events;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace dEngage.Loyalty.RuleEngine.Processing;

// CR-02 (docs/scope-change-rules A3): "Two postings under one transaction id." Only one
// TransferRule executes per event — the highest-priority match whose conditions pass and whose
// balance/daily-limit checks succeed — mirroring an exclusive winner without pulling in
// WinnerSelector's stacking machinery, since a transfer is a singular action, not something
// that stacks. A rule that fails validation/limits is skipped silently (same as an unmatched
// rule), not surfaced as a customer-facing failure — that UX belongs to whichever caller
// eventually triggers this path (see the interface's "not wired in yet" note).
public sealed class TransferRuleProcessor(
    LoyaltyDbContext db,
    ILedgerService ledger,
    IOutboxService outbox,
    IRuleFireAuditWriter auditWriter,
    ILogger<TransferRuleProcessor> logger) : ITransferRuleProcessor
{
    public async Task ProcessAsync(
        string tenantId,
        string eventId,
        IReadOnlyList<CachedRule> matchedTransferRules,
        EvaluationEvent evt,
        ConditionContext context,
        CancellationToken ct)
    {
        var requestedPoints = evt.Amount;
        // "target_contact_key" — same field name the legacy PointsTransferHandler already uses
        // (see Shared.Events.EventTypes.Catalog remarks: this codebase's real payload
        // convention is flat bare fields, not A5's illustrative "transfer.*" namespace).
        var recipientRef = ResolveStringField(evt.Data, "target_contact_key");

        if (requestedPoints <= 0 || string.IsNullOrWhiteSpace(recipientRef) || recipientRef == evt.ContactKey)
            return;

        var rule = matchedTransferRules
            .Where(r => GroupedConditionEvaluator.Evaluate(r.Conditions, evt, context))
            .OrderByDescending(r => r.Priority)
            .FirstOrDefault();
        if (rule is null || rule.TargetAccountTypeId is not { } accountTypeId) return;

        var ratio = rule.Calculation.Ratio ?? 1m;
        var fee = rule.Calculation.Fee ?? 0m;
        var recipientCredit = Math.Max(0m, requestedPoints * ratio - fee);
        if (recipientCredit <= 0) return;

        await using var tx = await db.Database.BeginTransactionAsync(ct);

        // Redelivery guard, same idempotency-key convention as LedgerPoster/PointsTransferHandler.
        var outKey = $"{eventId}:{rule.Id}:transfer_out";
        if (await db.LedgerEntries.AnyAsync(x => x.TenantId == tenantId && x.IdempotencyKey == outKey, ct))
            return;

        // Deterministic lock order — same rationale as PointsTransferHandler: concurrent
        // A→B and B→A transfers cannot deadlock.
        var lockFirst = string.CompareOrdinal(evt.ContactKey, recipientRef) <= 0 ? evt.ContactKey : recipientRef;
        var lockSecond = lockFirst == evt.ContactKey ? recipientRef : evt.ContactKey;

        var firstAccount = await ledger.LockAccountAsync(tenantId, lockFirst, accountTypeId, ct)
            ?? await ledger.UpsertAccountAsync(tenantId, lockFirst, accountTypeId, ct);
        var secondAccount = await ledger.LockAccountAsync(tenantId, lockSecond, accountTypeId, ct)
            ?? await ledger.UpsertAccountAsync(tenantId, lockSecond, accountTypeId, ct);

        var senderAccount = lockFirst == evt.ContactKey ? firstAccount : secondAccount;
        var receiverAccount = lockFirst == evt.ContactKey ? secondAccount : firstAccount;

        if (rule.Calculation.MaxPerDay.HasValue)
        {
            var todayUtc = DateTime.UtcNow.Date;
            var transferredToday = await db.LedgerEntries
                .Where(x =>
                    x.TenantId == tenantId &&
                    x.RuleId == rule.Id &&
                    x.CustomerAccountId == senderAccount.Id &&
                    x.Reason == LedgerReason.TransferOut &&
                    x.CreatedAt >= todayUtc)
                .SumAsync(x => -x.Delta, ct);

            if (transferredToday + requestedPoints > rule.Calculation.MaxPerDay.Value)
            {
                logger.LogInformation("RuleEngine: [{Tenant}] TransferRule SKIP [{Rule}] max_per_day exceeded", tenantId, rule.Name);
                return;
            }
        }

        if (senderAccount.Balance < requestedPoints)
        {
            logger.LogInformation("RuleEngine: [{Tenant}] TransferRule SKIP [{Rule}] insufficient_balance", tenantId, rule.Name);
            return;
        }

        var metadata = JsonSerializer.Serialize(new { transfer_to = recipientRef, ratio, fee });

        var debitEntry = await ledger.AddEntryAsync(
            tenantId, senderAccount.Id, evt.ContactKey,
            -requestedPoints, LedgerReason.TransferOut, eventId, outKey, rule.Id, metadata, ct);

        await ledger.AddEntryAsync(
            tenantId, receiverAccount.Id, recipientRef,
            recipientCredit, LedgerReason.TransferIn, eventId, $"{eventId}:{rule.Id}:transfer_in",
            rule.Id, JsonSerializer.Serialize(new { transfer_from = evt.ContactKey, ratio, fee }), ct);

        await auditWriter.RecordAsync(
            tenantId, rule.Id, rule.Version, eventId, evt.ContactKey,
            rule.Conditions, rule.Calculation, -requestedPoints, debitEntry.Id, null, ct);
        await db.SaveChangesAsync(ct);

        // Distinct dedup-key prefix from the legacy PointsTransferHandler's "transfer:{eventId}"
        // — the two paths are not wired to the same event in this milestone, but must never
        // collide if that changes later.
        await outbox.Enqueue(
            tenantId,
            OutboundEventTypes.PointsTransferred,
            evt.ContactKey,
            new
            {
                contact_key = evt.ContactKey,
                target_contact_key = recipientRef,
                points_amount = requestedPoints.ToString("F2", System.Globalization.CultureInfo.InvariantCulture),
                credited_amount = recipientCredit.ToString("F2", System.Globalization.CultureInfo.InvariantCulture),
                rule_id = rule.Id.ToString(),
                source_event_id = eventId
            },
            dedupKey: $"rule_transfer:{eventId}:{rule.Id}");
        await db.SaveChangesAsync(ct);

        await tx.CommitAsync(ct);
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
