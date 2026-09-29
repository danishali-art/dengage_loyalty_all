using System.Diagnostics;
using System.Text;
using System.Text.Json;
using dEngage.Loyalty.Ledger;
using dEngage.Loyalty.Schema;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;
using RabbitMQ.Client;
using Spectre.Console;

/// <summary>
/// Phase 1 — generic event + condition DSL v1 + occurred_within, end-to-end test.
///
/// Temporary GT rules are set up against the fintech tenant: signup→KYC window,
/// top-up threshold, free-form (mcc) filter, second-precision active_from, and the
/// data.profile.* profile-card convention (GT10-11: nested field + safe fallback).
/// Validation (inbox failed), idempotency, event_log writes, and 12-month retention
/// are verified. Rules are removed in teardown — the fintech seed is unchanged.
///
/// Note: the signup / kyc.completed / card.transaction routing keys must be bound
/// in the consumer via RabbitMq:GenericEventTypes (appsettings.Development).
/// </summary>
public static class GenericEventTest
{
    const string PG      = "Host=localhost;Database=loyalty_dev;Username=postgres;Password=postgres";
    const string TENANT  = "fintech";
    const string FinPuanId = "019fd002-0000-7000-8000-000000000001"; // POINTS
    const string CashId    = "019fd002-0000-7000-8000-000000000002"; // CASH
    const string ProgramId = "019fd001-0000-7000-8000-000000000001";

    const string KycRuleId   = "019fd003-aaaa-7000-8000-000000000001"; // occurred_within(signup, 1h) → +200
    const string TopupRuleId = "019fd003-aaaa-7000-8000-000000000002"; // amount gte 1000 → +50
    const string MccRuleId   = "019fd003-aaaa-7000-8000-000000000003"; // mcc eq 5812 → +10
    const string WindowRuleId= "019fd003-aaaa-7000-8000-000000000004"; // mcc eq 7777, active_from=+10s → +99
    const string ProfileRuleId = "019fd003-aaaa-7000-8000-000000000005"; // profile.kyc_status eq none → +25
    const string GeoRuleId     = "019fd003-aaaa-7000-8000-000000000006"; // profile.country eq SA → +30

    static int _passed = 0;
    static int _failed = 0;
    static string _tid = "";

