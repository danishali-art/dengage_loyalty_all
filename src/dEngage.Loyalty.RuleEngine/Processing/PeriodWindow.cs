namespace dEngage.Loyalty.RuleEngine.Processing;

// CR-07 (docs/scope-change-rules A7/A8): resolves a Period ("Day"|"Week"|"Month"|"Year") +
// ResetWindow ("Calendar"|"Rolling") pair into a concrete [start, now] window for a limit query.
// UTC only for now — A8's "midnight, program timezone" wording is not honored here (the
// existing PerCustomerPerDay counter already only ever used DateTime.UtcNow.Date, so this is
// consistent with current behavior, not a regression); program-timezone-aware boundaries are
// CR-08 (Configuration) territory.
public static class PeriodWindow
{
    public static DateTime Start(string? period, string? resetWindow, DateTime now)
    {
        var p = period ?? "Day";
        var rolling = resetWindow == "Rolling";

        if (rolling)
        {
            return p switch
            {
                "Week" => now.AddDays(-7),
                "Month" => now.AddMonths(-1),
                "Year" => now.AddYears(-1),
                _ => now.AddDays(-1)
            };
        }

        // Calendar (default): the start of the current calendar Day/Week/Month/Year, UTC.
        return p switch
        {
            "Week" => now.Date.AddDays(-(int)now.DayOfWeek), // week starts Sunday, UTC
            "Month" => new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc),
            "Year" => new DateTime(now.Year, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            _ => now.Date
        };
    }

    // CR-09: a discrete bucket identifier for the CURRENT calendar period — the key
    // IBudgetReservationService's counters accumulate against. Only meaningful for Calendar
    // reset (a Rolling window has no discrete bucket to key a persistent counter on; callers
    // fall back to a fresh ledger-query sum for that case instead — see BudgetReservationService).
    public static string CalendarKey(string? period, DateTime now) => (period ?? "Day") switch
    {
        "Week" => Start("Week", "Calendar", now).ToString("yyyy-MM-dd"),
        "Month" => now.ToString("yyyy-MM"),
        "Year" => now.ToString("yyyy"),
        _ => now.ToString("yyyy-MM-dd")
    };
}
