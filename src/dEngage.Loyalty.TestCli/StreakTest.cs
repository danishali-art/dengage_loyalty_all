using System.Diagnostics;
using System.Text;
using System.Text.Json;
using dEngage.Loyalty.Ledger;
using dEngage.Loyalty.RuleEngine.Campaigns.Streak;
using dEngage.Loyalty.Schema;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;
using RabbitMQ.Client;
using Spectre.Console;

/// <summary>
/// Streak engine end-to-end test — fintech tenant, card.transaction trigger.
///
/// Since periods are derived from occurred_at, day/week streaks can be set up with
/// backdated events without waiting in real time. Rules use the UTC timezone
/// (deterministic regardless of when the test runs); Asia/Riyadh is verified only
/// in PeriodCalculator's pure unit asserts.
///
/// Note: the card.transaction routing key must be bound in the consumer via
/// RabbitMq:GenericEventTypes; the consumer must be running the NEW binary that
/// includes streak support.
/// </summary>
public static class StreakTest
{
    const string PG        = "Host=localhost;Database=loyalty_dev;Username=postgres;Password=postgres";
    const string TENANT    = "fintech";
    const string FinPuanId = "019fd002-0000-7000-8000-000000000001"; // POINTS
    const string ProgramId = "019fd001-0000-7000-8000-000000000001";

    const string DayRuleId  = "019fd003-bbbb-7000-8000-000000000001"; // day, target 3, count>=1 → +15
    const string SumRuleId  = "019fd003-bbbb-7000-8000-000000000002"; // day, target 2, sum>=1000 → +25
    const string StopRuleId = "019fd003-bbbb-7000-8000-000000000003"; // day, target 2, on_complete=stop → +10
    const string WeekRuleId = "019fd003-bbbb-7000-8000-000000000004"; // week/sunday, target 2 → +30

    static int _passed = 0;
    static int _failed = 0;
    static string _tid = "";

