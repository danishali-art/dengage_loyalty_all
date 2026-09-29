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
/// points.transfer — end-to-end test.
///
/// Verifies the two-leg ledger (transfer_out/transfer_in), daily limit, insufficient
/// balance, self-transfer/config errors, idempotency, tier exclusion, and the expiry
/// FIFO interaction (transfer_out = consumption, transfer_in = fresh earn).
///
/// Decisions: transfer_in does not count toward tier qualifying; the sender's qualifying
/// balance is not reduced; the receiver's points age resets; same tenant + account type required.
/// </summary>
public static class TransferTest
{
    const string PG     = "Host=localhost;Database=loyalty_dev;Username=postgres;Password=postgres";
    const string TENANT = "starbucks";
    const string StarsAccountId = "018fcd02-0000-7000-8000-000000000001";

    static int _passed = 0;
    static int _failed = 0;
    static bool _expDaysAdded = false;
    static string _tid = "";

    public static async Task RunAsync(IModel channel)
    {
        _tid = await ScalarStringAsync($"SELECT id::text FROM tenants WHERE slug='{TENANT}'") ?? "";
        AnsiConsole.Write(new Rule("[yellow bold]Points Transfer Test — Starbucks[/]").RuleStyle("grey"));
        AnsiConsole.MarkupLine("[grey]A transfer block (daily_limit=500) is added to the Stars config, transfer flows are tested.[/]\n");

        await SetupAsync();

        // ── TR01: Happy path — two-leg + outbound ─────────────────────
        await RunTest("TR01 — 300★ tr_a → tr_b 100★: two-leg, balances, points.transferred outbox", async () =>
        {
            await SeedEarnAsync("tr_a", 300m);
            await SendTransferAsync(channel, Guid.NewGuid().ToString(), "tr_a", "tr_b", 100m);
            await WaitAsync();

            Assert("tr_a balance 200", await GetBalanceAsync("tr_a"), 200m);
            Assert("tr_b balance 100", await GetBalanceAsync("tr_b"), 100m);

            var outCount = await ScalarIntAsync(
                "SELECT COUNT(*) FROM ledger_entries WHERE tenant_id='starbucks' AND contact_key='tr_a' AND reason='transfer_out' AND delta=-100");
            Assert("ledger: transfer_out −100", outCount, 1);

            var inCount = await ScalarIntAsync(
                "SELECT COUNT(*) FROM ledger_entries WHERE tenant_id='starbucks' AND contact_key='tr_b' AND reason='transfer_in' AND delta=100");
            Assert("ledger: transfer_in +100", inCount, 1);

            var outboxCount = await ScalarIntAsync($"""
                SELECT COUNT(*) FROM outbox_events
                WHERE tenant_id='{_tid}' AND contact_key='tr_a'
                  AND event_type='loyalty.points.transferred'
                  AND payload->'data'->>'target_contact_key' = 'tr_b'
                  AND payload->'data'->>'points_amount' = '100.00'
                  AND payload->'data'->>'sender_balance' = '200.00'
                  AND payload->'data'->>'receiver_balance' = '100.00'
                """);
            Assert("outbox: points.transferred", outboxCount, 1);
        });

        // ── TR02: Insufficient balance → transfer_failed, business outcome ───────────
        await RunTest("TR02 — transfer 100★ with 50★ → balance unchanged, transfer_failed, inbox processed", async () =>
        {
            await SeedEarnAsync("tr_c", 50m);
            var eventId = Guid.NewGuid().ToString();
            await SendTransferAsync(channel, eventId, "tr_c", "tr_d", 100m);
            await WaitAsync();

            Assert("tr_c balance unchanged", await GetBalanceAsync("tr_c"), 50m);
            Assert("tr_d balance 0", await GetBalanceAsync("tr_d"), 0m);

            var transferCount = await ScalarIntAsync(
                "SELECT COUNT(*) FROM ledger_entries WHERE tenant_id='starbucks' AND contact_key IN ('tr_c','tr_d') AND reason IN ('transfer_out','transfer_in')");
            Assert("ledger: no transfer legs", transferCount, 0);

            var outboxCount = await ScalarIntAsync($"""
                SELECT COUNT(*) FROM outbox_events
                WHERE tenant_id='{_tid}' AND contact_key='tr_c'
                  AND event_type='loyalty.points.transfer_failed'
                  AND payload->'data'->>'reason' = 'insufficient_balance'
                """);
            Assert("outbox: transfer_failed (insufficient_balance)", outboxCount, 1);

            var inboxStatus = await ScalarStringAsync(
                $"SELECT status FROM event_inbox WHERE tenant_id='starbucks' AND event_id='{eventId}'");
            Assert("inbox processed (business outcome, not an error)", inboxStatus, "processed");
        });

        // ── TR03: Daily limit ────────────────────────────────────────────
        await RunTest("TR03 — daily_limit=500: 400 OK, +200 exceeds limit; reopens the next day", async () =>
        {
            await SeedEarnAsync("tr_e", 1000m);

            var firstEventId = Guid.NewGuid().ToString();
            await SendTransferAsync(channel, firstEventId, "tr_e", "tr_f", 400m);
            await WaitAsync();
            Assert("tr_e balance 600 (400 sent)", await GetBalanceAsync("tr_e"), 600m);

            await SendTransferAsync(channel, Guid.NewGuid().ToString(), "tr_e", "tr_f", 200m);
            await WaitAsync();
            Assert("tr_e balance still 600 (limit exceeded)", await GetBalanceAsync("tr_e"), 600m);
            Assert("tr_f balance 400", await GetBalanceAsync("tr_f"), 400m);

            var outboxCount = await ScalarIntAsync($"""
                SELECT COUNT(*) FROM outbox_events
                WHERE tenant_id='{_tid}' AND contact_key='tr_e'
                  AND event_type='loyalty.points.transfer_failed'
                  AND payload->'data'->>'reason' = 'daily_limit_exceeded'
                """);
            Assert("outbox: transfer_failed (daily_limit_exceeded)", outboxCount, 1);

            // Yesterday's transfer must not eat into today's limit: push the first transfer back to yesterday, retry
            await ExecuteSqlAsync($"""
                UPDATE ledger_entries SET created_at = NOW() - interval '25 hours'
                WHERE tenant_id='starbucks' AND idempotency_key='{firstEventId}:transfer_out'
                """);
            await SendTransferAsync(channel, Guid.NewGuid().ToString(), "tr_e", "tr_f", 200m);
            await WaitAsync();
            Assert("tr_e balance 400 (yesterday's 400 didn't count)", await GetBalanceAsync("tr_e"), 400m);
            Assert("tr_f balance 600", await GetBalanceAsync("tr_f"), 600m);
        });

        // ── TR04: Self transfer + missing config → inbox failed ───────────
        await RunTest("TR04 — self transfer and no transfer block configured → inbox failed", async () =>
        {
            await SeedEarnAsync("tr_g", 100m);

            var selfEventId = Guid.NewGuid().ToString();
            await SendTransferAsync(channel, selfEventId, "tr_g", "tr_g", 10m);
            await WaitAsync();

            var selfStatus = await ScalarStringAsync(
                $"SELECT status FROM event_inbox WHERE tenant_id='starbucks' AND event_id='{selfEventId}'");
            Assert("self transfer: inbox failed", selfStatus, "failed");
            var selfError = await ScalarStringAsync(
                $"SELECT error FROM event_inbox WHERE tenant_id='starbucks' AND event_id='{selfEventId}'");
            Assert("error self_transfer_not_allowed",
                selfError is not null && selfError.StartsWith("self_transfer_not_allowed") ? "ok" : selfError, "ok");

            await ExecuteSqlAsync(
                $"UPDATE account_types SET config = config - 'transfer' WHERE id = '{StarsAccountId}'");

            var noCfgEventId = Guid.NewGuid().ToString();
            await SendTransferAsync(channel, noCfgEventId, "tr_g", "tr_h", 10m);
            await WaitAsync();

            var noCfgStatus = await ScalarStringAsync(
                $"SELECT status FROM event_inbox WHERE tenant_id='starbucks' AND event_id='{noCfgEventId}'");
            Assert("no config: inbox failed", noCfgStatus, "failed");
            var noCfgError = await ScalarStringAsync(
                $"SELECT error FROM event_inbox WHERE tenant_id='starbucks' AND event_id='{noCfgEventId}'");
            Assert("error transfer_not_configured",
                noCfgError is not null && noCfgError.StartsWith("transfer_not_configured") ? "ok" : noCfgError, "ok");

            Assert("tr_g balance unchanged", await GetBalanceAsync("tr_g"), 100m);

            await ExecuteSqlAsync($$"""
                UPDATE account_types SET config = jsonb_set(config, '{transfer}', '{"daily_limit": 500}'::jsonb)
                WHERE id = '{{StarsAccountId}}'
                """);
        });

        // ── TR05: Idempotency — same event twice ──────────────────────────
        await RunTest("TR05 — same event_id twice → one two-leg entry, one outbound", async () =>
        {
            await SeedEarnAsync("tr_i", 300m);
            var eventId = Guid.NewGuid().ToString();
            await SendTransferAsync(channel, eventId, "tr_i", "tr_j", 100m);
            await WaitAsync();
            await SendTransferAsync(channel, eventId, "tr_i", "tr_j", 100m);
            await WaitAsync();

            Assert("tr_i −100 only once", await GetBalanceAsync("tr_i"), 200m);
            Assert("tr_j +100 only once", await GetBalanceAsync("tr_j"), 100m);

            var entryCount = await ScalarIntAsync(
                $"SELECT COUNT(*) FROM ledger_entries WHERE tenant_id='starbucks' AND source_event_id='{eventId}'");
            Assert("ledger: single two-leg entry (2 rows)", entryCount, 2);

            var outboxCount = await ScalarIntAsync(
                $"SELECT COUNT(*) FROM outbox_events WHERE tenant_id='{_tid}' AND contact_key='tr_i' AND event_type='loyalty.points.transferred'");
            Assert("outbox single points.transferred", outboxCount, 1);
        });

        // ── TR06: Tier exclusion — transfer doesn't count toward qualifying ───────────
        await RunTest("TR06 — transfer_in doesn't count toward the receiver's qualifying, transfer_out doesn't reduce the sender's", async () =>
        {
            // Tier accounting only reads earn/stamp_earn (TierEvaluationService + TierDowngradeJob).
            // Balance is unaffected: after TR01, tr_b's balance is 100 but qualifying total must be 0.
            var receiverQualifying = await ScalarDecimalAsync(
                "SELECT COALESCE(SUM(delta),0) FROM ledger_entries WHERE tenant_id='starbucks' AND contact_key='tr_b' AND reason IN ('earn','stamp_earn')");
            Assert("tr_b qualifying = 0 (even though balance is 100)", receiverQualifying, 0m);
            Assert("tr_b balance 100", await GetBalanceAsync("tr_b"), 100m);

            var senderQualifying = await ScalarDecimalAsync(
                "SELECT COALESCE(SUM(delta),0) FROM ledger_entries WHERE tenant_id='starbucks' AND contact_key='tr_a' AND reason IN ('earn','stamp_earn')");
            Assert("tr_a qualifying = 300 (transfer_out didn't reduce it)", senderQualifying, 300m);
        });

        // ── TR07: Expiry interaction ───────────────────────────────────────
        await RunTest("TR07 — transfer_out counts as consumption, transfer_in as fresh earn doesn't expire", async () =>
        {
            var expDays = await ScalarIntAsync(
                $"SELECT (config->>'expiration_days')::int FROM account_types WHERE id = '{StarsAccountId}'");

            // tr_x: 200 earned outside the expiry window + 60 transferred today → consumption 60,
            // expire = 200 − 60 = 140. tr_y: transfer_in today → not expired.
            await SeedEarnAsync("tr_x", 200m, daysAgo: expDays + 20);
            await SendTransferAsync(channel, Guid.NewGuid().ToString(), "tr_x", "tr_y", 60m);
            await WaitAsync();

            await RunJobAsync<PointsExpirationJob>(j => j.RunAsync());

            var expired = await ScalarDecimalAsync(
                "SELECT COALESCE(SUM(delta),0) FROM ledger_entries WHERE tenant_id='starbucks' AND contact_key='tr_x' AND reason='points_expired'");
            Assert("tr_x expire = −140 (200 − transfer_out 60)", expired, -140m);
            Assert("tr_x balance 0", await GetBalanceAsync("tr_x"), 0m);

            var receiverExpired = await ScalarIntAsync(
                "SELECT COUNT(*) FROM ledger_entries WHERE tenant_id='starbucks' AND contact_key='tr_y' AND reason='points_expired'");
            Assert("tr_y not expired (age reset)", receiverExpired, 0);
            Assert("tr_y balance 60", await GetBalanceAsync("tr_y"), 60m);
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
            "DELETE FROM ledger_entries WHERE tenant_id = 'starbucks'",
            $"DELETE FROM customer_accounts WHERE tenant_id = '{_tid}'",
            "DELETE FROM event_inbox WHERE tenant_id = 'starbucks'"
        })
            await ExecuteSqlAsync(sql);

        await ExecuteSqlAsync($$"""
            UPDATE account_types SET config = jsonb_set(config, '{transfer}', '{"daily_limit": 500}'::jsonb)
            WHERE id = '{{StarsAccountId}}'
            """);

        // TR07 needs expiration_days; add it temporarily if undefined (removed in teardown)
        var expDays = await ScalarStringAsync(
            $"SELECT config->>'expiration_days' FROM account_types WHERE id = '{StarsAccountId}'");
        if (expDays is null)
        {
            await ExecuteSqlAsync(
                $"UPDATE account_types SET config = jsonb_set(config, '{{expiration_days}}', '365'::jsonb) WHERE id = '{StarsAccountId}'");
            _expDaysAdded = true;
        }

        await FlushRedisLimitsAsync();
        AnsiConsole.MarkupLine("[grey]DB reset, transfer block (daily_limit=500) added to Stars config, Redis limit cache cleared.[/]\n");
    }

