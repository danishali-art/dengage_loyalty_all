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

public class PointsRedeemHandler(ILedgerService ledgerService, IOutboxService outbox, LoyaltyDbContext db, ITenantSlugResolver tenantSlugResolver) : IEventHandler
{
    public string? EventType => EventTypes.PointsRedeem;

    public async Task HandleAsync(EventEnvelope envelope, CancellationToken ct)
    {
        var data = envelope.Data;
        var contactKey = data.GetProperty("contact_key").GetString()!;
        var pointsAmount = decimal.Parse(data.GetProperty("points_amount").GetString()!, CultureInfo.InvariantCulture);
        var sourceAccountTypeId = Guid.Parse(data.GetProperty("source_account_type_id").GetString()!);

        var tenantGuid = await tenantSlugResolver.ResolveAsync(envelope.Tenant, ct);
        var accountType = await db.AccountTypes
            .FirstOrDefaultAsync(x =>
                x.TenantId == tenantGuid &&
                x.Id == sourceAccountTypeId &&
                x.Type == "POINTS", ct)
            ?? throw new InvalidOperationException("points_account_type_not_found");

        var config = JsonSerializer.Deserialize<PointsConfig>(accountType.Config)
            ?? throw new InvalidOperationException("redemption_not_configured");

        // Configuration errors stay hard failures (inbox failed → DLQ): a retry can't fix them
        // and they need an admin, not a customer-facing outcome event.
        if (config.Redemption is null)
            throw new InvalidOperationException("redemption_not_configured");

        var cashAmount = Math.Round(pointsAmount * config.Redemption.Rate, 2);
        var targetAccountTypeId = config.Redemption.TargetAccountTypeId;

        // Both legs (points debit + cash credit) in a single transaction; the points
        // balance is read under a row lock so it cannot change between check and debit.
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        // Redelivery: if the redeem was already written (crash after commit before the inbox
        // update), don't post or emit again. Must come BEFORE the minimum/balance checks — the
        // balance already debited on the first try would yield a wrong redeem_failed on the
        // second. Same guard as PointsTransferHandler.
        var debitIdempotencyKey = $"{envelope.EventId}:points_redeemed";
        if (await db.LedgerEntries.AnyAsync(x =>
                x.TenantId == envelope.Tenant &&
                x.IdempotencyKey == debitIdempotencyKey, ct))
            return;

        var pointsAccount = await ledgerService.LockAccountAsync(envelope.Tenant, contactKey, sourceAccountTypeId, ct)
            ?? await ledgerService.UpsertAccountAsync(envelope.Tenant, contactKey, sourceAccountTypeId, ct);

        // CR 2026-09-30 §3.9 step 4: below-minimum and insufficient points are business
        // outcomes, not processing errors — a retry won't change them. Decided by an explicit
        // check under the row lock (never by catching the ledger's exception), reported with
        // redeem_failed, committed → the inbox becomes 'processed'. Mirrors points.transfer.
        if (pointsAmount < config.Redemption.MinPoints)
        {
            await FailAsync(envelope, contactKey, pointsAmount, "below_minimum", pointsAccount.Balance, config.Redemption.MinPoints, ct);
            await tx.CommitAsync(ct);
            return;
        }

        if (pointsAccount.Balance < pointsAmount)
        {
            await FailAsync(envelope, contactKey, pointsAmount, "insufficient_points", pointsAccount.Balance, config.Redemption.MinPoints, ct);
            await tx.CommitAsync(ct);
            return;
        }

        var cashAccount = await ledgerService.UpsertAccountAsync(envelope.Tenant, contactKey, targetAccountTypeId, ct);

        var metadata = JsonSerializer.Serialize(new
        {
            redeemed_points = pointsAmount,
            cash_amount = cashAmount.ToString("F2", CultureInfo.InvariantCulture),
            rate = config.Redemption.Rate
        });

        await ledgerService.AddEntryAsync(
            tenantId: envelope.Tenant,
            customerAccountId: pointsAccount.Id,
            contactKey: contactKey,
            delta: -pointsAmount,
            reason: LedgerReason.PointsRedeemed,
            sourceEventId: envelope.EventId,
            idempotencyKey: debitIdempotencyKey,
            metadata: metadata,
            ct: ct);

        await ledgerService.AddEntryAsync(
            tenantId: envelope.Tenant,
            customerAccountId: cashAccount.Id,
            contactKey: contactKey,
            delta: cashAmount,
            reason: LedgerReason.PointsRedeemedCash,
            sourceEventId: envelope.EventId,
            idempotencyKey: $"{envelope.EventId}:points_redeemed_cash",
            metadata: metadata,
            ct: ct);

        await outbox.Enqueue(
            envelope.Tenant,
            OutboundEventTypes.PointsRedeemed,
            contactKey,
            new
            {
                contact_key = contactKey,
                points_amount = pointsAmount.ToString("F2", CultureInfo.InvariantCulture),
                cash_amount = cashAmount.ToString("F2", CultureInfo.InvariantCulture),
                source_account_type_id = sourceAccountTypeId.ToString(),
                cash_account_type_id = targetAccountTypeId.ToString(),
                points_balance = (pointsAccount.Balance - pointsAmount).ToString("F2", CultureInfo.InvariantCulture),
                source_event_id = envelope.EventId
            },
            dedupKey: $"points_redeemed:{envelope.EventId}");
        await db.SaveChangesAsync(ct);

        await tx.CommitAsync(ct);
    }

    private async Task FailAsync(
        EventEnvelope envelope,
        string contactKey,
        decimal pointsAmount,
        string reason,
        decimal balance,
        decimal minPoints,
        CancellationToken ct)
    {
        var failDedupKey = $"redeem_failed:{envelope.EventId}";
        var tenantGuid = await tenantSlugResolver.ResolveAsync(envelope.Tenant, ct);
        var alreadyEnqueued = await db.OutboxEvents.AnyAsync(x =>
            x.TenantId == tenantGuid && x.DedupKey == failDedupKey, ct);
        if (alreadyEnqueued) return;

        await outbox.Enqueue(
            envelope.Tenant,
            OutboundEventTypes.PointsRedeemFailed,
            contactKey,
            new
            {
                contact_key = contactKey,
                points_amount = pointsAmount.ToString("F2", CultureInfo.InvariantCulture),
                reason,
                balance = balance.ToString("F2", CultureInfo.InvariantCulture),
                min_points = minPoints.ToString("F2", CultureInfo.InvariantCulture),
                source_event_id = envelope.EventId
            },
            dedupKey: failDedupKey);
        await db.SaveChangesAsync(ct);
    }
}

file record PointsConfig(
    [property: JsonPropertyName("redemption")] RedemptionConfig? Redemption
);

file record RedemptionConfig(
    [property: JsonPropertyName("target_account_type_id")] Guid TargetAccountTypeId,
    [property: JsonPropertyName("rate")] decimal Rate,
    [property: JsonPropertyName("min_points")] decimal MinPoints
);
