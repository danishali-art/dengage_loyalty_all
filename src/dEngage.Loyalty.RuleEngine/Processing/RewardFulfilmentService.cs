using System.Globalization;
using System.Text.Json;
using dEngage.Loyalty.Ledger;
using dEngage.Loyalty.Schema;
using dEngage.Loyalty.Schema.Entities;
using dEngage.Loyalty.Shared;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace dEngage.Loyalty.RuleEngine.Processing;

public sealed class RewardFulfilmentService(
    LoyaltyDbContext db,
    ILedgerService ledger,
    IOutboxService outbox,
    ILogger<RewardFulfilmentService> logger) : IRewardFulfilmentService
{
    public Task<bool> IsProgramLiveAsync(Guid tenantGuid, Guid programId, CancellationToken ct) =>
        db.IsProgramLiveAsync(tenantGuid, programId, ct);

    public Task<RewardFulfilment> FulfilAsync(
        string tenantSlug, Guid tenantGuid, RewardDefinition reward, string contactKey,
        string sourceEventId, string idempotencyBase, CancellationToken ct) =>
        reward.RewardType switch
        {
            RewardType.Cashback => CreditCashbackAsync(tenantSlug, tenantGuid, reward, contactKey, sourceEventId, idempotencyBase, ct),
            RewardType.TierUpgrade => UpgradeTierAsync(tenantSlug, tenantGuid, reward, contactKey, sourceEventId, idempotencyBase, ct),
            // Retired types are deactivated (A1) and never reach here; anything else is reported, not paid.
            _ => Task.FromResult(NotFulfillable(reward, "unsupported_reward_type"))
        };

    // §3.5: a ledger credit to the reward's CASH wallet. Deterministic idempotency key, so a
    // redelivered purchase or a re-run completion never pays twice. No ruleId: a streak campaign
    // id is not a rules.id (ledger_entries.rule_id references rules) — the source is in metadata.
    private async Task<RewardFulfilment> CreditCashbackAsync(
        string tenantSlug, Guid tenantGuid, RewardDefinition reward, string contactKey,
        string sourceEventId, string idempotencyBase, CancellationToken ct)
    {
        // O10: real money never flows from a draft or paused program.
        if (!await IsProgramLiveAsync(tenantGuid, reward.ProgramId, ct))
        {
            logger.LogWarning("RewardFulfilment: [{Tenant}] cashback reward {Reward} not paid — program {Program} is not published and active",
                tenantSlug, reward.Name, reward.ProgramId);
            return new RewardFulfilment(RewardFulfilmentOutcome.ProgramNotLive,
                new Dictionary<string, object?> { ["outcome"] = RewardFulfilmentOutcome.ProgramNotLive }, null);
        }

        var cfg = JsonDocument.Parse(reward.TypeConfig).RootElement;
        if (!TryString(cfg, "amount", out var amountText)
            || !decimal.TryParse(amountText, NumberStyles.Number, CultureInfo.InvariantCulture, out var amount) || amount <= 0
            || !TryString(cfg, "cash_account_type_id", out var walletText) || !Guid.TryParse(walletText, out var walletId))
            return NotFulfillable(reward, "invalid_cashback_config");

        var account = await ledger.UpsertAccountAsync(tenantSlug, contactKey, walletId, ct);
        var entry = await ledger.AddEntryAsync(
            tenantId: tenantSlug,
            customerAccountId: account.Id,
            contactKey: contactKey,
            delta: amount,
            reason: LedgerReason.RewardCashback,
            sourceEventId: sourceEventId,
            idempotencyKey: $"{idempotencyBase}:reward_cashback",
            metadata: JsonSerializer.Serialize(new { reward_id = reward.Id, reward_name = reward.Name, source = reward.Acquisition, grant = idempotencyBase }),
            ct: ct);

        return new RewardFulfilment(RewardFulfilmentOutcome.CashCredited,
            new Dictionary<string, object?>
            {
                ["outcome"] = RewardFulfilmentOutcome.CashCredited,
                ["cashback_amount"] = amountText,
                ["currency"] = TryString(cfg, "currency", out var currency) ? currency : null,
                ["cash_account_type_id"] = walletId.ToString(),
                ["ledger_entry_id"] = entry.Id.ToString()
            },
            entry.Id.ToString());
    }

    // §3.6: move the customer's tier-qualifying account up to the target tier — never down. With
    // duration_days the tier is locked against TierDowngradeJob until then (O4: without it the
    // normal lifecycle applies). tier_period_start is not reset (O5).
    private async Task<RewardFulfilment> UpgradeTierAsync(
        string tenantSlug, Guid tenantGuid, RewardDefinition reward, string contactKey,
        string sourceEventId, string idempotencyBase, CancellationToken ct)
    {
        var cfg = JsonDocument.Parse(reward.TypeConfig).RootElement;
        if (!TryString(cfg, "target_tier_id", out var tierText) || !Guid.TryParse(tierText, out var targetTierId))
            return NotFulfillable(reward, "invalid_tier_upgrade_config");

        var qualifying = await db.AccountTypes.AsNoTracking().FirstOrDefaultAsync(a =>
            a.TenantId == tenantGuid && a.ProgramId == reward.ProgramId && a.IsTierQualifying, ct);
        var tiers = await db.TierDefinitions.AsNoTracking()
            .Where(t => t.TenantId == tenantGuid && t.ProgramId == reward.ProgramId && t.Status != TierStatus.Deleted)
            .ToListAsync(ct);
        var target = tiers.FirstOrDefault(t => t.Id == targetTierId);
        if (qualifying is null || target is null)
            return NotFulfillable(reward, qualifying is null ? "no_tier_qualifying_account" : "target_tier_missing");

        var logKey = $"{idempotencyBase}:tier_upgrade";
        var account = await ledger.UpsertAccountAsync(tenantSlug, contactKey, qualifying.Id, ct);
        // Read the tier from the DB, not from `account`: Upsert can hand back an instance this
        // DbContext already tracks, whose TierId is stale after a set-based update
        // (TierEvaluationService / this method use ExecuteUpdate). A stale "no tier" here would
        // upgrade a customer who is already above the target.
        var stored = await db.CustomerAccounts.AsNoTracking()
            .Where(ca => ca.Id == account.Id)
            .Select(ca => new { ca.TierId, ca.TierQualifyingPts })
            .SingleAsync(ct);
        var current = tiers.FirstOrDefault(t => t.Id == stored.TierId);

        // Redelivery: the upgrade was already applied for this grant — report it, don't redo it.
        var alreadyApplied = await db.TierUpgradeLogs.AnyAsync(l =>
            l.TenantId == tenantGuid && l.ContactKey == contactKey && l.SourceEventId == logKey, ct);

        if (!alreadyApplied && current is not null && current.SortOrder >= target.SortOrder)
            return new RewardFulfilment(RewardFulfilmentOutcome.AlreadyAtOrAbove,
                new Dictionary<string, object?>
                {
                    ["outcome"] = RewardFulfilmentOutcome.AlreadyAtOrAbove,
                    ["target_tier"] = target.Name,
                    ["current_tier"] = current.Name
                },
                null);

        DateOnly? lockedUntil = cfg.TryGetProperty("duration_days", out var d) && d.ValueKind == JsonValueKind.Number && d.TryGetInt32(out var days) && days > 0
            ? DateOnly.FromDateTime(DateTime.UtcNow).AddDays(days)
            : null;

        if (!alreadyApplied)
        {
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            await db.CustomerAccounts
                .Where(ca => ca.Id == account.Id)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(ca => ca.TierId, target.Id)
                    .SetProperty(ca => ca.TierLockedUntil, ca => lockedUntil ?? ca.TierLockedUntil)
                    // Same as TierEvaluationService: start a period if there is none; never reset one (O5).
                    .SetProperty(ca => ca.TierPeriodStart, ca => ca.TierPeriodStart ?? today)
                    .SetProperty(ca => ca.UpdatedAt, DateTime.UtcNow), ct);

            db.TierUpgradeLogs.Add(new TierUpgradeLog
            {
                Id = UUIDNext.Uuid.NewDatabaseFriendly(UUIDNext.Database.PostgreSql),
                TenantId = tenantGuid,
                ContactKey = contactKey,
                FromTierId = current?.Id,
                ToTierId = target.Id,
                QualifyingPts = stored.TierQualifyingPts,
                SourceEventId = logKey,
                CreatedAt = DateTime.UtcNow
            });

            await outbox.Enqueue(
                tenantSlug,
                dEngage.Loyalty.Shared.Events.OutboundEventTypes.TierChanged,
                contactKey,
                new
                {
                    contact_key = contactKey,
                    from_tier = current?.Name,
                    to_tier = target.Name,
                    direction = TierChangeDirection.Up,
                    qualifying_points = stored.TierQualifyingPts.ToString("F2", CultureInfo.InvariantCulture),
                    // Additive: tells consumers this change came from a reward, not from points.
                    reason = "reward",
                    tier_locked_until = lockedUntil?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                },
                dedupKey: $"tier_changed:{logKey}", ct: ct);

            logger.LogInformation("RewardFulfilment: [{Tenant}] tier upgrade contact={Contact} {From} -> {To} locked_until={Until}",
                tenantSlug, contactKey, current?.Name ?? "none", target.Name, lockedUntil);
        }

        return new RewardFulfilment(RewardFulfilmentOutcome.TierUpgraded,
            new Dictionary<string, object?>
            {
                ["outcome"] = RewardFulfilmentOutcome.TierUpgraded,
                ["target_tier"] = target.Name,
                ["tier_locked_until"] = lockedUntil?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            },
            target.Id.ToString());
    }

    private RewardFulfilment NotFulfillable(RewardDefinition reward, string reason)
    {
        logger.LogWarning("RewardFulfilment: reward {Reward} ({Type}) not fulfilled — {Reason}", reward.Name, reward.RewardType, reason);
        return new RewardFulfilment(RewardFulfilmentOutcome.NotFulfillable,
            new Dictionary<string, object?> { ["outcome"] = RewardFulfilmentOutcome.NotFulfillable, ["reason"] = reason }, null);
    }

    private static bool TryString(JsonElement cfg, string field, out string value)
    {
        value = string.Empty;
        if (cfg.ValueKind != JsonValueKind.Object || !cfg.TryGetProperty(field, out var v) || v.ValueKind != JsonValueKind.String)
            return false;
        value = v.GetString() ?? string.Empty;
        return value.Length > 0;
    }
}
