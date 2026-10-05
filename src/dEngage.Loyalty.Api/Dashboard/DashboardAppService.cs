using dEngage.Loyalty.Schema;
using dEngage.Loyalty.Schema.Entities;
using dEngage.Loyalty.Shared;
using Microsoft.EntityFrameworkCore;

namespace dEngage.Loyalty.Api.Dashboard;

public interface IDashboardAppService
{
    Task<DashboardSummaryResponse> GetSummaryAsync(string tenantId, Guid? programId, DateTime? from, DateTime? to, CancellationToken ct);
}

// One composed endpoint rather than several frontend calls, matching the same
// embed-the-subquery-in-one-projection style ProgramsAppService.Project already uses for
// accountTypeCount/ruleCount — avoids N round-trips and N loading states on one dashboard view.
public sealed class DashboardAppService(
    LoyaltyDbContext db,
    ITenantSlugResolver tenantSlugResolver) : IDashboardAppService
{
    public async Task<DashboardSummaryResponse> GetSummaryAsync(string tenantId, Guid? programId, DateTime? from, DateTime? to, CancellationToken ct)
    {
        var tenantGuid = await tenantSlugResolver.ResolveAsync(tenantId, ct);

        var programs = db.Programs.Where(p => p.TenantId == tenantGuid && p.Status != ProgramStatus.Deleted);
        if (programId is not null) programs = programs.Where(p => p.Id == programId);
        var programCount = await programs.CountAsync(ct);

        var tiers = db.TierDefinitions.Where(t => t.TenantId == tenantGuid && t.Status != TierStatus.Deleted);
        if (programId is not null) tiers = tiers.Where(t => t.ProgramId == programId);
        var tierCount = await tiers.CountAsync(ct);

        var accounts = db.CustomerAccounts.Where(a => a.TenantId == tenantGuid);
        if (programId is not null) accounts = accounts.Where(a => a.AccountType.ProgramId == programId);
        var accountCount = await accounts.CountAsync(ct);
        var totalBalance = await accounts.SumAsync(a => (decimal?)a.Balance, ct) ?? 0m;

        var rewardLog = db.RewardLogs.Where(r => r.TenantId == tenantGuid);
        if (programId is not null) rewardLog = rewardLog.Where(r => r.AccountType.ProgramId == programId);
        if (from is not null) rewardLog = rewardLog.Where(r => r.CreatedAt >= from);
        if (to is not null) rewardLog = rewardLog.Where(r => r.CreatedAt < to);
        var redemptionCount = await rewardLog.CountAsync(ct);

        var streakProgress = db.StreakProgresses
            .Where(s => s.TenantId == tenantGuid && s.Status == StreakProgressStatus.Active);
        if (programId is not null)
            streakProgress = streakProgress.Where(s => db.StreakCampaigns.Any(c => c.Id == s.CampaignId && c.ProgramId == programId));
        var streakActiveCount = await streakProgress.CountAsync(ct);

        var streakLog = db.StreakLogs.Where(s => s.TenantId == tenantGuid);
        if (programId is not null)
            streakLog = streakLog.Where(s => db.StreakCampaigns.Any(c => c.Id == s.CampaignId && c.ProgramId == programId));
        if (from is not null) streakLog = streakLog.Where(s => s.CreatedAt >= from);
        if (to is not null) streakLog = streakLog.Where(s => s.CreatedAt < to);
        var streakCompletedInRange = await streakLog.CountAsync(ct);

        return new DashboardSummaryResponse(
            programCount, tierCount, accountCount, totalBalance,
            redemptionCount, streakActiveCount, streakCompletedInRange);
    }
}