    // The transfer block must not stay permanent: other suites run against starbucks's
    // original seed config (same pattern as RewardTest deactivating definitions).
    static async Task TeardownAsync()
    {
        await ExecuteSqlAsync(
            $"UPDATE account_types SET config = config - 'transfer' WHERE id = '{StarsAccountId}'");
        if (_expDaysAdded)
            await ExecuteSqlAsync(
                $"UPDATE account_types SET config = config - 'expiration_days' WHERE id = '{StarsAccountId}'");
        AnsiConsole.MarkupLine("\n[grey]Teardown: transfer block removed from config.[/]");
    }

    // ── Seed helpers ─────────────────────────────────────────────────────

    static async Task SeedEarnAsync(string contact, decimal amount, int daysAgo = 0)
    {
        await using var db = new NpgsqlConnection(PG);
        await db.OpenAsync();

        await using (var acc = new NpgsqlCommand("""
            INSERT INTO customer_accounts (id, tenant_id, contact_key, account_type_id, balance, updated_at)
            VALUES (gen_random_uuid(), @t::uuid, @c, @at::uuid, 0, NOW())
            ON CONFLICT (tenant_id, contact_key, account_type_id) DO NOTHING
            """, db))
        {
            acc.Parameters.AddWithValue("t", _tid);
            acc.Parameters.AddWithValue("c", contact);
            acc.Parameters.AddWithValue("at", StarsAccountId);
            await acc.ExecuteNonQueryAsync();
        }

        var key = $"seed-earn:{contact}:{daysAgo}d:{amount}";
        await using (var cmd = new NpgsqlCommand("""
            INSERT INTO ledger_entries (id, tenant_id, customer_account_id, contact_key, delta, reason,
                                        source_event_id, idempotency_key, metadata, created_at)
            SELECT gen_random_uuid(), @slug, ca.id, @c, @delta, 'earn', @src, @idem, '{}', NOW() - (@days || ' days')::interval
            FROM customer_accounts ca
            WHERE ca.tenant_id=@t::uuid AND ca.contact_key=@c AND ca.account_type_id=@at::uuid
            """, db))
        {
            cmd.Parameters.AddWithValue("slug", TENANT);
            cmd.Parameters.AddWithValue("t", _tid);
            cmd.Parameters.AddWithValue("c", contact);
            cmd.Parameters.AddWithValue("at", StarsAccountId);
            cmd.Parameters.AddWithValue("delta", amount);
            cmd.Parameters.AddWithValue("src", key);
            cmd.Parameters.AddWithValue("idem", key);
            cmd.Parameters.AddWithValue("days", daysAgo.ToString());
            await cmd.ExecuteNonQueryAsync();
        }

        await using (var bal = new NpgsqlCommand("""
            UPDATE customer_accounts SET balance = balance + @b, updated_at = NOW()
            WHERE tenant_id=@t::uuid AND contact_key=@c AND account_type_id=@at::uuid
            """, db))
        {
            bal.Parameters.AddWithValue("b", amount);
            bal.Parameters.AddWithValue("t", _tid);
            bal.Parameters.AddWithValue("c", contact);
            bal.Parameters.AddWithValue("at", StarsAccountId);
            await bal.ExecuteNonQueryAsync();
        }
    }

