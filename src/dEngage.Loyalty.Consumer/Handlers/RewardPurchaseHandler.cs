using System.Globalization;
using dEngage.Loyalty.Engine.Framework.Consumers;
using dEngage.Loyalty.Ledger;
using dEngage.Loyalty.RuleEngine.Processing;
using dEngage.Loyalty.Schema;
using dEngage.Loyalty.Schema.Entities;
using dEngage.Loyalty.Shared;
using dEngage.Loyalty.Shared.Events;
using Microsoft.EntityFrameworkCore;

namespace dEngage.Loyalty.Consumer.Handlers;

public class RewardPurchaseHandler(
    ILedgerService ledgerService,
    IOutboxService outbox,
    IRewardFulfilmentService fulfilment,
    LoyaltyDbContext db,
    ITenantSlugResolver tenantSlugResolver) : IEventHandler
{
    public string? EventType => EventTypes.RewardPurchase;

    public async Task HandleAsync(EventEnvelope envelope, CancellationToken ct)
    {
        var data = envelope.Data;
        var contactKey = data.GetProperty("contact_key").GetString()!;
        // CR 2026-09-30 (§3.7): reward_id identifies the reward exactly; reward_name still works
        // (an active name is unique per tenant since the same CR). One of them is required.
        var rewardName = data.TryGetProperty("reward_name", out var rn) && rn.ValueKind == System.Text.Json.JsonValueKind.String ? rn.GetString() : null;
        Guid? rewardId = data.TryGetProperty("reward_id", out var ri) && Guid.TryParse(ri.GetString(), out var parsedId) ? parsedId : null;
        var channel = data.TryGetProperty("channel", out var ch) ? ch.GetString() : null;
        var tenantGuid = await tenantSlugResolver.ResolveAsync(envelope.Tenant, ct);
        var rewardRef = rewardId?.ToString() ?? rewardName
            ?? throw new InvalidOperationException("unknown_reward: neither reward_id nor reward_name given");

        // Undefined/inactive reward = configuration error → throw → inbox failed → DLQ.
        // A retry won't fix it, but it needs human intervention and must not be silently swallowed.
        var definition = await db.RewardDefinitions
            .AsNoTracking()
            .FirstOrDefaultAsync(x =>
                x.TenantId == tenantGuid &&
                (rewardId != null ? x.Id == rewardId : x.Name == rewardName) &&
                x.Acquisition == RewardAcquisition.PointsPurchase &&
                x.IsActive, ct)
            ?? throw new InvalidOperationException($"unknown_reward: {rewardRef}");

        // A4: a cashback reward nobody has approved yet must not be sellable — same reasoning as
        // a PendingApproval CASH rule never matching (CR-04).
        if (definition.Status != RewardStatus.Active)
            throw new InvalidOperationException($"reward_not_approved: {definition.Name}");

        if (definition.PointsPrice is null || definition.PointsAccountTypeId is null)
            throw new InvalidOperationException($"invalid_reward_config: {definition.Name} points_price/points_account_type_id missing");

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

        // O10: a cashback reward pays real money, which never flows from a draft or paused
        // program. CR 2026-10-05 item 5 (P-2): the same now holds for every reward type (a tier
        // upgrade included). Refused before the debit, so the customer keeps their points.
        if (!await fulfilment.IsProgramLiveAsync(tenantGuid, definition.ProgramId, ct))
        {
            await FailAsync(envelope, tenantGuid, contactKey, definition, OutcomeReasons.ProgramNotLive, account.Balance, price, ct);
            await tx.CommitAsync(ct);
            return;
        }

        if (account.Balance < price)
        {
            // A business outcome, not a processing error: a retry won't change the balance.
            // Write the purchase_failed event and commit → the inbox becomes 'processed'.
            await FailAsync(envelope, tenantGuid, contactKey, definition, "insufficient_balance", account.Balance, price, ct);
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

        // A2: act on what the reward is (cashback → CASH credit), in this same transaction —
        // the points debit and the payout commit together or not at all.
        var granted = await fulfilment.FulfilAsync(
            envelope.Tenant, tenantGuid, definition, contactKey, envelope.EventId, envelope.EventId, ct);

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

        // Additive fields from the fulfilment (outcome, cashback_amount, currency, ...) — §3.5.
        var payload = new Dictionary<string, object?>
        {
            ["contact_key"] = contactKey,
            ["reward_id"] = definition.Id.ToString(),
            ["reward_name"] = definition.Name,
            ["reward_type"] = definition.RewardType,
            ["source"] = RewardAcquisition.PointsPurchase,
            ["completion_count"] = completionCount,
            ["points_spent"] = price.ToString("F2", CultureInfo.InvariantCulture),
            ["channel"] = channel,
            ["source_event_id"] = envelope.EventId
        };
        foreach (var (key, value) in granted.EventFields) payload[key] = value;

        await outbox.Enqueue(
            envelope.Tenant,
            OutboundEventTypes.RewardEarned,
            contactKey,
            payload,
            dedupKey: $"reward_earned:{envelope.EventId}:{accountTypeId}");

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    private async Task FailAsync(
        EventEnvelope envelope, Guid tenantGuid, string contactKey, RewardDefinition definition,
        string reason, decimal balance, decimal price, CancellationToken ct)
    {
        var failDedupKey = $"purchase_failed:{envelope.EventId}";
        var alreadyEnqueued = await db.OutboxEvents.AnyAsync(x =>
            x.TenantId == tenantGuid && x.DedupKey == failDedupKey, ct);
        if (alreadyEnqueued) return;

        await outbox.Enqueue(
            envelope.Tenant,
            OutboundEventTypes.RewardPurchaseFailed,
            contactKey,
            new
            {
                contact_key = contactKey,
                reward_name = definition.Name,
                reason,
                balance = balance.ToString("F2", CultureInfo.InvariantCulture),
                points_price = price.ToString("F2", CultureInfo.InvariantCulture),
                source_event_id = envelope.EventId
            },
            dedupKey: failDedupKey);
        await db.SaveChangesAsync(ct);
    }
}
