using System.Globalization;
using dEngage.Loyalty.Engine.Framework.Consumers;
using dEngage.Loyalty.Ledger;
using dEngage.Loyalty.Schema;
using dEngage.Loyalty.Schema.Entities;
using dEngage.Loyalty.Shared;
using dEngage.Loyalty.Shared.Events;
using Microsoft.EntityFrameworkCore;

namespace dEngage.Loyalty.Consumer.Handlers;

public class RewardPurchaseHandler(ILedgerService ledgerService, IOutboxService outbox, LoyaltyDbContext db, ITenantSlugResolver tenantSlugResolver) : IEventHandler
{
    public string? EventType => EventTypes.RewardPurchase;

    public async Task HandleAsync(EventEnvelope envelope, CancellationToken ct)
    {
        var data = envelope.Data;
        var contactKey = data.GetProperty("contact_key").GetString()!;
        var rewardName = data.GetProperty("reward_name").GetString()!;
        var channel = data.TryGetProperty("channel", out var ch) ? ch.GetString() : null;
        var tenantGuid = await tenantSlugResolver.ResolveAsync(envelope.Tenant, ct);

        // Undefined/inactive reward = configuration error → throw → inbox failed → DLQ.
        // A retry won't fix it, but it needs human intervention and must not be silently swallowed.
        var definition = await db.RewardDefinitions
            .AsNoTracking()
            .FirstOrDefaultAsync(x =>
                x.TenantId == tenantGuid &&
                x.Name == rewardName &&
                x.Acquisition == RewardAcquisition.PointsPurchase &&
                x.IsActive, ct)
            ?? throw new InvalidOperationException($"unknown_reward: {rewardName}");

        if (definition.PointsPrice is null || definition.PointsAccountTypeId is null)
            throw new InvalidOperationException($"invalid_reward_config: {rewardName} points_price/points_account_type_id eksik");

        var price = definition.PointsPrice.Value;
        var accountTypeId = definition.PointsAccountTypeId.Value;

        await using var tx = await db.Database.BeginTransactionAsync(ct);

        // Redelivery: if the reward was already written (crash after commit before the
        // inbox update), do not debit or emit events again. Must come BEFORE the balance
        // check — a balance debited on the first try would yield a wrong purchase_failed on the second.
        var rewardExists = await db.RewardLogs.AnyAsync(x =>
            x.TenantId == tenantGuid &&
            x.AccountTypeId == accountTypeId &&
            x.SourceEventId == envelope.EventId, ct);
        if (rewardExists) return;

        var account = await ledgerService.LockAccountAsync(envelope.Tenant, contactKey, accountTypeId, ct)
            ?? await ledgerService.UpsertAccountAsync(envelope.Tenant, contactKey, accountTypeId, ct);

        if (account.Balance < price)
        {
            // A business outcome, not a processing error: a retry won't change the balance.
            // Write the purchase_failed event and commit → the inbox becomes 'processed'.
            var failDedupKey = $"purchase_failed:{envelope.EventId}";
            var alreadyEnqueued = await db.OutboxEvents.AnyAsync(x =>
                x.TenantId == tenantGuid && x.DedupKey == failDedupKey, ct);

            if (!alreadyEnqueued)
            {
                await outbox.Enqueue(
                    envelope.Tenant,
                    OutboundEventTypes.RewardPurchaseFailed,
                    contactKey,
                    new
                    {
                        contact_key = contactKey,
                        reward_name = definition.Name,
                        reason = "insufficient_balance",
                        balance = account.Balance.ToString("F2", CultureInfo.InvariantCulture),
                        points_price = price.ToString("F2", CultureInfo.InvariantCulture),
                        source_event_id = envelope.EventId
                    },
                    dedupKey: failDedupKey);
                await db.SaveChangesAsync(ct);
            }

            await tx.CommitAsync(ct);
            return;
        }

        var entry = await ledgerService.AddEntryAsync(
            tenantId: envelope.Tenant,
            customerAccountId: account.Id,
            contactKey: contactKey,
            delta: -price,
            reason: LedgerReason.RewardPurchase,
            sourceEventId: envelope.EventId,
            idempotencyKey: $"{envelope.EventId}:reward_purchase",
            ct: ct);

        var completionCount = await db.RewardLogs.CountAsync(x =>
            x.TenantId == tenantGuid &&
            x.ContactKey == contactKey &&
            x.AccountTypeId == accountTypeId, ct) + 1;

        var now = DateTime.UtcNow;
        db.RewardLogs.Add(new RewardLog
        {
            Id = UUIDNext.Uuid.NewDatabaseFriendly(UUIDNext.Database.PostgreSql),
            TenantId = tenantGuid,
            ContactKey = contactKey,
            AccountTypeId = accountTypeId,
            RewardName = definition.Name,
            SourceEventId = envelope.EventId,
            LedgerResetEntryId = entry.Id,
            CompletionCount = completionCount,
            RewardDefinitionId = definition.Id,
            Status = RewardLogStatus.Notified,
            CreatedAt = now,
            DeliveredAt = now
        });

        await outbox.Enqueue(
            envelope.Tenant,
            OutboundEventTypes.RewardEarned,
            contactKey,
            new
            {
                contact_key = contactKey,
                reward_name = definition.Name,
                reward_type = definition.RewardType,
                source = RewardAcquisition.PointsPurchase,
                completion_count = completionCount,
                points_spent = price.ToString("F2", CultureInfo.InvariantCulture),
                channel,
                source_event_id = envelope.EventId
            },
            dedupKey: $"reward_earned:{envelope.EventId}:{accountTypeId}");

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }
}
