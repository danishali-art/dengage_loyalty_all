using dEngage.Loyalty.Schema.Entities;

namespace dEngage.Loyalty.RuleEngine.Campaigns.Streak;

// Adds the one operation StreakMaintenanceJob needs beyond the generic ICampaignModule
// contract: completing a streak directly from a recompute, without going through the
// per-event EvaluateAsync path (there is no inbound event driving a nightly recompute).
public interface IStreakCampaignModule : ICampaignModule
{
    // Also called by StreakMaintenanceJob when a recompute uncovers a missed completion.
    // Does NOT SaveChanges — the caller's transaction owns persistence.
    Task<bool> CompleteAsync(
        Guid tenantId,
        string eventId,
        CachedCampaignConfig config,
        StreakProgress progress,
        DateOnly completedPeriod,
        DateTime now,
        CancellationToken ct);
}
