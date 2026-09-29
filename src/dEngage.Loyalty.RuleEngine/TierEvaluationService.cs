using dEngage.Loyalty.Ledger;
using dEngage.Loyalty.Schema;
using dEngage.Loyalty.Schema.Entities;
using dEngage.Loyalty.Shared;
using dEngage.Loyalty.Shared.Events;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using UUIDNext;

namespace dEngage.Loyalty.RuleEngine;

public class TierEvaluationService(LoyaltyDbContext db, IOutboxService outbox, ITenantSlugResolver tenantSlugResolver, ILogger<TierEvaluationService> logger) : ITierEvaluationService
{
    public async Task EvaluateAsync(
        string tenantId,
        string contactKey,
        Guid programId,
        Guid qualifyingAccountTypeId,
        string sourceEventId,
        CancellationToken ct = default)
    {
        try
        {
            await EvaluateInternalAsync(tenantId, contactKey, programId, qualifyingAccountTypeId, sourceEventId, ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "TierEval: failed for contact={ContactKey} tenant={Tenant} event={EventId} — will self-correct on next event",
                contactKey, tenantId, sourceEventId);
        }
    }

    private async Task EvaluateInternalAsync(
        string tenantId,
        string contactKey,
        Guid programId,
        Guid qualifyingAccountTypeId,
        string sourceEventId,
        CancellationToken ct)
    {
        var tenantGuid = await tenantSlugResolver.ResolveAsync(tenantId, ct);
        var account = await db.CustomerAccounts
            .FirstOrDefaultAsync(ca =>
                ca.TenantId == tenantGuid &&
                ca.ContactKey == contactKey &&
                ca.AccountTypeId == qualifyingAccountTypeId, ct);

        if (account is null)
        {
            logger.LogDebug("TierEval: no qualifying account yet contact={ContactKey} tenant={Tenant}",
                contactKey, tenantId);
            return;
        }

        var periodStart   = account.TierPeriodStart;
        var qualifyingPts = await ComputeQualifyingPtsAsync(account.Id, periodStart, ct);

        // Sort tiers by sort_order DESC — hierarchy consistent with TierDowngradeJob
        var tiers = await db.TierDefinitions
            .Where(t => t.ProgramId == programId)
            .OrderByDescending(t => t.SortOrder)
            .ToListAsync(ct);

        if (tiers.Count == 0) return;

        // Highest tier earned: the one with the highest sort_order (topmost), if min_points is met
        var newTier = tiers.FirstOrDefault(t => qualifyingPts >= t.MinPoints)
                      ?? tiers.OrderBy(t => t.SortOrder).First();

        if (account.TierId == newTier.Id) return;

        // The event flow only UPGRADES. Downgrading (with grace semantics) is the nightly job's duty.
        // Otherwise the first order after a period reset would instantly drag the customer's
        // tier down to the bottom with the new period's low points.
        if (account.TierId is not null)
        {
            var currentTier = tiers.FirstOrDefault(t => t.Id == account.TierId);
            if (currentTier is not null && newTier.SortOrder <= currentTier.SortOrder)
                return;
        }

        var oldTierId = account.TierId;
        var today     = DateOnly.FromDateTime(DateTime.UtcNow);

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        try
        {
            await db.CustomerAccounts
                .Where(ca => ca.Id == account.Id)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(ca => ca.TierId, newTier.Id)
                    .SetProperty(ca => ca.TierQualifyingPts, qualifyingPts)
                    .SetProperty(ca => ca.TierPeriodStart, ca => ca.TierPeriodStart ?? today)
                    .SetProperty(ca => ca.UpdatedAt, DateTime.UtcNow), ct);

            var log = new TierUpgradeLog
            {
                Id = Uuid.NewDatabaseFriendly(Database.PostgreSql),
                TenantId = tenantGuid,
                ContactKey = contactKey,
                FromTierId = oldTierId,
                ToTierId = newTier.Id,
                QualifyingPts = qualifyingPts,
                SourceEventId = sourceEventId,
                CreatedAt = DateTime.UtcNow
            };

            db.TierUpgradeLogs.Add(log);

            await outbox.Enqueue(
                tenantId,
                OutboundEventTypes.TierChanged,
                contactKey,
                new
                {
                    contact_key = contactKey,
                    from_tier = tiers.FirstOrDefault(t => t.Id == oldTierId)?.Name,
                    to_tier = newTier.Name,
                    direction = TierChangeDirection.Up,
                    qualifying_points = qualifyingPts
                        .ToString("F2", System.Globalization.CultureInfo.InvariantCulture)
                },
                dedupKey: $"tier_changed:{sourceEventId}:{account.Id}");

            await db.SaveChangesAsync(ct); // log + outbox in the same tx

            await tx.CommitAsync(ct);
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }

        logger.LogInformation(
            "TierEval: tier change contact={ContactKey} tenant={Tenant} {From} → {To} qualifying_pts={Pts}",
            contactKey, tenantId,
            oldTierId?.ToString() ?? "none",
            newTier.Name, qualifyingPts);
    }

    private async Task<decimal> ComputeQualifyingPtsAsync(
        Guid customerAccountId,
        DateOnly? periodStart,
        CancellationToken ct)
    {
        var query = db.LedgerEntries
            .Where(le =>
                le.CustomerAccountId == customerAccountId &&
                (le.Reason == LedgerReason.Earn || le.Reason == LedgerReason.StampEarn));

        if (periodStart.HasValue)
        {
            var periodStartDt = periodStart.Value.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
            query = query.Where(le => le.CreatedAt >= periodStartDt);
        }

        return await query.SumAsync(le => (decimal?)le.Delta, ct) ?? 0m;
    }
}