    public static async Task RunAsync(IModel channel)
    {
        _tid = await ScalarStringAsync($"SELECT id::text FROM tenants WHERE slug='{TENANT}'") ?? "";
        AnsiConsole.Write(new Rule("[yellow bold]Generic Event Test — fintech[/]").RuleStyle("grey"));
        AnsiConsole.MarkupLine("[grey]Generic event contract + DSL v1 + occurred_within + event_log/retention.[/]\n");

        await SetupAsync();

        // ── GT01: signup → 1 hour window → kyc.completed → bonus ───────────
        var kycEventId = Guid.NewGuid().ToString();
        await RunTest("GT01 — KYC 30 min after signup → occurred_within(1h) bonus +200", async () =>
        {
            await PublishAsync(channel, "signup", Guid.NewGuid().ToString(), new Dictionary<string, object?>
            {
                ["contact_key"] = "gt_a", ["amount"] = "0", ["channel"] = "app"
            }, occurredAt: DateTime.UtcNow.AddMinutes(-30));
            await WaitAsync();

            await PublishAsync(channel, "kyc.completed", kycEventId, new Dictionary<string, object?>
            {
                ["contact_key"] = "gt_a", ["amount"] = "0", ["channel"] = "app"
            });
            await WaitAsync();

            Assert("gt_a FinPuan 200", await GetBalanceAsync("gt_a", FinPuanId), 200m);
            var earn = await ScalarIntAsync(
                $"SELECT COUNT(*) FROM ledger_entries WHERE tenant_id='fintech' AND contact_key='gt_a' AND reason='earn' AND idempotency_key='{kycEventId}:{KycRuleId}'");
            Assert("ledger: {event}:{rule} bonus row", earn, 1);
        });

        // ── GT02: outside the window / no preceding event → no bonus ─────────────
        await RunTest("GT02 — signup 2 hours ago → outside the window; KYC without signup → no bonus", async () =>
        {
            await PublishAsync(channel, "signup", Guid.NewGuid().ToString(), new Dictionary<string, object?>
            {
                ["contact_key"] = "gt_b", ["amount"] = "0", ["channel"] = "app"
            }, occurredAt: DateTime.UtcNow.AddHours(-2));
            await WaitAsync();

            var lateKyc = Guid.NewGuid().ToString();
            await PublishAsync(channel, "kyc.completed", lateKyc, new Dictionary<string, object?>
            {
                ["contact_key"] = "gt_b", ["amount"] = "0", ["channel"] = "app"
            });

            var orphanKyc = Guid.NewGuid().ToString();
            await PublishAsync(channel, "kyc.completed", orphanKyc, new Dictionary<string, object?>
            {
                ["contact_key"] = "gt_c", ["amount"] = "0", ["channel"] = "app"
            });
            await WaitAsync();

            Assert("gt_b FinPuan 0 (2h > 1h)", await GetBalanceAsync("gt_b", FinPuanId), 0m);
            Assert("gt_c FinPuan 0 (no signup)", await GetBalanceAsync("gt_c", FinPuanId), 0m);
            Assert("gt_b KYC inbox processed (business outcome)",
                await InboxStatusAsync(lateKyc), "processed");
            Assert("gt_c KYC inbox processed",
                await InboxStatusAsync(orphanKyc), "processed");
        });

        // ── GT03: contract violation → inbox failed ──────────────────────
        await RunTest("GT03 — missing channel / missing occurred_at → inbox failed + invalid_generic_event", async () =>
        {
            var noChannel = Guid.NewGuid().ToString();
            await PublishAsync(channel, "kyc.completed", noChannel, new Dictionary<string, object?>
            {
                ["contact_key"] = "gt_v1", ["amount"] = "0"
            });

            var noOccurred = Guid.NewGuid().ToString();
            await PublishAsync(channel, "kyc.completed", noOccurred, new Dictionary<string, object?>
            {
                ["contact_key"] = "gt_v2", ["amount"] = "0", ["channel"] = "app"
            }, omitOccurredAt: true);
            await WaitAsync();

            Assert("missing channel: inbox failed", await InboxStatusAsync(noChannel), "failed");
            var err1 = await InboxErrorAsync(noChannel);
            Assert("error: data.channel",
                err1 is not null && err1.StartsWith("invalid_generic_event: data.channel") ? "ok" : err1, "ok");

            Assert("missing occurred_at: inbox failed", await InboxStatusAsync(noOccurred), "failed");
            var err2 = await InboxErrorAsync(noOccurred);
            Assert("error: occurred_at",
                err2 is not null && err2.StartsWith("invalid_generic_event: occurred_at") ? "ok" : err2, "ok");

            var logRows = await ScalarIntAsync(
                "SELECT COUNT(*) FROM event_log WHERE tenant_id='fintech' AND contact_key IN ('gt_v1','gt_v2')");
            Assert("invalid events were not written to event_log", logRows, 0);
        });

        // ── GT04: built-in cash.added + campaign threshold ───────────────
        await RunTest("GT04 — cash.added 1500 → cash_load + threshold bonus; 500 → cash_load only", async () =>
        {
            var bigTopup = Guid.NewGuid().ToString();
            await PublishAsync(channel, "cash.added", bigTopup, new Dictionary<string, object?>
            {
                ["contact_key"] = "gt_d", ["amount"] = "1500.00",
                ["account_type_id"] = CashId, ["channel"] = "app"
            });
            await WaitAsync();

            Assert("gt_d Cash 1500 (cash_load)", await GetBalanceAsync("gt_d", CashId), 1500m);
            Assert("gt_d FinPuan 50 (threshold bonus)", await GetBalanceAsync("gt_d", FinPuanId), 50m);
            var bonus = await ScalarIntAsync(
                $"SELECT COUNT(*) FROM ledger_entries WHERE tenant_id='fintech' AND contact_key='gt_d' AND idempotency_key='{bigTopup}:{TopupRuleId}'");
            Assert("ledger: both cash_load and bonus", bonus, 1);

            await PublishAsync(channel, "cash.added", Guid.NewGuid().ToString(), new Dictionary<string, object?>
            {
                ["contact_key"] = "gt_d", ["amount"] = "500.00",
                ["account_type_id"] = CashId, ["channel"] = "app"
            });
            await WaitAsync();

            Assert("gt_d Cash 2000", await GetBalanceAsync("gt_d", CashId), 2000m);
            Assert("gt_d FinPuan still 50 (500 < 1000)", await GetBalanceAsync("gt_d", FinPuanId), 50m);
        });

        // ── GT05: duplicate publish → single grant ──────────────────────────
        await RunTest("GT05 — GT01's KYC event published a 2nd time → single grant, single inbox row", async () =>
        {
            await PublishAsync(channel, "kyc.completed", kycEventId, new Dictionary<string, object?>
            {
                ["contact_key"] = "gt_a", ["amount"] = "0", ["channel"] = "app"
            });
            await WaitAsync();

            Assert("gt_a FinPuan still 200", await GetBalanceAsync("gt_a", FinPuanId), 200m);
            var earnCount = await ScalarIntAsync(
                $"SELECT COUNT(*) FROM ledger_entries WHERE tenant_id='fintech' AND idempotency_key='{kycEventId}:{KycRuleId}'");
            Assert("single bonus row", earnCount, 1);
            var inboxCount = await ScalarIntAsync(
                $"SELECT COUNT(*) FROM event_inbox WHERE tenant_id='fintech' AND event_id='{kycEventId}'");
            Assert("single inbox row (processed dedup)", inboxCount, 1);
        });

        // ── GT06: free-form field filter (mcc) ────────────────────────────
        await RunTest("GT06 — card.transaction mcc=5812 → +10; mcc=5999 → nothing (amount as JSON number)", async () =>
        {
            await PublishAsync(channel, "card.transaction", Guid.NewGuid().ToString(), new Dictionary<string, object?>
            {
                ["contact_key"] = "gt_e", ["amount"] = 120.5, ["channel"] = "pos", ["mcc"] = "5812"
            });
            await PublishAsync(channel, "card.transaction", Guid.NewGuid().ToString(), new Dictionary<string, object?>
            {
                ["contact_key"] = "gt_e", ["amount"] = "80.00", ["channel"] = "pos", ["mcc"] = "5999"
            });
            await WaitAsync();

            Assert("gt_e FinPuan 10 (5812 only)", await GetBalanceAsync("gt_e", FinPuanId), 10m);
        });

        // ── GT07: event_log rows ────────────────────────────────────
        await RunTest("GT07 — event_log: gt_a 2 rows (duplicate is a no-op), gt_b signup 2 hours in the past", async () =>
        {
            var gtaRows = await ScalarIntAsync(
                "SELECT COUNT(*) FROM event_log WHERE tenant_id='fintech' AND contact_key='gt_a'");
            Assert("gt_a: signup + kyc = 2 rows", gtaRows, 2);

            var pastSignup = await ScalarIntAsync("""
                SELECT COUNT(*) FROM event_log
                WHERE tenant_id='fintech' AND contact_key='gt_b' AND event_type='signup'
                  AND occurred_at < now() - interval '110 minutes'
                """);
            Assert("gt_b signup occurred_at ≈ 2 hours ago", pastSignup, 1);

            var cashRows = await ScalarIntAsync(
                "SELECT COUNT(*) FROM event_log WHERE tenant_id='fintech' AND contact_key='gt_d' AND event_type='cash.added'");
            Assert("built-in cash.added was logged too", cashRows, 2);
        });

        // ── GT08: retention deletes 12 months+ ─────────────────────────────
        await RunTest("GT08 — retention job: a 13-month-old row is deleted, fresh ones remain", async () =>
        {
            await ExecuteSqlAsync("""
                INSERT INTO event_log (tenant_id, event_id, contact_key, event_type, occurred_at)
                VALUES ('fintech', 'gt-old-evt-1', 'gt_old', 'signup', now() - interval '13 months')
                ON CONFLICT (tenant_id, event_id) DO NOTHING
                """);

            await RunJobAsync<EventLogRetentionJob>(j => j.RunAsync());

            var oldRows = await ScalarIntAsync(
                "SELECT COUNT(*) FROM event_log WHERE tenant_id='fintech' AND event_id='gt-old-evt-1'");
            Assert("13-month-old row deleted", oldRows, 0);
            var freshRows = await ScalarIntAsync(
                "SELECT COUNT(*) FROM event_log WHERE tenant_id='fintech' AND contact_key='gt_a'");
            Assert("fresh rows remain", freshRows, 2);
        });

        // ── GT09: second-precision active_from — checked at evaluation time ────────────
        await RunTest("GT09 — active_from=+10s: no bonus before, +99 once the window opens (no cache reload)", async () =>
        {
            await ExecuteSqlAsync($$"""
                INSERT INTO rules (id, tenant_id, program_id, name, type, trigger,
                    conditions, calculation, target_account_type_id,
                    limits, priority, stackable, active_from, active_to, status, created_at, updated_at)
                VALUES ('{{WindowRuleId}}', '{{_tid}}', '{{ProgramId}}',
                    'GT Timed Window +99', 'FixedBonusRule', 'card.transaction',
                    '[{"field": "mcc", "op": "eq", "value": "7777"}]',
                    '{"type": "fixed", "amount": 99}', '{{FinPuanId}}',
                    NULL, 110, false, now() + interval '10 seconds', NULL, 'active', now(), now())
                """);
            await FlushRulesCacheAsync();

            await PublishAsync(channel, "card.transaction", Guid.NewGuid().ToString(), new Dictionary<string, object?>
            {
                ["contact_key"] = "gt_f", ["amount"] = "60.00", ["channel"] = "pos", ["mcc"] = "7777"
            });
            await WaitAsync(2000);
            Assert("no bonus before the window opens", await GetBalanceAsync("gt_f", FinPuanId), 0m);

            await Task.Delay(9000); // let active_from pass — cache unchanged, the filter is checked at eval time

            await PublishAsync(channel, "card.transaction", Guid.NewGuid().ToString(), new Dictionary<string, object?>
            {
                ["contact_key"] = "gt_f", ["amount"] = "60.00", ["channel"] = "pos", ["mcc"] = "7777"
            });
            await WaitAsync(2000);
            Assert("window opened: +99", await GetBalanceAsync("gt_f", FinPuanId), 99m);
        });

        // ── GT10: profile card (data.profile.*) ──────────────────────────
        await RunTest("GT10 — profile.kyc_status=none → +25; verified → nothing; no profile card → nothing", async () =>
        {
            await PublishAsync(channel, "card.transaction", Guid.NewGuid().ToString(), new Dictionary<string, object?>
            {
                ["contact_key"] = "gt_g", ["amount"] = "40.00", ["channel"] = "card",
                ["profile"] = new Dictionary<string, object?> { ["kyc_status"] = "none", ["segment"] = "standard" }
            });
            await WaitAsync();
            Assert("kyc_status=none → +25", await GetBalanceAsync("gt_g", FinPuanId), 25m);

            await PublishAsync(channel, "card.transaction", Guid.NewGuid().ToString(), new Dictionary<string, object?>
            {
                ["contact_key"] = "gt_g", ["amount"] = "40.00", ["channel"] = "card",
                ["profile"] = new Dictionary<string, object?> { ["kyc_status"] = "verified", ["segment"] = "standard" }
            });
            await WaitAsync();
            Assert("kyc_status=verified → no bonus", await GetBalanceAsync("gt_g", FinPuanId), 25m);

            await PublishAsync(channel, "card.transaction", Guid.NewGuid().ToString(), new Dictionary<string, object?>
            {
                ["contact_key"] = "gt_g", ["amount"] = "40.00", ["channel"] = "card"
            });
            await WaitAsync();
            Assert("no profile card → safe fallback, no bonus", await GetBalanceAsync("gt_g", FinPuanId), 25m);
        });

        // ── GT11: profile geo (profile.country) ───────────────────────────
        await RunTest("GT11 — profile.country=SA → +30; AE → nothing", async () =>
        {
            await PublishAsync(channel, "card.transaction", Guid.NewGuid().ToString(), new Dictionary<string, object?>
            {
                ["contact_key"] = "gt_h", ["amount"] = "75.00", ["channel"] = "card",
                ["profile"] = new Dictionary<string, object?> { ["country"] = "SA", ["language"] = "ar" }
            });
            await WaitAsync();
            Assert("profile.country=SA → +30", await GetBalanceAsync("gt_h", FinPuanId), 30m);

            await PublishAsync(channel, "card.transaction", Guid.NewGuid().ToString(), new Dictionary<string, object?>
            {
                ["contact_key"] = "gt_h", ["amount"] = "75.00", ["channel"] = "card",
                ["profile"] = new Dictionary<string, object?> { ["country"] = "AE", ["language"] = "en" }
            });
            await WaitAsync();
            Assert("profile.country=AE → no bonus", await GetBalanceAsync("gt_h", FinPuanId), 30m);
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
            $"DELETE FROM rules WHERE id IN ('{KycRuleId}','{TopupRuleId}','{MccRuleId}','{WindowRuleId}','{ProfileRuleId}','{GeoRuleId}')"
        })
            await ExecuteSqlAsync(sql);

        await ExecuteSqlAsync($$$"""
            INSERT INTO rules (id, tenant_id, program_id, name, type, trigger,
                conditions, calculation, target_account_type_id,
                limits, priority, stackable, active_from, active_to, status, created_at, updated_at)
            VALUES
            ('{{{KycRuleId}}}', '{{{_tid}}}', '{{{ProgramId}}}',
             'GT KYC Speed Bonus', 'FixedBonusRule', 'kyc.completed',
             '[{"field": "event", "op": "occurred_within", "value": {"after_event": "signup", "hours": 1}}]',
             '{"type": "fixed", "amount": 200}', '{{{FinPuanId}}}',
             NULL, 100, false, NULL, NULL, 'active', now(), now()),
            ('{{{TopupRuleId}}}', '{{{_tid}}}', '{{{ProgramId}}}',
             'GT Top-up Threshold', 'FixedBonusRule', 'cash.added',
             '[{"field": "amount", "op": "gte", "value": 1000}]',
             '{"type": "fixed", "amount": 50}', '{{{FinPuanId}}}',
             NULL, 100, false, NULL, NULL, 'active', now(), now()),
            ('{{{MccRuleId}}}', '{{{_tid}}}', '{{{ProgramId}}}',
             'GT Restaurant MCC', 'FixedBonusRule', 'card.transaction',
             '[{"field": "mcc", "op": "eq", "value": "5812"}]',
             '{"type": "fixed", "amount": 10}', '{{{FinPuanId}}}',
             NULL, 100, false, NULL, NULL, 'active', now(), now()),
            ('{{{ProfileRuleId}}}', '{{{_tid}}}', '{{{ProgramId}}}',
             'GT Profile KYC Incentive', 'FixedBonusRule', 'card.transaction',
             '[{"field": "profile.kyc_status", "op": "eq", "value": "none"}]',
             '{"type": "fixed", "amount": 25}', '{{{FinPuanId}}}',
             NULL, 100, false, NULL, NULL, 'active', now(), now()),
            ('{{{GeoRuleId}}}', '{{{_tid}}}', '{{{ProgramId}}}',
             'GT Profile Geo SA', 'FixedBonusRule', 'card.transaction',
             '[{"field": "profile.country", "op": "eq", "value": "SA"}]',
             '{"type": "fixed", "amount": 30}', '{{{FinPuanId}}}',
             NULL, 100, false, NULL, NULL, 'active', now(), now())
            """);

        await FlushRulesCacheAsync();
        await FlushRedisPatternAsync("limit:fintech:*");
        AnsiConsole.MarkupLine("[grey]fintech data reset, 5 GT rules added, Redis cache cleared.[/]\n");
    }

