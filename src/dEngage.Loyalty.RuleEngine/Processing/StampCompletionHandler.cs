using System.Text.Json;
using dEngage.Loyalty.Ledger;
using dEngage.Loyalty.RuleEngine.Models;
using dEngage.Loyalty.Schema;
using dEngage.Loyalty.Schema.Entities;
using dEngage.Loyalty.Shared;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace dEngage.Loyalty.RuleEngine.Processing;

public sealed class StampCompletionHandler(
    LoyaltyDbContext db,
    ILedgerService ledger,
    IOutboxService outbox,
    ITenantSlugResolver tenantSlugResolver,
    ILogger<StampCompletionHandler> logger) : IStampCompletionHandler
{
    public async Task ProcessAsync(
        string tenantId,
        Guid accountId,
        string contactKey,
        CachedRule rule,
        string eventId,
        CancellationToken ct)
    {
        var tenantGuid = await tenantSlugResolver.ResolveAsync(tenantId, ct);
        var account = await db.CustomerAccounts
            .AsNoTracking()
            .Include(x => x.AccountType)
            .FirstAsync(x => x.Id == accountId, ct);

        var config = JsonSerializer.Deserialize<StampConfig>(account.AccountType.Config);
        if (config is null || account.Balance < config.StampTarget) return;

        // Idempotency: if a reward was already written for this event, do not produce another.
        // On redelivery, the reset entry would hit idempotency and the balance would not drop,
        // yet a second RewardLog could be written (double reward without deduction).
        var rewardExists = await db.RewardLogs.AnyAsync(x =>
            x.TenantId == tenantGuid &&
            x.AccountTypeId == account.AccountTypeId &&
            x.SourceEventId == eventId, ct);
        if (rewardExists) return;

        var completionCount = await db.RewardLogs
            .CountAsync(x => x.TenantId == tenantGuid && x.ContactKey == contactKey && x.AccountTypeId == account.AccountTypeId, ct) + 1;

        var resetEntry = await ledger.AddEntryAsync(
            tenantId: tenantId,
            customerAccountId: accountId,
            contactKey: contactKey,
            delta: -config.StampTarget,
            reason: LedgerReason.StampReset,
            sourceEventId: eventId,
            idempotencyKey: $"{eventId}:{rule.Id}:reset",
            ct: ct);

        // The active reward definition the tenant mapped to this stamp account.
        // If there is no definition, legacy behavior: pending record with the reward name from config.
        var definition = await db.RewardDefinitions
            .AsNoTracking()
            .FirstOrDefaultAsync(x =>
                x.TenantId == tenantGuid &&
                x.StampAccountTypeId == account.AccountTypeId &&
                x.Acquisition == RewardAcquisition.StampCompletion &&
                x.IsActive, ct);

        var now = DateTime.UtcNow;
        var reward = new RewardLog
        {
            Id = UUIDNext.Uuid.NewDatabaseFriendly(UUIDNext.Database.PostgreSql),
            TenantId = tenantGuid,
            ContactKey = contactKey,
            AccountTypeId = account.AccountTypeId,
            RewardName = definition?.Name ?? config.RewardType,
            SourceEventId = eventId,
            LedgerResetEntryId = resetEntry.Id,
            CompletionCount = completionCount,
            RewardDefinitionId = definition?.Id,
            Status = definition is null ? RewardLogStatus.Pending : RewardLogStatus.Notified,
            CreatedAt = now,
            DeliveredAt = definition is null ? null : now
        };

        db.RewardLogs.Add(reward);

        if (definition is not null)
        {
            await outbox.Enqueue(
                tenantId,
                dEngage.Loyalty.Shared.Events.OutboundEventTypes.RewardEarned,
                contactKey,
                new
                {
                    contact_key = contactKey,
                    reward_name = definition.Name,
                    reward_type = definition.RewardType,
                    source = RewardAcquisition.StampCompletion,
                    completion_count = completionCount,
                    source_event_id = eventId
                },
                dedupKey: $"reward_earned:{eventId}:{account.AccountTypeId}");

            logger.LogInformation(
                "RuleEngine: [{Tenant}] REWARD contact={Contact} reward={Reward} type={RewardType} completion={Count}",
                tenantId, contactKey, definition.Name, definition.RewardType, completionCount);
        }
        else
        {
            logger.LogWarning(
                "RuleEngine: [{Tenant}] stamp completed but no active reward_definition — account_type={AccountType} reward_type={RewardType} left pending",
                tenantId, account.AccountTypeId, config.RewardType);
        }

        await db.SaveChangesAsync(ct); // RewardLog + OutboxEvent in the same tx
    }
}

internal record StampConfig(
    [property: System.Text.Json.Serialization.JsonPropertyName("stamp_target")] decimal StampTarget,
    [property: System.Text.Json.Serialization.JsonPropertyName("reward_type")] string RewardType
);
