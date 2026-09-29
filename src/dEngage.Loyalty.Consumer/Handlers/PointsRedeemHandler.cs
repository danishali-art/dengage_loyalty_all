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

public class PointsRedeemHandler(ILedgerService ledgerService, LoyaltyDbContext db, ITenantSlugResolver tenantSlugResolver) : IEventHandler
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

        if (config.Redemption is null)
            throw new InvalidOperationException("redemption_not_configured");

        if (pointsAmount < config.Redemption.MinPoints)
            throw new InvalidOperationException($"below_minimum: {pointsAmount} < {config.Redemption.MinPoints}");

        var cashAmount = Math.Round(pointsAmount * config.Redemption.Rate, 2);
        var targetAccountTypeId = config.Redemption.TargetAccountTypeId;

        // Both legs (points debit + cash credit) in a single transaction; the points
        // balance is read under a row lock so it cannot change between check and debit.
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var pointsAccount = await ledgerService.LockAccountAsync(envelope.Tenant, contactKey, sourceAccountTypeId, ct)
            ?? await ledgerService.UpsertAccountAsync(envelope.Tenant, contactKey, sourceAccountTypeId, ct);

        if (pointsAccount.Balance < pointsAmount)
            throw new InvalidOperationException($"insufficient_points: {pointsAccount.Balance} < {pointsAmount}");

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
            idempotencyKey: $"{envelope.EventId}:points_redeemed",
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

        await tx.CommitAsync(ct);
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
