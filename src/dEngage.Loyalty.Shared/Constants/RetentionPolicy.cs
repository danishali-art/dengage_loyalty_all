namespace dEngage.Loyalty.Shared;

// event_log / streak_period_state retention is 12 months — the single business rule behind
// both StreakConfig's per-period target_periods cap and StreakMaintenanceJob's purge cutoff,
// previously duplicated as unrelated-looking magic numbers in each file independently.
public static class RetentionPolicy
{
    public const int EventRetentionMonths = 12;
    public const int MaxDailyStreakPeriods = 365;
    public const int MaxWeeklyStreakPeriods = 52;
    public const int MaxMonthlyStreakPeriods = EventRetentionMonths;

    // Purge cutoff: one extra month of margin beyond the retention window, so the nightly
    // recompute always has data to work with even right at the boundary.
    public const int PurgeSafetyMarginMonths = 1;
}