    public static async Task RunAsync(IModel channel)
    {
        _tid = await ScalarStringAsync($"SELECT id::text FROM tenants WHERE slug='{TENANT}'") ?? "";
        AnsiConsole.Write(new Rule("[yellow bold]Streak Test — fintech[/]").RuleStyle("grey"));
        AnsiConsole.MarkupLine("[grey]Period accounting + consecutiveness + completion/reward + break/recompute + idempotency.[/]\n");

        // Anchor mid-day UTC — avoid date drift on runs close to a day boundary
        var anchor = DateTime.UtcNow.Date.AddHours(12);
        var today = DateOnly.FromDateTime(anchor);

        await SetupAsync();

        // ── ST00: PeriodCalculator pure unit asserts ─────────────────
        await RunTest("ST00 — PeriodCalculator: tz day rollover, week_start, month, next/prev", () =>
        {
            var riyadh = TimeZoneInfo.FindSystemTimeZoneById("Asia/Riyadh");

            // 22:30 UTC = 01:30 Riyadh the next day
            Assert("Riyadh day rollover (22:30Z → +1 day)",
                PeriodCalculator.GetPeriodStart(new DateTime(2026, 8, 28, 22, 30, 0, DateTimeKind.Utc), riyadh, "day", "monday").ToString("O"),
                "2026-08-29");
            Assert("Riyadh same day (20:00Z)",
                PeriodCalculator.GetPeriodStart(new DateTime(2026, 8, 28, 20, 0, 0, DateTimeKind.Utc), riyadh, "day", "monday").ToString("O"),
                "2026-08-28");

            var utc = TimeZoneInfo.Utc;
            // 2026-08-26 is a Wednesday
            Assert("week monday: Wednesday → Monday",
                PeriodCalculator.GetPeriodStart(new DateTime(2026, 8, 26, 10, 0, 0, DateTimeKind.Utc), utc, "week", "monday").ToString("O"),
                "2026-08-24");
            Assert("week sunday: Wednesday → Sunday",
                PeriodCalculator.GetPeriodStart(new DateTime(2026, 8, 26, 10, 0, 0, DateTimeKind.Utc), utc, "week", "sunday").ToString("O"),
                "2026-08-23");
            Assert("week sunday: Sunday itself",
                PeriodCalculator.GetPeriodStart(new DateTime(2026, 8, 23, 10, 0, 0, DateTimeKind.Utc), utc, "week", "sunday").ToString("O"),
                "2026-08-23");

            Assert("month: the 1st of the month",
                PeriodCalculator.GetPeriodStart(new DateTime(2026, 8, 26, 10, 0, 0, DateTimeKind.Utc), utc, "month", "monday").ToString("O"),
                "2026-08-01");

            Assert("next day", PeriodCalculator.NextPeriodStart(new DateOnly(2026, 8, 31), "day").ToString("O"), "2026-09-01");
            Assert("next week", PeriodCalculator.NextPeriodStart(new DateOnly(2026, 8, 24), "week").ToString("O"), "2026-08-31");
            Assert("next month", PeriodCalculator.NextPeriodStart(new DateOnly(2026, 1, 1), "month").ToString("O"), "2026-02-01");
            Assert("prev day", PeriodCalculator.PreviousPeriodStart(new DateOnly(2026, 9, 1), "day").ToString("O"), "2026-08-31");
            Assert("prev month", PeriodCalculator.PreviousPeriodStart(new DateOnly(2026, 1, 1), "month").ToString("O"), "2025-12-01");
            return Task.CompletedTask;
        });

        // ── ST01: day streak — 3 consecutive days → completion + reward ───────
        var st01ThirdEventId = Guid.NewGuid().ToString();
        await RunTest("ST01 — 3 consecutive days of card.transaction (mcc 6011) → +15, streak_log, restart", async () =>
        {
            await PublishTxAsync(channel, "st_a", "6011", "50.00", anchor.AddDays(-2));
            await WaitAsync();
            Assert("day 1: streak_count=1", await ProgressCountAsync(DayRuleId, "st_a"), 1);

            await PublishTxAsync(channel, "st_a", "6011", "50.00", anchor.AddDays(-1));
            await WaitAsync();
            Assert("day 2: streak_count=2", await ProgressCountAsync(DayRuleId, "st_a"), 2);
            Assert("no reward yet", await GetBalanceAsync("st_a", FinPuanId), 0m);

            await PublishTxAsync(channel, "st_a", "6011", "50.00", anchor, eventId: st01ThirdEventId);
            await WaitAsync();

            Assert("completion: FinPuan +15", await GetBalanceAsync("st_a", FinPuanId), 15m);
            Assert("streak_log 1 row, completion_no=1", await ScalarIntAsync(
                $"SELECT COUNT(*) FROM streak_log WHERE tenant_id='{_tid}' AND campaign_id='{DayRuleId}' AND contact_key='st_a' AND completion_no=1"), 1);
            Assert("ledger idempotency_key streak:...:1", await ScalarIntAsync(
                $"SELECT COUNT(*) FROM ledger_entries WHERE tenant_id='{TENANT}' AND idempotency_key='streak:{DayRuleId}:st_a:1'"), 1);
            Assert("outbox streak.completed", await ScalarIntAsync(
                $"SELECT COUNT(*) FROM outbox_events WHERE tenant_id='{_tid}' AND event_type='loyalty.streak.completed' AND dedup_key='streak_completed:{DayRuleId}:st_a:1'"), 1);
            Assert("restart: streak_count=0", await ProgressCountAsync(DayRuleId, "st_a"), 0);
            Assert("completions=1", await ScalarIntAsync(
                $"SELECT completions FROM streak_progress WHERE tenant_id='{_tid}' AND campaign_id='{DayRuleId}' AND contact_key='st_a'"), 1);
            Assert("status=active", await ScalarStringAsync(
                $"SELECT status FROM streak_progress WHERE tenant_id='{_tid}' AND campaign_id='{DayRuleId}' AND contact_key='st_a'"), "active");
        });

        // ── ST02: redelivery — inbox dedup + streak_applied_event anchor ─
        await RunTest("ST02 — same event published 2nd time → inbox dedup; anchor simulation → single increment", async () =>
        {
            await PublishTxAsync(channel, "st_a", "6011", "50.00", anchor, eventId: st01ThirdEventId);
            await WaitAsync();
            Assert("balance still 15", await GetBalanceAsync("st_a", FinPuanId), 15m);
            Assert("today's period_state agg_count=1", await AggCountAsync(DayRuleId, "st_a", today), 1);

            // Crash-redelivery simulation: a new event_id not caught by the inbox, but the anchor already exists
            var replayId = Guid.NewGuid().ToString();
            await ExecuteSqlAsync($"""
                INSERT INTO streak_applied_event (tenant_id, campaign_id, event_id, applied_at)
                VALUES ('{_tid}', '{DayRuleId}', '{replayId}', now())
                """);
            await PublishTxAsync(channel, "st_a", "6011", "50.00", anchor, eventId: replayId);
            await WaitAsync();
            Assert("anchor: agg_count still 1", await AggCountAsync(DayRuleId, "st_a", today), 1);
            Assert("streak_log still 1", await ScalarIntAsync(
                $"SELECT COUNT(*) FROM streak_log WHERE tenant_id='{_tid}' AND campaign_id='{DayRuleId}' AND contact_key='st_a'"), 1);
        });

        // ── ST03: extra event in the same period → doesn't count toward a new streak ─────
        await RunTest("ST03 — new event same day after completion → count stays 0 (restart semantics)", async () =>
        {
            await PublishTxAsync(channel, "st_a", "6011", "50.00", anchor);
            await WaitAsync();
            Assert("agg_count=2 (period row processed)", await AggCountAsync(DayRuleId, "st_a", today), 2);
            Assert("streak_count still 0", await ProgressCountAsync(DayRuleId, "st_a"), 0);
            Assert("no second reward", await GetBalanceAsync("st_a", FinPuanId), 15m);
        });

        // ── ST04: sum threshold — 600+500 same day → met; next day 1100 ────
        await RunTest("ST04 — sum>=1000: 600 isn't enough, met with +500; day 2 → completion +25", async () =>
        {
            await PublishTxAsync(channel, "st_b", "6012", "600.00", anchor.AddDays(-1));
            await WaitAsync();
            Assert("600 < 1000: not met, count=0", await ProgressCountAsync(SumRuleId, "st_b"), 0);

            await PublishTxAsync(channel, "st_b", "6012", "500.00", anchor.AddDays(-1));
            await WaitAsync();
            Assert("1100 >= 1000: count=1", await ProgressCountAsync(SumRuleId, "st_b"), 1);
            Assert("agg_sum=1100", await ScalarIntAsync(
                $"SELECT COUNT(*) FROM streak_period_state WHERE tenant_id='{_tid}' AND campaign_id='{SumRuleId}' AND contact_key='st_b' AND period_start='{today.AddDays(-1):yyyy-MM-dd}' AND agg_sum=1100 AND met"), 1);

            await PublishTxAsync(channel, "st_b", "6012", "1100.00", anchor);
            await WaitAsync();
            Assert("2/2 completion: FinPuan +25", await GetBalanceAsync("st_b", FinPuanId), 25m);
            Assert("streak_log completion_no=1", await ScalarIntAsync(
                $"SELECT COUNT(*) FROM streak_log WHERE tenant_id='{_tid}' AND campaign_id='{SumRuleId}' AND contact_key='st_b' AND completion_no=1"), 1);
        });

        // ── ST05: break — an active streak that missed yesterday breaks in the nightly job ───
        await RunTest("ST05 — met 3 and 2 days ago, nothing yesterday → job: count=0 + streak.broken (rerun dedup)", async () =>
        {
            await PublishTxAsync(channel, "st_c", "6011", "50.00", anchor.AddDays(-3));
            await WaitAsync();
            await PublishTxAsync(channel, "st_c", "6011", "50.00", anchor.AddDays(-2));
            await WaitAsync();
            Assert("count=2 before the break", await ProgressCountAsync(DayRuleId, "st_c"), 2);

            await RunMaintenanceJobAsync();

            Assert("count=0 after the break", await ProgressCountAsync(DayRuleId, "st_c"), 0);
            Assert("outbox streak.broken", await ScalarIntAsync(
                $"SELECT COUNT(*) FROM outbox_events WHERE tenant_id='{_tid}' AND event_type='loyalty.streak.broken' AND contact_key='st_c'"), 1);

            await RunMaintenanceJobAsync(); // rerun — dedup_key prevents a second event
            Assert("rerun: broken event still 1", await ScalarIntAsync(
                $"SELECT COUNT(*) FROM outbox_events WHERE tenant_id='{_tid}' AND event_type='loyalty.streak.broken' AND contact_key='st_c'"), 1);
        });

        // ── ST06: recompute — a late event fills the gap, the missed reward is granted ─
        await RunTest("ST06 — late event fills yesterday → recompute completes 3/3, reward +15", async () =>
        {
            await PublishTxAsync(channel, "st_d", "6011", "50.00", anchor.AddDays(-2));
            await WaitAsync();
            await PublishTxAsync(channel, "st_d", "6011", "50.00", anchor);
            await WaitAsync();
            Assert("with a gap: count=1 (today)", await ProgressCountAsync(DayRuleId, "st_d"), 1);

            // Late event: falls into yesterday's period — deferred to event-time recompute
            await PublishTxAsync(channel, "st_d", "6011", "50.00", anchor.AddDays(-1));
            await WaitAsync();
            Assert("late event: count still 1", await ProgressCountAsync(DayRuleId, "st_d"), 1);
            Assert("but yesterday's period is met", await ScalarIntAsync(
                $"SELECT COUNT(*) FROM streak_period_state WHERE tenant_id='{_tid}' AND campaign_id='{DayRuleId}' AND contact_key='st_d' AND period_start='{today.AddDays(-1):yyyy-MM-dd}' AND met"), 1);

            await RunMaintenanceJobAsync();

            Assert("recompute completed: FinPuan +15", await GetBalanceAsync("st_d", FinPuanId), 15m);
            Assert("streak_log source=streak_maintenance", await ScalarIntAsync(
                $"SELECT COUNT(*) FROM streak_log WHERE tenant_id='{_tid}' AND campaign_id='{DayRuleId}' AND contact_key='st_d' AND source_event_id LIKE 'streak_maintenance:%'"), 1);
            Assert("restart: count=0", await ProgressCountAsync(DayRuleId, "st_d"), 0);
        });

        // ── ST07: on_complete=stop ───────────────────────────────────────
        await RunTest("ST07 — stop rule: 2/2 completes, status=completed_stopped, doesn't continue", async () =>
        {
            await PublishTxAsync(channel, "st_e", "6013", "50.00", anchor.AddDays(-1));
            await WaitAsync();
            await PublishTxAsync(channel, "st_e", "6013", "50.00", anchor);
            await WaitAsync();

            Assert("completion: FinPuan +10", await GetBalanceAsync("st_e", FinPuanId), 10m);
            Assert("status=completed_stopped", await ScalarStringAsync(
                $"SELECT status FROM streak_progress WHERE tenant_id='{_tid}' AND campaign_id='{StopRuleId}' AND contact_key='st_e'"), "completed_stopped");

            await PublishTxAsync(channel, "st_e", "6013", "50.00", anchor);
            await WaitAsync();
            await RunMaintenanceJobAsync();
            Assert("no second reward after new event/job", await GetBalanceAsync("st_e", FinPuanId), 10m);
            Assert("streak_log still 1", await ScalarIntAsync(
                $"SELECT COUNT(*) FROM streak_log WHERE tenant_id='{_tid}' AND campaign_id='{StopRuleId}' AND contact_key='st_e'"), 1);
        });

        // ── ST08: week streak, week_start=sunday ──────────────────────
        await RunTest("ST08 — 2 consecutive weeks (sunday-start) → +30, period_starts fall on Sunday", async () =>
        {
            await PublishTxAsync(channel, "st_f", "6014", "50.00", anchor.AddDays(-7));
            await WaitAsync();
            Assert("week 1: count=1", await ProgressCountAsync(WeekRuleId, "st_f"), 1);

            await PublishTxAsync(channel, "st_f", "6014", "50.00", anchor);
            await WaitAsync();
            Assert("week 2 completion: FinPuan +30", await GetBalanceAsync("st_f", FinPuanId), 30m);
            Assert("period_starts fall on Sunday (DOW=0)", await ScalarIntAsync(
                $"SELECT COUNT(*) FROM streak_period_state WHERE tenant_id='{_tid}' AND campaign_id='{WeekRuleId}' AND contact_key='st_f' AND EXTRACT(DOW FROM period_start) = 0"), 2);
        });

        // ── ST09: double-reward attempt — a job rerun changes nothing ─
        await RunTest("ST09 — job rerun: streak_log/balance/outbox totals unchanged", async () =>
        {
            var logsBefore = await ScalarIntAsync($"SELECT COUNT(*) FROM streak_log WHERE tenant_id='{_tid}'");
            var completedBefore = await ScalarIntAsync(
                $"SELECT COUNT(*) FROM outbox_events WHERE tenant_id='{_tid}' AND event_type='loyalty.streak.completed'");

            await RunMaintenanceJobAsync();

            Assert("streak_log total unchanged", await ScalarIntAsync($"SELECT COUNT(*) FROM streak_log WHERE tenant_id='{_tid}'"), logsBefore);
            Assert("completed outbox unchanged", await ScalarIntAsync(
                $"SELECT COUNT(*) FROM outbox_events WHERE tenant_id='{_tid}' AND event_type='loyalty.streak.completed'"), completedBefore);
            Assert("st_a balance unchanged", await GetBalanceAsync("st_a", FinPuanId), 15m);
            Assert("st_d balance unchanged", await GetBalanceAsync("st_d", FinPuanId), 15m);
        });

        await TeardownAsync();

        AnsiConsole.WriteLine();
        AnsiConsole.Write(new Rule().RuleStyle("grey"));
        var color = _failed == 0 ? "green" : "red";
        AnsiConsole.MarkupLine($"[{color} bold]Result: {_passed} asserts passed, {_failed} tests failed[/]");
    }