    static async Task TeardownAsync()
    {
        await ExecuteSqlAsync(
            $"DELETE FROM rules WHERE id IN ('{KycRuleId}','{TopupRuleId}','{MccRuleId}','{WindowRuleId}','{ProfileRuleId}','{GeoRuleId}')");
        await FlushRulesCacheAsync();
        AnsiConsole.MarkupLine("\n[grey]Teardown: GT rules deleted, rule cache cleared.[/]");
    }

    // ── Publish ──────────────────────────────────────────────────────────

    static Task PublishAsync(IModel ch, string eventType, string eventId,
        Dictionary<string, object?> data, DateTime? occurredAt = null, bool omitOccurredAt = false)
    {
        var envelope = new Dictionary<string, object?>
        {
            ["eventId"]   = eventId,
            ["eventType"] = eventType,
            ["tenant"]    = TENANT,
            ["version"]   = "1",
            ["data"]      = data
        };
        if (!omitOccurredAt)
            envelope["occurredAt"] = occurredAt ?? DateTime.UtcNow;

        var body  = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(envelope));
        var props = ch.CreateBasicProperties();
        props.Persistent  = true;
        props.ContentType = "application/json";
        ch.BasicPublish("loyalty.events", eventType, props, body);
        return Task.CompletedTask;
    }

    // ── Job (prod code, mini DI) ─────────────────────────────────────────

    static async Task RunJobAsync<TJob>(Func<TJob, Task> run) where TJob : class
    {
        var services = new ServiceCollection();
        services.AddLogging(b => b.AddConsole().SetMinimumLevel(LogLevel.Warning));
        services.AddDbContext<LoyaltyDbContext>(opts => opts.UseNpgsql(PG));
        services.AddScoped<TJob>();
        await using var sp = services.BuildServiceProvider();
        using var scope = sp.CreateScope();
        await run(scope.ServiceProvider.GetRequiredService<TJob>());
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

    static Task FlushRulesCacheAsync() => FlushRedisPatternAsync("rules:fintech:*");

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

    static Task<string?> InboxStatusAsync(string eventId) => ScalarStringAsync(
        $"SELECT status FROM event_inbox WHERE tenant_id='fintech' AND event_id='{eventId}'");

    static Task<string?> InboxErrorAsync(string eventId) => ScalarStringAsync(
        $"SELECT error FROM event_inbox WHERE tenant_id='fintech' AND event_id='{eventId}'");

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
