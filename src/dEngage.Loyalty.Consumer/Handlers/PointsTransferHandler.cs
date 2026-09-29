using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using dEngage.Loyalty.Engine.Framework.Consumers;
using dEngage.Loyalty.Ledger;
using dEngage.Loyalty.Schema;
using dEngage.Loyalty.Shared;
using dEngage.Loyalty.Shared.Events;
using Microsoft.EntityFrameworkCore;

namespace dEngage.Loyalty.Consumer.Handlers;

public class PointsTransferHandler(ILedgerService ledgerService, IOutboxService outbox, LoyaltyDbContext db, ITenantSlugResolver tenantSlugResolver) : IEventHandler
{
    public string? EventType => EventTypes.PointsTransfer;

    public async Task HandleAsync(EventEnvelope envelope, CancellationToken ct)
    {
        var data = envelope.Data;
        var contactKey = data.GetProperty("contact_key").GetString()!;
        var targetContactKey = data.GetProperty("target_contact_key").GetString()!;
        var pointsAmount = decimal.Parse(data.GetProperty("points_amount").GetString()!, CultureInfo.InvariantCulture);
        var sourceAccountTypeId = Guid.Parse(data.GetProperty("source_account_type_id").GetString()!);

        if (pointsAmount <= 0)
            throw new InvalidOperationException($"invalid_amount: {pointsAmount}");

        if (contactKey == targetContactKey)
            throw new InvalidOperationException("self_transfer_not_allowed");

        var tenantGuid = await tenantSlugResolver.ResolveAsync(envelope.Tenant, ct);
        var accountType = await db.AccountTypes
            .FirstOrDefaultAsync(x =>
                x.TenantId == tenantGuid &&
                x.Id == sourceAccountTypeId &&
                x.Type == "POINTS", ct)
            ?? throw new InvalidOperationException("points_account_type_not_found");

        var config = JsonSerializer.Deserialize<PointsConfig>(accountType.Config);
        if (config?.Transfer is null)
            throw new InvalidOperationException("transfer_not_configured");

        var dailyLimit = config.Transfer.DailyLimit;

        await using var tx = await db.Database.BeginTransactionAsync(ct);

        // Lock both accounts in deterministic order so concurrent A→B and B→A
        // transfers cannot deadlock if consumers ever run in parallel.
        var lockFirst = string.CompareOrdinal(contactKey, targetContactKey) <= 0 ? contactKey : targetContactKey;
        var lockSecond = lockFirst == contactKey ? targetContactKey : contactKey;

        var firstAccount = await ledgerService.LockAccountAsync(envelope.Tenant, lockFirst, sourceAccountTypeId, ct)
            ?? await ledgerService.UpsertAccountAsync(envelope.Tenant, lockFirst, sourceAccountTypeId, ct);
        var secondAccount = await ledgerService.LockAccountAsync(envelope.Tenant, lockSecond, sourceAccountTypeId, ct)
            ?? await ledgerService.UpsertAccountAsync(envelope.Tenant, lockSecond, sourceAccountTypeId, ct);

        var senderAccount = lockFirst == contactKey ? firstAccount : secondAccount;
        var receiverAccount = lockFirst == contactKey ? secondAccount : firstAccount;

        // Redelivery: if the transfer was already written (crash after commit before the
        // inbox update), do not move points or emit events again. Must come BEFORE the
        // limit/balance checks — a balance already debited on the first try would yield
        // a wrong transfer_failed on the second.
        var outIdempotencyKey = $"{envelope.EventId}:transfer_out";
        var transferExists = await db.LedgerEntries.AnyAsync(x =>
            x.TenantId == envelope.Tenant &&
            x.IdempotencyKey == outIdempotencyKey, ct);
        if (transferExists) return;

        // Sender is row-locked, so today's sum cannot change concurrently.
        var todayUtc = DateTime.UtcNow.Date;
        var transferredToday = await db.LedgerEntries
            .Where(x =>
                x.TenantId == envelope.Tenant &&
                x.CustomerAccountId == senderAccount.Id &&
                x.Reason == LedgerReason.TransferOut &&
                x.CreatedAt >= todayUtc)
            .SumAsync(x => -x.Delta, ct);

        if (transferredToday + pointsAmount > dailyLimit)
        {
            await FailAsync(envelope, contactKey, targetContactKey, pointsAmount,
                "daily_limit_exceeded", senderAccount.Balance, ct);
            await tx.CommitAsync(ct);
            return;
        }

        if (senderAccount.Balance < pointsAmount)
        {
            await FailAsync(envelope, contactKey, targetContactKey, pointsAmount,
                "insufficient_balance", senderAccount.Balance, ct);
            await tx.CommitAsync(ct);
            return;
        }

        await ledgerService.AddEntryAsync(
            tenantId: envelope.Tenant,
            customerAccountId: senderAccount.Id,
            contactKey: contactKey,
            delta: -pointsAmount,
            reason: LedgerReason.TransferOut,
            sourceEventId: envelope.EventId,
            idempotencyKey: outIdempotencyKey,
            metadata: JsonSerializer.Serialize(new { transfer_to = targetContactKey }),
            ct: ct);

        await ledgerService.AddEntryAsync(
            tenantId: envelope.Tenant,
            customerAccountId: receiverAccount.Id,
            contactKey: targetContactKey,
            delta: pointsAmount,
            reason: LedgerReason.TransferIn,
            sourceEventId: envelope.EventId,
            idempotencyKey: $"{envelope.EventId}:transfer_in",
            metadata: JsonSerializer.Serialize(new { transfer_from = contactKey }),
            ct: ct);

        await outbox.Enqueue(
            envelope.Tenant,
            OutboundEventTypes.PointsTransferred,
            contactKey,
            new
            {
                contact_key = contactKey,
                target_contact_key = targetContactKey,
                points_amount = pointsAmount.ToString("F2", CultureInfo.InvariantCulture),
                sender_balance = (senderAccount.Balance - pointsAmount).ToString("F2", CultureInfo.InvariantCulture),
                receiver_balance = (receiverAccount.Balance + pointsAmount).ToString("F2", CultureInfo.InvariantCulture),
                source_event_id = envelope.EventId
            },
            dedupKey: $"transfer:{envelope.EventId}");
        await db.SaveChangesAsync(ct);

        await tx.CommitAsync(ct);
    }