    // ── Setup / Teardown ─────────────────────────────────────────────────

    static async Task SetupAsync()
    {
        foreach (var sql in new[]
        {
            $"DELETE FROM outbox_events WHERE tenant_id = '{_tid}'",
            "DELETE FROM ledger_entries WHERE tenant_id = 'fintech'",
            $"DELETE FROM customer_accounts WHERE tenant_id = '{_tid}'",
            "DELETE FROM event_inbox WHERE tenant_id = 'fintech'",
            "DELETE FROM event_log WHERE tenant_id = 'fintech'",
            $"DELETE FROM streak_applied_event WHERE tenant_id = '{_tid}'",
            $"DELETE FROM streak_period_state WHERE tenant_id = '{_tid}'",
            $"DELETE FROM streak_progress WHERE tenant_id = '{_tid}'",
            $"DELETE FROM streak_log WHERE tenant_id = '{_tid}'",
            $"DELETE FROM streak_campaigns WHERE id IN ('{DayRuleId}','{SumRuleId}','{StopRuleId}','{WeekRuleId}')"
        })
            await ExecuteSqlAsync(sql);

        // All 4 are StreakRule — streak lives entirely in streak_campaigns now (Phase B split),
        // not as a Rule.streak_config case; the campaign id plays the same role DayRuleId etc.
        // played historically (still named *RuleId for minimal diff against this file's history).
        await ExecuteSqlAsync($$$"""
            INSERT INTO streak_campaigns (id, tenant_id, program_id, name, trigger,
                target_account_type_id, conditions, config, active_from, active_to, status, created_at, updated_at)
            VALUES
            ('{{{DayRuleId}}}', '{{{_tid}}}', '{{{ProgramId}}}',
             'ST Day Streak', 'card.transaction', '{{{FinPuanId}}}',
             '[{"field": "mcc", "op": "eq", "value": "6011"}]',
             '{"period": "day", "target_periods": 3, "aggregate": {"metric": "count", "threshold": 1}, "timezone": "UTC", "on_complete": "restart", "reward": {"kind": "fixed_bonus", "amount": 15}}',
             NULL, NULL, 'active', now(), now()),
            ('{{{SumRuleId}}}', '{{{_tid}}}', '{{{ProgramId}}}',
             'ST Sum Streak', 'card.transaction', '{{{FinPuanId}}}',
             '[{"field": "mcc", "op": "eq", "value": "6012"}]',
             '{"period": "day", "target_periods": 2, "aggregate": {"metric": "sum", "threshold": 1000}, "timezone": "UTC", "on_complete": "restart", "reward": {"kind": "fixed_bonus", "amount": 25}}',
             NULL, NULL, 'active', now(), now()),
            ('{{{StopRuleId}}}', '{{{_tid}}}', '{{{ProgramId}}}',
             'ST Stop Streak', 'card.transaction', '{{{FinPuanId}}}',
             '[{"field": "mcc", "op": "eq", "value": "6013"}]',
             '{"period": "day", "target_periods": 2, "aggregate": {"metric": "count", "threshold": 1}, "timezone": "UTC", "on_complete": "stop", "reward": {"kind": "fixed_bonus", "amount": 10}}',
             NULL, NULL, 'active', now(), now()),
            ('{{{WeekRuleId}}}', '{{{_tid}}}', '{{{ProgramId}}}',
             'ST Week Streak', 'card.transaction', '{{{FinPuanId}}}',
             '[{"field": "mcc", "op": "eq", "value": "6014"}]',
             '{"period": "week", "week_start": "sunday", "target_periods": 2, "aggregate": {"metric": "count", "threshold": 1}, "timezone": "UTC", "on_complete": "restart", "reward": {"kind": "fixed_bonus", "amount": 30}}',
             NULL, NULL, 'active', now(), now())
            """);

        await FlushCampaignsCacheAsync();
        await FlushRedisPatternAsync("limit:fintech:*");
        AnsiConsole.MarkupLine("[grey]fintech data reset, 4 ST campaigns added, Redis cache cleared.[/]\n");
    }