    // ── Sending events ───────────────────────────────────────────────────

    static Task SendTransferAsync(IModel ch, string eventId, string from, string to, decimal amount)
    {
        PublishRaw(ch, "points.transfer", eventId, new
        {
            contact_key = from,
            target_contact_key = to,
            points_amount = amount.ToString("F2", System.Globalization.CultureInfo.InvariantCulture),
            source_account_type_id = StarsAccountId
        });
        return Task.CompletedTask;
    }

    static void PublishRaw(IModel ch, string routingKey, string eventId, object data)
    {
        var envelope = new
        {
            EventId    = eventId,
            EventType  = routingKey,
            Tenant     = TENANT,
            OccurredAt = DateTime.UtcNow,
            Version    = "1",
            Data       = data
        };
        var body  = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(envelope,
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
        var props = ch.CreateBasicProperties();
        props.Persistent  = true;
        props.ContentType = "application/json";
        ch.BasicPublish("loyalty.events", routingKey, props, body);
    }

    // ── Running jobs (prod code, mini DI) ────────────────────────────────

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
            AnsiConsole.MarkupLine($"  [red]{ex.Message}[/]");
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

    static Task WaitAsync(int ms = 1200) => Task.Delay(ms);

    static async Task FlushRedisLimitsAsync()
    {
        var lua = "local ks=redis.call('keys','limit:starbucks:*') for _,k in ipairs(ks) do redis.call('del',k) end return #ks";
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

    static async Task<decimal> GetBalanceAsync(string contactKey)
    {
        await using var db = new NpgsqlConnection(PG);
        await db.OpenAsync();
        await using var cmd = new NpgsqlCommand("""
            SELECT balance FROM customer_accounts
            WHERE tenant_id = @t::uuid AND contact_key = @c AND account_type_id = @at::uuid
            """, db);
        cmd.Parameters.AddWithValue("t", _tid);
        cmd.Parameters.AddWithValue("c", contactKey);
        cmd.Parameters.AddWithValue("at", StarsAccountId);
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

    static async Task<decimal> ScalarDecimalAsync(string sql)
    {
        await using var db = new NpgsqlConnection(PG);
        await db.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, db);
        var result = await cmd.ExecuteScalarAsync();
        return result is DBNull or null ? 0m : Convert.ToDecimal(result);
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
