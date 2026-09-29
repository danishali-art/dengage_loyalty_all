using System.Globalization;
using dEngage.Loyalty.Engine.Framework.Consumers;
using dEngage.Loyalty.Ledger;
using dEngage.Loyalty.Schema;
using dEngage.Loyalty.Shared;
using dEngage.Loyalty.Shared.Events;

namespace dEngage.Loyalty.Consumer.Handlers;

public class CashSpentHandler(ILedgerService ledgerService, LoyaltyDbContext db) : IEventHandler
{
    public string? EventType => EventTypes.CashSpent;

    public async Task HandleAsync(EventEnvelope envelope, CancellationToken ct)
    {
        var data = envelope.Data;
        var contactKey = data.GetProperty("contact_key").GetString()!;
        var amount = decimal.Parse(data.GetProperty("amount").GetString()!, CultureInfo.InvariantCulture);
        var accountTypeId = Guid.Parse(data.GetProperty("account_type_id").GetString()!);

        // Balance check + debit in a single transaction, with a row lock —
        // no concurrent write can change the balance between the check and the debit.
        await using var tx = await db.Database.BeginTransactionAsync(ct);

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
            idempotencyKey: $"{envelope.EventId}:cash_spend",
            ct: ct);

        await tx.CommitAsync(ct);
    }
}