    static async Task TeardownAsync()
    {
        await ExecuteSqlAsync(
            $"DELETE FROM streak_campaigns WHERE id IN ('{DayRuleId}','{SumRuleId}','{StopRuleId}','{WeekRuleId}')");
        await FlushCampaignsCacheAsync();
        AnsiConsole.MarkupLine("\n[grey]Teardown: ST campaigns deleted, campaign cache cleared.[/]");
    }

    // ── Publish ──────────────────────────────────────────────────────────

    static Task PublishTxAsync(IModel ch, string contact, string mcc, string amount,
        DateTime occurredAt, string? eventId = null)
    {
        var envelope = new Dictionary<string, object?>
        {
            ["eventId"]    = eventId ?? Guid.NewGuid().ToString(),
            ["eventType"]  = "card.transaction",
            ["tenant"]     = TENANT,
            ["version"]    = "1",
            ["occurredAt"] = occurredAt,
            ["data"] = new Dictionary<string, object?>
            {
                ["contact_key"] = contact,
                ["amount"]      = amount,
                ["channel"]     = "pos",
                ["mcc"]         = mcc
            }
        };

        var body  = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(envelope));
        var props = ch.CreateBasicProperties();
        props.Persistent  = true;
        props.ContentType = "application/json";
        ch.BasicPublish("loyalty.events", "card.transaction", props, body);
        return Task.CompletedTask;
    }

    // ── Maintenance job (prod code, mini DI) ─────────────────────────────

    static async Task RunMaintenanceJobAsync()
    {
        var services = new ServiceCollection();
        services.AddLogging(b => b.AddConsole().SetMinimumLevel(LogLevel.Warning));
        services.AddDbContext<LoyaltyDbContext>(opts => opts.UseNpgsql(PG));
        services.AddSingleton<TenantSlugCache>();
        services.AddScoped<ITenantSlugResolver, TenantSlugResolver>();
        services.AddScoped<ILedgerService, LedgerService>();
        services.AddScoped<IOutboxService, OutboxService>();
        services.AddScoped<StreakCampaignModule>();
        services.AddScoped<IStreakCampaignModule>(p => p.GetRequiredService<StreakCampaignModule>());
        services.AddScoped<StreakMaintenanceJob>();
        await using var sp = services.BuildServiceProvider();
        using var scope = sp.CreateScope();
        await scope.ServiceProvider.GetRequiredService<StreakMaintenanceJob>().RunAsync();
    }

    // ── Test helpers ─────────────────────────────────────────────────────

    static async Task RunTest(string name, Func<Task> test)
    {
        try
        {
            await test();
            AnsiConsole.MarkupLine($"[green]✓[/] {name}");
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]✗[/] {name}");
            AnsiConsole.MarkupLine($"  [red]{ex.Message.Replace("[", "[[").Replace("]", "]]")}[/]");
            _failed++;
        }
    }

    static void Assert(string label, decimal actual, decimal expected)
    {
        if (actual != expected)
            throw new Exception($"{label} → expected: {expected}, actual: {actual}");
        _passed++;
    }

    static void Assert(string label, int actual, int expected)
    {
        if (actual != expected)
            throw new Exception($"{label} → expected: {expected}, actual: {actual}");
        _passed++;
    }

    static void Assert(string label, string? actual, string? expected)
    {
        if (!string.Equals(actual, expected, StringComparison.Ordinal))
            throw new Exception($"{label} → expected: {expected ?? "null"}, actual: {actual ?? "null"}");
        _passed++;
    }

    static Task WaitAsync(int ms = 1500) => Task.Delay(ms);

    // ── Redis ────────────────────────────────────────────────────────────

    static Task FlushCampaignsCacheAsync() => FlushRedisPatternAsync("campaigns:fintech:*");

    static async Task FlushRedisPatternAsync(string pattern)
    {
        var lua = $"local ks=redis.call('keys','{pattern}') for _,k in ipairs(ks) do redis.call('del',k) end return #ks";
        var psi = new ProcessStartInfo("docker")
        {
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            UseShellExecute        = false
        };
        psi.ArgumentList.Add("exec"); psi.ArgumentList.Add("loyalty-update-redis-1");
        psi.ArgumentList.Add("redis-cli"); psi.ArgumentList.Add("eval");
        psi.ArgumentList.Add(lua); psi.ArgumentList.Add("0");
        using var proc = Process.Start(psi)!;
        await Task.WhenAll(proc.StandardOutput.ReadToEndAsync(), proc.StandardError.ReadToEndAsync());
        await proc.WaitForExitAsync();
    }

    // ── DB helpers ───────────────────────────────────────────────────────

    static Task<int> ProgressCountAsync(string ruleId, string contact) => ScalarIntAsync(
        $"SELECT COALESCE((SELECT streak_count FROM streak_progress WHERE tenant_id='{_tid}' AND campaign_id='{ruleId}' AND contact_key='{contact}'), 0)");

    static Task<int> AggCountAsync(string ruleId, string contact, DateOnly period) => ScalarIntAsync(
        $"SELECT COALESCE((SELECT agg_count FROM streak_period_state WHERE tenant_id='{_tid}' AND campaign_id='{ruleId}' AND contact_key='{contact}' AND period_start='{period:yyyy-MM-dd}'), 0)");

    static async Task<decimal> GetBalanceAsync(string contactKey, string accountTypeId)
    {
        await using var db = new NpgsqlConnection(PG);
        await db.OpenAsync();
        await using var cmd = new NpgsqlCommand("""
            SELECT balance FROM customer_accounts
            WHERE tenant_id = @t::uuid AND contact_key = @c AND account_type_id = @at::uuid
            """, db);
        cmd.Parameters.AddWithValue("t", _tid);
        cmd.Parameters.AddWithValue("c", contactKey);
        cmd.Parameters.AddWithValue("at", accountTypeId);
        var result = await cmd.ExecuteScalarAsync();
        return result is DBNull or null ? 0m : (decimal)result;
    }

    static async Task ExecuteSqlAsync(string sql)
    {
        await using var db = new NpgsqlConnection(PG);
        await db.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, db);
        await cmd.ExecuteNonQueryAsync();
    }

    static async Task<int> ScalarIntAsync(string sql)
    {
        await using var db = new NpgsqlConnection(PG);
        await db.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, db);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync());
    }

    static async Task<string?> ScalarStringAsync(string sql)
    {
        await using var db = new NpgsqlConnection(PG);
        await db.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, db);
        var result = await cmd.ExecuteScalarAsync();
        return result is DBNull or null ? null : (string)result;
    }
}
