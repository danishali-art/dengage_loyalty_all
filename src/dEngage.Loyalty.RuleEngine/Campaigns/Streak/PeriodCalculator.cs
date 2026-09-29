namespace dEngage.Loyalty.RuleEngine.Campaigns.Streak;

public static class PeriodCalculator
{
    // occurredAtUtc is converted to the rule's timezone before bucketing, so an
    // event at 23:30 UTC can fall into "tomorrow" for Asia/Riyadh day streaks.
    public static DateOnly GetPeriodStart(DateTime occurredAtUtc, TimeZoneInfo tz, string period, string weekStart)
    {
        var local = TimeZoneInfo.ConvertTimeFromUtc(
            DateTime.SpecifyKind(occurredAtUtc, DateTimeKind.Utc), tz);
        var date = DateOnly.FromDateTime(local);

        return period switch
        {
            StreakPeriod.Day => date,
            StreakPeriod.Week => StartOfWeek(date, weekStart),
            StreakPeriod.Month => new DateOnly(date.Year, date.Month, 1),
            _ => throw new InvalidOperationException($"unknown period '{period}'")
        };
    }

    public static DateOnly NextPeriodStart(DateOnly periodStart, string period) => period switch
    {
        StreakPeriod.Day => periodStart.AddDays(1),
        StreakPeriod.Week => periodStart.AddDays(7),
        StreakPeriod.Month => periodStart.AddMonths(1),
        _ => throw new InvalidOperationException($"unknown period '{period}'")
    };

    public static DateOnly PreviousPeriodStart(DateOnly periodStart, string period) => period switch
    {
        StreakPeriod.Day => periodStart.AddDays(-1),
        StreakPeriod.Week => periodStart.AddDays(-7),
        StreakPeriod.Month => periodStart.AddMonths(-1),
        _ => throw new InvalidOperationException($"unknown period '{period}'")
    };

    private static DateOnly StartOfWeek(DateOnly date, string weekStart)
    {
        var firstDay = weekStart == StreakWeekStart.Sunday ? DayOfWeek.Sunday : DayOfWeek.Monday;
        var diff = ((int)date.DayOfWeek - (int)firstDay + 7) % 7;
        return date.AddDays(-diff);
    }
}
