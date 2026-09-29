using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Npgsql;
using RabbitMQ.Client;
using Spectre.Console;

/// <summary>
/// Phase 3 — Card Buckets end-to-end test: a pure composition of Tiqmo SOW
/// scenarios out of existing building blocks (StreakRule + DSL + limits +
/// timestamptz window). Engine code stays unchanged; rules are set up
/// temporarily against the fintech tenant.
///
/// CB01 SOW flagship scenario: 3 consecutive days of 1-500 SAR card transactions → 2 SAR.
/// CB04 day-rollover simulation: the Redis daily limit key is deleted + ledger
/// created_at is pushed back (the exact equivalent of the 25h TTL + rebuild).
///
/// Note: the card.transaction routing key must be bound in the consumer via
/// RabbitMq:GenericEventTypes (DOTNET_ENVIRONMENT=Development). Cannot run IN
/// PARALLEL with --streak-test (both clear the streak_* tables).
/// </summary>
public static class CardBucketTest
{
    const string PG        = "Host=localhost;Database=loyalty_dev;Username=postgres;Password=postgres";
    const string TENANT    = "fintech";
    const string CashId    = "019fd002-0000-7000-8000-000000000002"; // CASH (SAR)
    const string ProgramId = "019fd001-0000-7000-8000-000000000001";

    const string Cb01RuleId = "019fd003-cccc-7000-8000-000000000001"; // streak: 3 consecutive days 1-500 SAR → +2
    const string Cb02RuleId = "019fd003-cccc-7000-8000-000000000002"; // mcc in [5812,5813] + amount>=50 → +5
    const string Cb03RuleId = "019fd003-cccc-7000-8000-000000000003"; // country ne SA + profile.country eq SA → +10
    const string Cb04RuleId = "019fd003-cccc-7000-8000-000000000004"; // mcc 7011, per_customer_per_day=1 → +1
    const string Cb05RuleId = "019fd003-cccc-7000-8000-000000000005"; // mcc 7995, active_from=+10s → +7

    static int _passed = 0;
    static int _failed = 0;
    static string _tid = "";

