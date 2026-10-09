using System.Globalization;
using dEngage.Loyalty.Engine.Framework.Consumers;
using dEngage.Loyalty.Ledger;
using dEngage.Loyalty.RuleEngine.Processing;
using dEngage.Loyalty.Schema;
using dEngage.Loyalty.Shared;
using dEngage.Loyalty.Shared.Events;
using Microsoft.EntityFrameworkCore;

namespace dEngage.Loyalty.Consumer.Handlers;

// CR 2026-10-06 D23: this handler runs the rule engine itself, and only for a spend that
// happened — a refused spend (program not live, insufficient balance) earns nothing. The worker
// leaves cash.spent out of its own evaluation for that reason (HandlerEvaluatedEvents).
public class CashSpentHandler(
    ILedgerService ledgerService,
    IOutboxService outbox,
    LoyaltyDbContext db,
    ITenantSlugResolver tenantSlugResolver,
    ICampaignEvaluationService campaignEval) : IEventHandler
{
    public string? EventType => EventTypes.CashSpent;

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

        if (!await SpendAsync(envelope, tenantGuid, programId, contactKey, amount, accountTypeId, ct))
            return;

        // After the spend's commit, as the worker used to: a redelivery of an already posted
        // spend evaluates again too — postings are deduped by key.
        await campaignEval.EvaluateAsync(envelope, ct);
    }

    // True when the spend is posted (now or on an earlier delivery); false when it was refused.
    private async Task<bool> SpendAsync(EventEnvelope envelope, Guid tenantGuid, Guid programId,
        string contactKey, decimal amount, Guid accountTypeId, CancellationToken ct)
    {
        // Balance check + debit in a single transaction, with a row lock —
        // no concurrent write can change the balance between the check and the debit.
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        // Redelivery: a spend already posted (before the program was paused, say) must not be
        // reported as failed now. Must come BEFORE the program check.
        var idempotencyKey = $"{envelope.EventId}:cash_spend";
        if (await db.LedgerEntries.AnyAsync(x => x.TenantId == envelope.Tenant && x.IdempotencyKey == idempotencyKey, ct))
            return true;

        // CR 2026-10-05 item 5 (P-3): nothing moves in a program that isn't live. A business
        // outcome, not a processing error — reported with cash.spend_failed, inbox 'processed'.
        if (!await db.IsProgramLiveAsync(tenantGuid, programId, ct))
        {
            await CashFailure.ReportAsync(db, outbox, envelope, tenantGuid, OutboundEventTypes.CashSpendFailed,
                "cash_spend_failed", contactKey, amount, accountTypeId, OutcomeReasons.ProgramNotLive, ct);
            await tx.CommitAsync(ct);
            return false;
        }

        var account = await ledgerService.LockAccountAsync(envelope.Tenant, contactKey, accountTypeId, ct)
            ?? await ledgerService.UpsertAccountAsync(envelope.Tenant, contactKey, accountTypeId, ct);

        // CR 2026-10-06 D23: a balance too low for the spend is a business outcome like the
        // program check above — reported with cash.spend_failed (insufficient_balance), inbox
        // 'processed'. It used to throw, which dead-lettered the event and told the client
        // nothing. Decided by an explicit check under the row lock, never by catching.
        if (account.Balance < amount)
        {
            await CashFailure.ReportAsync(db, outbox, envelope, tenantGuid, OutboundEventTypes.CashSpendFailed,
                "cash_spend_failed", contactKey, amount, accountTypeId, OutcomeReasons.InsufficientBalance, ct);
            await tx.CommitAsync(ct);
            return false;
        }

        await ledgerService.AddEntryAsync(
            tenantId: envelope.Tenant,
            customerAccountId: account.Id,
            contactKey: contactKey,
            delta: -amount,
            reason: LedgerReason.CashSpend,
            sourceEventId: envelope.EventId,
            idempotencyKey: idempotencyKey,
            ct: ct);

        await tx.CommitAsync(ct);
        return true;
    }
}
