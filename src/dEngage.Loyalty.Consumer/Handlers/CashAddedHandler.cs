using System.Globalization;
using dEngage.Loyalty.Engine.Framework.Consumers;
using dEngage.Loyalty.Ledger;
using dEngage.Loyalty.Shared;
using dEngage.Loyalty.Shared.Events;

namespace dEngage.Loyalty.Consumer.Handlers;

public class CashAddedHandler(ILedgerService ledgerService) : IEventHandler
{
    public string? EventType => EventTypes.CashAdded;

    public async Task HandleAsync(EventEnvelope envelope, CancellationToken ct)
    {
        var data = envelope.Data;
        var contactKey = data.GetProperty("contact_key").GetString()!;
        var amount = decimal.Parse(data.GetProperty("amount").GetString()!, CultureInfo.InvariantCulture);
        var accountTypeId = Guid.Parse(data.GetProperty("account_type_id").GetString()!);

        var account = await ledgerService.UpsertAccountAsync(envelope.Tenant, contactKey, accountTypeId, ct);

        await ledgerService.AddEntryAsync(
            tenantId: envelope.Tenant,
            customerAccountId: account.Id,
            contactKey: contactKey,
            delta: amount,
            reason: LedgerReason.CashLoad,
            sourceEventId: envelope.EventId,
            idempotencyKey: $"{envelope.EventId}:cash_load",
            ct: ct);
    }
}