    public static async Task RunAsync(IModel channel)
    {
        _tid = await ScalarStringAsync($"SELECT id::text FROM tenants WHERE slug='{TENANT}'") ?? "";
        AnsiConsole.Write(new Rule("[yellow bold]Card Bucket Test — fintech (tiqmo)[/]").RuleStyle("grey"));
        AnsiConsole.MarkupLine("[grey]MCC / amount range / geo / frequency cap / time window / consecutive-day compositions.[/]\n");

        // Anchor mid-day UTC — avoid date drift on runs close to a day boundary
        var anchor = DateTime.UtcNow.Date.AddHours(12);

        await SetupAsync();

        // ── CB01: SOW flagship scenario — 3 consecutive days 1–500 SAR → 2 SAR ─
        await RunTest("CB01 — 3 consecutive days of 1-500 SAR card transactions → +2 SAR; 501 SAR doesn't count toward the day", async () =>
        {
            await PublishCardAsync(channel, "cb_a", "200.00", anchor.AddDays(-2));
            await WaitAsync();
            Assert("day 1 (200 SAR): streak_count=1", await ProgressCountAsync(Cb01RuleId, "cb_a"), 1);

            await PublishCardAsync(channel, "cb_a", "501.00", anchor.AddDays(-1));
            await WaitAsync();
            Assert("501 SAR outside range: count still 1", await ProgressCountAsync(Cb01RuleId, "cb_a"), 1);
            Assert("no reward", await GetBalanceAsync("cb_a", CashId), 0m);

            await PublishCardAsync(channel, "cb_a", "450.00", anchor.AddDays(-1));
            await WaitAsync();
            Assert("day 2 (450 SAR): count=2", await ProgressCountAsync(Cb01RuleId, "cb_a"), 2);

            await PublishCardAsync(channel, "cb_a", "300.00", anchor);
            await WaitAsync();
            Assert("day 3 completes: +2 SAR", await GetBalanceAsync("cb_a", CashId), 2m);
            Assert("streak_log completion_no=1", await ScalarIntAsync(
                $"SELECT COUNT(*) FROM streak_log WHERE tenant_id='{_tid}' AND campaign_id='{Cb01RuleId}' AND contact_key='cb_a' AND completion_no=1"), 1);
            Assert("outbox streak.completed", await ScalarIntAsync(
                $"SELECT COUNT(*) FROM outbox_events WHERE tenant_id='{_tid}' AND event_type='loyalty.streak.completed' AND dedup_key='streak_completed:{Cb01RuleId}:cb_a:1'"), 1);
            Assert("restart: count=0", await ProgressCountAsync(Cb01RuleId, "cb_a"), 0);
        });

        // ── CB02: MCC bucket ─────────────────────────────────────────────
        await RunTest("CB02 — mcc in (5812, 5813) + amount>=50: 5812/120 → +5; 5999 and 5812/30 → nothing", async () =>
        {
            await PublishCardAsync(channel, "cb_b", "120.00", anchor, mcc: "5812");
            await WaitAsync();
            Assert("5812 + 120 SAR: +5", await GetBalanceAsync("cb_b", CashId), 5m);

            await PublishCardAsync(channel, "cb_b", "120.00", anchor, mcc: "5999");
            await WaitAsync();
            Assert("5999 outside bucket: still 5", await GetBalanceAsync("cb_b", CashId), 5m);

            await PublishCardAsync(channel, "cb_b", "30.00", anchor, mcc: "5812");
            await WaitAsync();
            Assert("30 SAR < 50: still 5", await GetBalanceAsync("cb_b", CashId), 5m);
        });

        // ── CB03: geo combination — SA customer spending abroad ───
        await RunTest("CB03 — country ne SA + profile.country eq SA: transaction in AE → +10; in SA → nothing", async () =>
        {
            await PublishCardAsync(channel, "cb_c", "100.00", anchor, country: "AE",
                profile: new Dictionary<string, object?> { ["country"] = "SA", ["kyc_status"] = "verified" });
            await WaitAsync();
            Assert("AE transaction + SA profile: +10", await GetBalanceAsync("cb_c", CashId), 10m);

            await PublishCardAsync(channel, "cb_c", "100.00", anchor, country: "SA",
                profile: new Dictionary<string, object?> { ["country"] = "SA", ["kyc_status"] = "verified" });
            await WaitAsync();
            Assert("transaction in SA (domestic): still 10", await GetBalanceAsync("cb_c", CashId), 10m);

            await PublishCardAsync(channel, "cb_c", "100.00", anchor, country: "AE",
                profile: new Dictionary<string, object?> { ["country"] = "AE", ["kyc_status"] = "verified" });
            await WaitAsync();
            Assert("AE transaction + AE profile: still 10", await GetBalanceAsync("cb_c", CashId), 10m);
        });

        // ── CB04: frequency cap — once per day ────────────────────────────
        await RunTest("CB04 — per_customer_per_day=1: 2nd transaction same day gets no reward, resets the next day", async () =>
        {
            await PublishCardAsync(channel, "cb_d", "60.00", DateTime.UtcNow, mcc: "7011");
            await WaitAsync();
            Assert("1st transaction: +1", await GetBalanceAsync("cb_d", CashId), 1m);

            await PublishCardAsync(channel, "cb_d", "60.00", DateTime.UtcNow, mcc: "7011");
            await WaitAsync();
            Assert("2nd transaction same day: capped — still 1", await GetBalanceAsync("cb_d", CashId), 1m);

            // Day-rollover simulation: the daily Redis key expires (25h TTL),
            // yesterday's earn rows fall outside the rebuild's "today" window.
            await FlushRedisPatternAsync($"limit:fintech:{Cb04RuleId}:*");
            await ExecuteSqlAsync($"""
                UPDATE ledger_entries SET created_at = created_at - interval '1 day'
                WHERE tenant_id='{TENANT}' AND contact_key='cb_d' AND rule_id='{Cb04RuleId}'
                """);

            await PublishCardAsync(channel, "cb_d", "60.00", DateTime.UtcNow, mcc: "7011");
            await WaitAsync();
            Assert("next day: rewards again +1 → 2", await GetBalanceAsync("cb_d", CashId), 2m);
            Assert("ledger: 2 earn rows", await ScalarIntAsync(
                $"SELECT COUNT(*) FROM ledger_entries WHERE tenant_id='{TENANT}' AND contact_key='cb_d' AND rule_id='{Cb04RuleId}' AND reason='earn'"), 2);
        });

        // ── CB05: time window — timestamptz check at evaluation time ──────
        await RunTest("CB05 — active_from=+10s: no reward outside the window, +7 once it opens (no cache reload)", async () =>
        {
            await ExecuteSqlAsync($$"""
                INSERT INTO rules (id, tenant_id, program_id, name, type, trigger,
                    conditions, calculation, target_account_type_id,
                    limits, priority, stackable, active_from, active_to, status, created_at, updated_at)
                VALUES ('{{Cb05RuleId}}', '{{_tid}}', '{{ProgramId}}',
                    'CB Timed Card Campaign', 'FixedBonusRule', 'card.transaction',
                    '[{"field": "mcc", "op": "eq", "value": "7995"}]',
                    '{"type": "fixed", "amount": 7}', '{{CashId}}',
                    NULL, 110, false, now() + interval '10 seconds', NULL, 'active', now(), now())
                """);
            await FlushRulesCacheAsync();

            await PublishCardAsync(channel, "cb_e", "80.00", DateTime.UtcNow, mcc: "7995");
            await WaitAsync(2000);
            Assert("no reward before the window opens", await GetBalanceAsync("cb_e", CashId), 0m);

            await Task.Delay(9000); // let active_from pass — the filter is evaluated at eval time

            await PublishCardAsync(channel, "cb_e", "80.00", DateTime.UtcNow, mcc: "7995");
            await WaitAsync(2000);
            Assert("window opened: +7", await GetBalanceAsync("cb_e", CashId), 7m);
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
            $"DELETE FROM streak_campaigns WHERE id = '{Cb01RuleId}'",
            // the rules soft-delete trigger is two-phase: the 1st DELETE sets status='deleted',
            // the 2nd DELETE actually removes it — also cleans up any active rule left from a crash
            $"DELETE FROM rules WHERE id IN ('{Cb02RuleId}','{Cb03RuleId}','{Cb04RuleId}','{Cb05RuleId}')",
            $"DELETE FROM rules WHERE id IN ('{Cb02RuleId}','{Cb03RuleId}','{Cb04RuleId}','{Cb05RuleId}')"
        })
            await ExecuteSqlAsync(sql);

        // CB01 is StreakRule — streak lives in streak_campaigns now (Phase B split), not as a
        // Rule.streak_config case.
        await ExecuteSqlAsync($$$"""
            INSERT INTO streak_campaigns (id, tenant_id, program_id, name, trigger,
                target_account_type_id, conditions, config, active_from, active_to, status, created_at, updated_at)
            VALUES
            ('{{{Cb01RuleId}}}', '{{{_tid}}}', '{{{ProgramId}}}',
             'CB Consecutive-Day Card Streak', 'card.transaction', '{{{CashId}}}',
             '[{"field": "amount", "op": "gte", "value": 1}, {"field": "amount", "op": "lte", "value": 500}]',
             '{"period": "day", "target_periods": 3, "aggregate": {"metric": "count", "threshold": 1}, "timezone": "UTC", "on_complete": "restart", "reward": {"kind": "fixed_bonus", "amount": 2}}',
             NULL, NULL, 'active', now(), now())
            """);

        await ExecuteSqlAsync($$$"""
            INSERT INTO rules (id, tenant_id, program_id, name, type, trigger,
                conditions, calculation, target_account_type_id,
                limits, priority, stackable, active_from, active_to, status, created_at, updated_at)
            VALUES
            ('{{{Cb02RuleId}}}', '{{{_tid}}}', '{{{ProgramId}}}',
             'CB Restaurant MCC Bucket', 'FixedBonusRule', 'card.transaction',
             '[{"field": "mcc", "op": "in", "value": ["5812", "5813"]}, {"field": "amount", "op": "gte", "value": 50}]',
             '{"type": "fixed", "amount": 5}', '{{{CashId}}}',
             NULL, 100, false, NULL, NULL, 'active', now(), now()),
            ('{{{Cb03RuleId}}}', '{{{_tid}}}', '{{{ProgramId}}}',
             'CB Spending Abroad (SA Customer)', 'FixedBonusRule', 'card.transaction',
             '[{"field": "country", "op": "ne", "value": "SA"}, {"field": "profile.country", "op": "eq", "value": "SA"}]',
             '{"type": "fixed", "amount": 10}', '{{{CashId}}}',
             NULL, 100, false, NULL, NULL, 'active', now(), now()),
            ('{{{Cb04RuleId}}}', '{{{_tid}}}', '{{{ProgramId}}}',
             'CB Once-Daily Hotel Bonus', 'FixedBonusRule', 'card.transaction',
             '[{"field": "mcc", "op": "eq", "value": "7011"}]',
             '{"type": "fixed", "amount": 1}', '{{{CashId}}}',
             '{"per_customer_per_day": 1}', 100, false, NULL, NULL, 'active', now(), now())
            """);

        await FlushRulesCacheAsync();
        await FlushCampaignsCacheAsync();
        await FlushRedisPatternAsync("limit:fintech:*");
        AnsiConsole.MarkupLine("[grey]fintech data reset, 1 CB campaign + 3 CB rules added (CB05's rule is added inside its test), Redis cache cleared.[/]\n");
    }

