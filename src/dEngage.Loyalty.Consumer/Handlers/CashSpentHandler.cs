using System.Globalization;
using dEngage.Loyalty.Engine.Framework.Consumers;
using dEngage.Loyalty.Ledger;
using dEngage.Loyalty.RuleEngine.Processing;
using dEngage.Loyalty.Schema;
using dEngage.Loyalty.Shared;
using dEngage.Loyalty.Shared.Events;
using Microsoft.EntityFrameworkCore;

namespace dEngage.Loyalty.Consumer.Handlers;

public class CashSpentHandler(
    ILedgerService ledgerService,
    IOutboxService outbox,
    LoyaltyDbContext db,
    ITenantSlugResolver tenantSlugResolver) : IEventHandler
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

        // Balance check + debit in a single transaction, with a row lock —
        // no concurrent write can change the balance between the check and the debit.
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        // Redelivery: a spend already posted (before the program was paused, say) must not be
        // reported as failed now. Must come BEFORE the program check.
        var idempotencyKey = $"{envelope.EventId}:cash_spend";
        if (await db.LedgerEntries.AnyAsync(x => x.TenantId == envelope.Tenant && x.IdempotencyKey == idempotencyKey, ct))
            return;

        // CR 2026-10-05 item 5 (P-3): nothing moves in a program that isn't live. A business
        // outcome, not a processing error — reported with cash.spend_failed, inbox 'processed'.
        if (!await db.IsProgramLiveAsync(tenantGuid, programId, ct))
        {
            await CashFailure.ReportAsync(db, outbox, envelope, tenantGuid, OutboundEventTypes.CashSpendFailed,
                "cash_spend_failed", contactKey, amount, accountTypeId, OutcomeReasons.ProgramNotLive, ct);
            await tx.CommitAsync(ct);
            return;
        }

        var account = await ledgerService.LockAccountAsync(envelope.Tenant, contactKey, accountTypeId, ct)
            ?? await ledgerService.UpsertAccountAsync(envelope.Tenant, contactKey, accountTypeId, ct);

        if (account.Balance < amount)
            throw new InvalidOperationException($"insufficient_balance: {account.Balance} < {amount}");

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
    }
}
