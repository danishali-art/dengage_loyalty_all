using System.Globalization;
using dEngage.Loyalty.Engine.Framework.Consumers;
using dEngage.Loyalty.Ledger;
using dEngage.Loyalty.RuleEngine.Processing;
using dEngage.Loyalty.Schema;
using dEngage.Loyalty.Shared;
using dEngage.Loyalty.Shared.Events;
using Microsoft.EntityFrameworkCore;

namespace dEngage.Loyalty.Consumer.Handlers;

public class CashAddedHandler(
    ILedgerService ledgerService,
    IOutboxService outbox,
    LoyaltyDbContext db,
    ITenantSlugResolver tenantSlugResolver) : IEventHandler
{
    public string? EventType => EventTypes.CashAdded;

    public async Task HandleAsync(EventEnvelope envelope, CancellationToken ct)
    {
        var data = envelope.Data;
        var contactKey = data.GetProperty("contact_key").GetString()!;
        var amount = decimal.Parse(data.GetProperty("amount").GetString()!, CultureInfo.InvariantCulture);
        var accountTypeId = Guid.Parse(data.GetProperty("account_type_id").GetString()!);

        var tenantGuid = await tenantSlugResolver.ResolveAsync(envelope.Tenant, ct);
        var programId = await db.AccountTypes
            .AsNoTracking()
            .Where(x => x.TenantId == tenantGuid && x.Id == accountTypeId)
            .Select(x => (Guid?)x.ProgramId)
            .FirstOrDefaultAsync(ct)
            ?? throw new InvalidOperationException("account_type_not_found");

        // Redelivery: a load already posted (before the program was paused, say) must not be
        // reported as failed now. Must come BEFORE the program check.
        var idempotencyKey = $"{envelope.EventId}:cash_load";
        if (await db.LedgerEntries.AnyAsync(x => x.TenantId == envelope.Tenant && x.IdempotencyKey == idempotencyKey, ct))
            return;

        // CR 2026-10-05 item 5 (P-3): nothing moves in a program that isn't live. A business
        // outcome, not a processing error — reported with cash.add_failed, inbox 'processed'.
        if (!await db.IsProgramLiveAsync(tenantGuid, programId, ct))
        {
            await CashFailure.ReportAsync(db, outbox, envelope, tenantGuid, OutboundEventTypes.CashAddFailed,
                "cash_add_failed", contactKey, amount, accountTypeId, OutcomeReasons.ProgramNotLive, ct);
            return;
        }

        var account = await ledgerService.UpsertAccountAsync(envelope.Tenant, contactKey, accountTypeId, ct);

        await ledgerService.AddEntryAsync(
            tenantId: envelope.Tenant,
            customerAccountId: account.Id,
            contactKey: contactKey,
            delta: amount,
            reason: LedgerReason.CashLoad,
            sourceEventId: envelope.EventId,
            idempotencyKey: idempotencyKey,
            ct: ct);
    }
}

// CR 2026-10-05 item 5 (P-3): the cash.add_failed / cash.spend_failed outbound event — same
// shape and dedupe-key convention as redeem_failed / transfer_failed.
internal static class CashFailure
{
    public static async Task ReportAsync(
        LoyaltyDbContext db,
        IOutboxService outbox,
        EventEnvelope envelope,
        Guid tenantGuid,
        string outboundEventType,
        string dedupPrefix,
        string contactKey,
        decimal amount,
        Guid accountTypeId,
        string reason,
        CancellationToken ct)
    {
        var dedupKey = $"{dedupPrefix}:{envelope.EventId}";
        if (await db.OutboxEvents.AnyAsync(x => x.TenantId == tenantGuid && x.DedupKey == dedupKey, ct))
            return;

        await outbox.Enqueue(
            envelope.Tenant,
            outboundEventType,
            contactKey,
            new
            {
                contact_key = contactKey,
                amount = amount.ToString(CultureInfo.InvariantCulture),
                account_type_id = accountTypeId.ToString(),
                reason,
                source_event_id = envelope.EventId
            },
            dedupKey: dedupKey);
        await db.SaveChangesAsync(ct);
    }
}
