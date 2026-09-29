using dEngage.Loyalty.Consumer;
using Spectre.Console;

/// <summary>
/// Verifies the schedule logic (NextRunAt) of the Consumer workers.
/// TierDowngradeWorker → 00:00 UTC, PointsExpiringDetectorWorker → 01:00 UTC,
/// PointsExpirationWorker → 03:00 UTC.
/// </summary>
public static class WorkerScheduleVerify
{
    static int _pass, _fail;

    public static Task RunAsync()
    {
        AnsiConsole.Write(new Rule("[bold]Worker Schedule Verify[/]").LeftJustified());

        VerifyTierWorker();
        VerifyExpiringDetectorWorker();
        VerifyExpireWorker();
        VerifyOrdering();

        AnsiConsole.WriteLine();
        var color = _fail == 0 ? "green" : "red";
        AnsiConsole.MarkupLine($"[{color}]PASSED: {_pass}[/]  [red]FAILED: {_fail}[/]");
        return Task.CompletedTask;
    }

    static void VerifyTierWorker()
    {
        AnsiConsole.MarkupLine("\n[bold]TierDowngradeWorker — 00:00 UTC[/]");

        Check("just after midnight (00:00:01) → next night 00:00",
            new DateTime(2026, 7, 2, 0, 0, 0, DateTimeKind.Utc),
            TierDowngradeWorker.NextRunAt(new DateTime(2026, 7, 1, 0, 0, 1, DateTimeKind.Utc)));

        Check("midday (12:30) → next night 00:00",
            new DateTime(2026, 7, 2, 0, 0, 0, DateTimeKind.Utc),
            TierDowngradeWorker.NextRunAt(new DateTime(2026, 7, 1, 12, 30, 0, DateTimeKind.Utc)));

        Check("end of day (23:59:59) → next night 00:00",
            new DateTime(2026, 7, 2, 0, 0, 0, DateTimeKind.Utc),
            TierDowngradeWorker.NextRunAt(new DateTime(2026, 7, 1, 23, 59, 59, DateTimeKind.Utc)));

        Check("exactly 00:00:00.000 → doesn't skip today, runs immediately",
            new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc),
            TierDowngradeWorker.NextRunAt(new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc)));

        Check("end of month (31 Jul 18:00) → 1 Aug 00:00",
            new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc),
            TierDowngradeWorker.NextRunAt(new DateTime(2026, 7, 31, 18, 0, 0, DateTimeKind.Utc)));

        Check("end of year (31 Dec 23:00) → 1 Jan 00:00",
            new DateTime(2027, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            TierDowngradeWorker.NextRunAt(new DateTime(2026, 12, 31, 23, 0, 0, DateTimeKind.Utc)));

        var next = TierDowngradeWorker.NextRunAt(DateTime.UtcNow);
        Check("delay is always positive (relative to now)", true, next >= DateTime.UtcNow);
    }

    static void VerifyExpiringDetectorWorker()
    {
        AnsiConsole.MarkupLine("\n[bold]PointsExpiringDetectorWorker — 01:00 UTC[/]");

        Check("before 01:00 (00:30) → same day 01:00",
            new DateTime(2026, 7, 1, 1, 0, 0, DateTimeKind.Utc),
            PointsExpiringDetectorWorker.NextRunAt(new DateTime(2026, 7, 1, 0, 30, 0, DateTimeKind.Utc)));

        Check("exactly 01:00:00.000 → doesn't skip today, runs immediately",
            new DateTime(2026, 7, 1, 1, 0, 0, DateTimeKind.Utc),
            PointsExpiringDetectorWorker.NextRunAt(new DateTime(2026, 7, 1, 1, 0, 0, DateTimeKind.Utc)));

        Check("just after 01:00 (01:00:01) → next day 01:00",
            new DateTime(2026, 7, 2, 1, 0, 0, DateTimeKind.Utc),
            PointsExpiringDetectorWorker.NextRunAt(new DateTime(2026, 7, 1, 1, 0, 1, DateTimeKind.Utc)));

        Check("midday (14:00) → next day 01:00",
            new DateTime(2026, 7, 2, 1, 0, 0, DateTimeKind.Utc),
            PointsExpiringDetectorWorker.NextRunAt(new DateTime(2026, 7, 1, 14, 0, 0, DateTimeKind.Utc)));

        Check("end of day (23:59) → next day 01:00",
            new DateTime(2026, 7, 2, 1, 0, 0, DateTimeKind.Utc),
            PointsExpiringDetectorWorker.NextRunAt(new DateTime(2026, 7, 1, 23, 59, 0, DateTimeKind.Utc)));

        var next = PointsExpiringDetectorWorker.NextRunAt(DateTime.UtcNow);
        Check("delay is always positive (relative to now)", true, next >= DateTime.UtcNow);
    }

    static void VerifyExpireWorker()
    {
        AnsiConsole.MarkupLine("\n[bold]PointsExpirationWorker — 03:00 UTC[/]");

        Check("before 03:00 (01:30) → same day 03:00",
            new DateTime(2026, 7, 1, 3, 0, 0, DateTimeKind.Utc),
            PointsExpirationWorker.NextRunAt(new DateTime(2026, 7, 1, 1, 30, 0, DateTimeKind.Utc)));

        Check("exactly 03:00:00.000 → doesn't skip today, runs immediately",
            new DateTime(2026, 7, 1, 3, 0, 0, DateTimeKind.Utc),
            PointsExpirationWorker.NextRunAt(new DateTime(2026, 7, 1, 3, 0, 0, DateTimeKind.Utc)));

        Check("just after 03:00 (03:00:01) → next day 03:00",
            new DateTime(2026, 7, 2, 3, 0, 0, DateTimeKind.Utc),
            PointsExpirationWorker.NextRunAt(new DateTime(2026, 7, 1, 3, 0, 1, DateTimeKind.Utc)));

        Check("midnight (00:00) → same day 03:00",
            new DateTime(2026, 7, 1, 3, 0, 0, DateTimeKind.Utc),
            PointsExpirationWorker.NextRunAt(new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc)));

        Check("end of day (23:59) → next day 03:00",
            new DateTime(2026, 7, 2, 3, 0, 0, DateTimeKind.Utc),
            PointsExpirationWorker.NextRunAt(new DateTime(2026, 7, 1, 23, 59, 0, DateTimeKind.Utc)));

        var next = PointsExpirationWorker.NextRunAt(DateTime.UtcNow);
        Check("delay is always positive (relative to now)", true, next >= DateTime.UtcNow);
    }

    // The nightly order must be fixed: tier downgrade (00:00) → expiring warning (01:00)
    // → expire (03:00). Qualifying points are evaluated before they lapse, and the warning
    // is produced before the last tranche that will lapse that same night.
    static void VerifyOrdering()
    {
        AnsiConsole.MarkupLine("\n[bold]Ordering — tier (00:00) < expiring (01:00) < expire (03:00)[/]");

        var samples = new[]
        {
            new DateTime(2026, 7, 1, 23, 30, 0, DateTimeKind.Utc),
            new DateTime(2026, 7, 1, 4, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 12, 31, 22, 0, 0, DateTimeKind.Utc)
        };

        foreach (var now in samples)
        {
            var tierNext = TierDowngradeWorker.NextRunAt(now);
            var expiringNext = PointsExpiringDetectorWorker.NextRunAt(now);
            var expireNext = PointsExpirationWorker.NextRunAt(now);
            var sameNight = tierNext < expiringNext && expiringNext < expireNext
                && (expiringNext - tierNext) == TimeSpan.FromHours(1)
                && (expireNext - expiringNext) == TimeSpan.FromHours(2);
            Check($"{now:MM-dd HH:mm} → tier {tierNext:MM-dd HH:mm}, expiring {expiringNext:MM-dd HH:mm}, expire {expireNext:MM-dd HH:mm}",
                true, sameNight);
        }
    }

    static void Check<T>(string label, T expected, T actual)
    {
        if (EqualityComparer<T>.Default.Equals(expected, actual))
        {
            _pass++;
            AnsiConsole.MarkupLine($"  [green]✓[/] {label}");
        }
        else
        {
            _fail++;
            AnsiConsole.MarkupLine($"  [red]✗[/] {label} — expected: {expected}, actual: {actual}");
        }
    }
}