    static async Task TeardownAsync()
    {
        await ExecuteSqlAsync(
            $"DELETE FROM rules WHERE id IN ('{Cb02RuleId}','{Cb03RuleId}','{Cb04RuleId}','{Cb05RuleId}')");
        await ExecuteSqlAsync($"DELETE FROM streak_campaigns WHERE id = '{Cb01RuleId}'");
        await FlushRulesCacheAsync();
        await FlushCampaignsCacheAsync();
        AnsiConsole.MarkupLine("\n[grey]Teardown: CB rules/campaign deleted, cache cleared.[/]");
    }

    // ── Publish ──────────────────────────────────────────────────────────

    static Task PublishCardAsync(IModel ch, string contact, string amount, DateTime occurredAt,
        string? mcc = null, string? country = null, Dictionary<string, object?>? profile = null,
        string? eventId = null)
    {
        var data = new Dictionary<string, object?>
        {
            ["contact_key"] = contact,
            ["amount"]      = amount,
            ["channel"]     = "card",
            ["currency"]    = "SAR"
        };
        if (mcc is not null)     data["mcc"] = mcc;
        if (country is not null) data["country"] = country;
        if (profile is not null) data["profile"] = profile;

        var envelope = new Dictionary<string, object?>
        {
            ["eventId"]    = eventId ?? Guid.NewGuid().ToString(),
            ["eventType"]  = "card.transaction",
            ["tenant"]     = TENANT,
            ["version"]    = "1",
            ["occurredAt"] = occurredAt,
            ["data"]       = data
        };

        var body  = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(envelope));
        var props = ch.CreateBasicProperties();
        props.Persistent  = true;
        props.ContentType = "application/json";
        ch.BasicPublish("loyalty.events", "card.transaction", props, body);
        return Task.CompletedTask;
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

    static Task WaitAsync(int ms = 1500) => Task.Delay(ms);

    // ── Redis ────────────────────────────────────────────────────────────

    static Task FlushRulesCacheAsync() => FlushRedisPatternAsync("rules:fintech:*");
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