    // A business outcome, not a processing error: a retry won't change the limit or the
    // balance. Write the transfer_failed event and commit → the inbox becomes 'processed'.
    private async Task FailAsync(
        EventEnvelope envelope,
        string contactKey,
        string targetContactKey,
        decimal pointsAmount,
        string reason,
        decimal senderBalance,
        CancellationToken ct)
    {
        var failDedupKey = $"transfer_failed:{envelope.EventId}";
        var tenantGuid = await tenantSlugResolver.ResolveAsync(envelope.Tenant, ct);
        var alreadyEnqueued = await db.OutboxEvents.AnyAsync(x =>
            x.TenantId == tenantGuid && x.DedupKey == failDedupKey, ct);
        if (alreadyEnqueued) return;

        await outbox.Enqueue(
            envelope.Tenant,
            OutboundEventTypes.PointsTransferFailed,
            contactKey,
            new
            {
                contact_key = contactKey,
                target_contact_key = targetContactKey,
                points_amount = pointsAmount.ToString("F2", CultureInfo.InvariantCulture),
                reason,
                balance = senderBalance.ToString("F2", CultureInfo.InvariantCulture),
                source_event_id = envelope.EventId
            },
            dedupKey: failDedupKey);
        await db.SaveChangesAsync(ct);
    }
}

file record PointsConfig(
    [property: JsonPropertyName("transfer")] TransferConfig? Transfer
);

file record TransferConfig(
    [property: JsonPropertyName("daily_limit")] decimal DailyLimit
);
