using System.Diagnostics;
using System.Text;
using System.Text.Json;
using dEngage.Loyalty.Ledger;
using dEngage.Loyalty.RuleEngine;
using dEngage.Loyalty.Schema;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;
using RabbitMQ.Client;
using Spectre.Console;

/// <summary>
/// Outbound event producers — end-to-end test.
///
/// points.earned (RuleEngine tx), points.reversed (RefundService),
/// tier.changed up (TierEvaluationService) / down (TierDowngradeJob),
/// points.expired (PointsExpirationJob), points.expiring (PointsExpiringDetectorJob)
/// and the publisher flow are verified.
///
/// Expected counts depend on the seed rules:
///   Kış 3x (winner, p100) + İlk Alışveriş +50 (one-time). (The coffee stamp rule was removed
///   with stamps, CR 2026-10-05.)
///   Tier: yesil(0) → altin(1000) → siyah(5000), via Stars.
/// </summary>
public static class OutboundTest
{
    const string PG     = "Host=localhost;Database=loyalty_dev;Username=postgres;Password=postgres";
    const string TENANT = "starbucks";
    const string OutboundExchange = "loyalty.outbound";
    const string TestQueue = "q.test.outbound";

    static int _passed = 0;
    static int _failed = 0;

    public static async Task RunAsync(IModel channel)
    {
        AnsiConsole.Write(new Rule("[yellow bold]Outbound Events Test — Starbucks[/]").RuleStyle("grey"));
        AnsiConsole.MarkupLine("[grey]points.earned / reversed / expired + tier.changed up/down + publisher.[/]\n");

        // The listener queue must be bound BEFORE the publishes (a topic exchange does not retain)
        channel.ExchangeDeclare(OutboundExchange, ExchangeType.Topic, durable: true);
        channel.QueueDeclare(TestQueue, durable: true, exclusive: false, autoDelete: false);
        channel.QueueBind(TestQueue, OutboundExchange, "starbucks.#");
        channel.QueuePurge(TestQueue);

        await SetupAsync();

        var e1 = Guid.NewGuid().ToString();
        var e2 = Guid.NewGuid().ToString();
        var e3 = Guid.NewGuid().ToString();
        var e4 = Guid.NewGuid().ToString();

        // ── OB01: order → points.earned payload + first tier assignment ───
        await RunTest("OB01 — 1000TL mobile/coffee → points.earned (Stars 350) + tier.changed (→yesil)", async () =>
        {
            await SendOrderAsync(channel, e1, "ob_user1", 1000m, "mobile", "coffee");
            await WaitAsync(2500);

            var payload = await ScalarStringAsync($"""
                SELECT payload::text FROM outbox_events
                WHERE tenant_id='starbucks' AND event_type='loyalty.points.earned'
                  AND payload->'data'->>'source_event_id' = '{e1}'
                """);
            Assert("points.earned outbox row exists", payload is not null ? "ok" : "missing", "ok");

            using var doc = JsonDocument.Parse(payload!);
            var root = doc.RootElement;
            Assert("envelope.tenant", root.GetProperty("tenant").GetString(), "starbucks");
            Assert("envelope.version", root.GetProperty("version").GetString(), "1");
            var data = root.GetProperty("data");
            Assert("data.contact_key", data.GetProperty("contact_key").GetString(), "ob_user1");

            var accounts = data.GetProperty("accounts").EnumerateArray().ToList();
            // Stamps were retired by CR 2026-10-05 — Stars is the only wallet this order hits.
            Assert("accounts length (Stars)", accounts.Count, 1);

            var stars = accounts.FirstOrDefault(a => a.GetProperty("code").GetString() == "Stars");
            Assert("Stars.account_type", stars.GetProperty("account_type").GetString(), "POINTS");
            Assert("Stars.delta = 350.00 (Kış 300 + İlk 50)", stars.GetProperty("delta").GetString(), "350.00");
            Assert("Stars.balance = 350.00", stars.GetProperty("balance").GetString(), "350.00");

            // Kış + İlk Alışveriş (the Kahve Damgası stamp rule was removed with stamps).
            Assert("applied_rules length", data.GetProperty("applied_rules").GetArrayLength(), 2);

            var tierCount = await ScalarIntAsync("""
                SELECT COUNT(*) FROM outbox_events
                WHERE tenant_id='starbucks' AND contact_key='ob_user1'
                  AND event_type='loyalty.tier.changed'
                  AND payload->'data'->>'from_tier' IS NULL
                  AND payload->'data'->>'to_tier' = 'yesil'
                  AND payload->'data'->>'direction' = 'up'
                """);
            Assert("tier.changed: first assignment (null → yesil, up)", tierCount, 1);
        });

        // ── OB02: same order again → dedup, no second event ────────────────
        await RunTest("OB02 — same event_id again → points.earned/tier.changed don't duplicate, balance unchanged", async () =>
        {
            await SendOrderAsync(channel, e1, "ob_user1", 1000m, "mobile", "coffee");
            await WaitAsync(2000);

            var stars = await GetBalanceAsync("ob_user1", "Stars");
            Assert("Stars still 350", stars, 350m);

            var earnedCount = await ScalarIntAsync($"""
                SELECT COUNT(*) FROM outbox_events
                WHERE tenant_id='starbucks' AND event_type='loyalty.points.earned'
                  AND payload->'data'->>'source_event_id' = '{e1}'
                """);
            Assert("points.earned single row", earnedCount, 1);

            var tierCount = await ScalarIntAsync("""
                SELECT COUNT(*) FROM outbox_events
                WHERE tenant_id='starbucks' AND contact_key='ob_user1'
                  AND event_type='loyalty.tier.changed'
                """);
            Assert("tier.changed single row", tierCount, 1);
        });

        // ── OB03: second order → new points.earned + tier upgrade ──────────
        await RunTest("OB03 — 2500TL coffee → points.earned (Stars 750/1100) + tier.changed (yesil→altin)", async () =>
        {
            await SendOrderAsync(channel, e2, "ob_user1", 2500m, "mobile", "coffee");
            await WaitAsync(2500);

            var payload = await ScalarStringAsync($"""
                SELECT payload::text FROM outbox_events
                WHERE tenant_id='starbucks' AND event_type='loyalty.points.earned'
                  AND payload->'data'->>'source_event_id' = '{e2}'
                """);
            Assert("points.earned #2 exists", payload is not null ? "ok" : "missing", "ok");

            using var doc = JsonDocument.Parse(payload!);
            var data = doc.RootElement.GetProperty("data");
            var stars = data.GetProperty("accounts").EnumerateArray()
                .FirstOrDefault(a => a.GetProperty("code").GetString() == "Stars");
            Assert("Stars.delta = 750.00 (Kış, İlk Alışveriş already used up)", stars.GetProperty("delta").GetString(), "750.00");
            Assert("Stars.balance = 1100.00", stars.GetProperty("balance").GetString(), "1100.00");
            Assert("applied_rules length (Kış)", data.GetProperty("applied_rules").GetArrayLength(), 1);

            var tierCount = await ScalarIntAsync("""
                SELECT COUNT(*) FROM outbox_events
                WHERE tenant_id='starbucks' AND contact_key='ob_user1'
                  AND event_type='loyalty.tier.changed'
                  AND payload->'data'->>'from_tier' = 'yesil'
                  AND payload->'data'->>'to_tier'   = 'altin'
                  AND payload->'data'->>'direction' = 'up'
                  AND payload->'data'->>'qualifying_points' = '1100.00'
                """);
            Assert("tier.changed: yesil → altin (1100.00)", tierCount, 1);
        });

        // ── OB04: refund → points.reversed ─────────────────────────────────
        await RunTest("OB04 — 50% refund → points.reversed (Stars −375.00/725.00)", async () =>
        {
            PublishRaw(channel, "order.refunded", Guid.NewGuid().ToString(), new
            {
                contact_key = "ob_user1",
                original_event_id = e2,
                refund_ratio = "0.5"
            });
            await WaitAsync(2000);

            var stars = await GetBalanceAsync("ob_user1", "Stars");
            Assert("Stars 1100 − 375 = 725", stars, 725m);

            var payload = await ScalarStringAsync($"""
                SELECT payload::text FROM outbox_events
                WHERE tenant_id='starbucks' AND event_type='loyalty.points.reversed'
                  AND payload->'data'->>'original_event_id' = '{e2}'
                """);
            Assert("points.reversed row exists", payload is not null ? "ok" : "missing", "ok");

            using var doc = JsonDocument.Parse(payload!);
            var data = doc.RootElement.GetProperty("data");
            Assert("refund_ratio = 0.5", data.GetProperty("refund_ratio").GetString(), "0.5");
            var accounts = data.GetProperty("accounts").EnumerateArray().ToList();
            Assert("accounts is Stars only", accounts.Count, 1);
            Assert("Stars.delta = -375.00", accounts[0].GetProperty("delta").GetString(), "-375.00");
            Assert("Stars.balance = 725.00", accounts[0].GetProperty("balance").GetString(), "725.00");

            var reversedCount = await ScalarIntAsync(
                "SELECT COUNT(*) FROM outbox_events WHERE tenant_id='starbucks' AND event_type='loyalty.points.reversed'");
            Assert("points.reversed single row", reversedCount, 1);
        });

        // ── OB05: nightly job → tier.changed (down) ────────────────────────
        await RunTest("OB05 — TierDowngradeJob → tier.changed (altin→yesil, down, 0.00)", async () =>
        {
            // Make the period look ended: it started 400 days ago, grace ran out yesterday.
            // No earns inside the window [start, start+365d) → requalification is 0.
            await ExecuteSqlAsync("""
                UPDATE customer_accounts ca
                SET tier_period_start = (now() - interval '400 days')::date,
                    tier_expires_at   = now() - interval '1 day'
                FROM account_types at
                WHERE at.id = ca.account_type_id
                  AND ca.tenant_id = 'starbucks' AND ca.contact_key = 'ob_user1'
                  AND at.name = 'Stars'
                """);

            await RunJobAsync<TierDowngradeJob>(j => j.RunAsync());

            var count = await ScalarIntAsync("""
                SELECT COUNT(*) FROM outbox_events
                WHERE tenant_id='starbucks' AND contact_key='ob_user1'
                  AND event_type='loyalty.tier.changed'
                  AND payload->'data'->>'from_tier' = 'altin'
                  AND payload->'data'->>'to_tier'   = 'yesil'
                  AND payload->'data'->>'direction' = 'down'
                  AND payload->'data'->>'qualifying_points' = '0.00'
                """);
            Assert("tier.changed down row", count, 1);

            var consistent = await ScalarIntAsync("""
                SELECT COUNT(*) FROM outbox_events
                WHERE tenant_id='starbucks' AND event_type='loyalty.tier.changed'
                  AND payload->'data'->>'direction' = 'down'
                  AND event_id::text = payload->>'eventId'
                """);
            Assert("event_id column = payload.eventId", consistent, 1);

            // Second run: the period was reset → no candidates, no new event is produced
            await RunJobAsync<TierDowngradeJob>(j => j.RunAsync());
            var after = await ScalarIntAsync("""
                SELECT COUNT(*) FROM outbox_events
                WHERE tenant_id='starbucks' AND event_type='loyalty.tier.changed'
                  AND payload->'data'->>'direction' = 'down'
                """);
            Assert("did not duplicate on the second run", after, 1);
        });

        // ── OB06: expire job → points.expired ──────────────────────────────
        await RunTest("OB06 — PointsExpirationJob → points.expired (200.00, balance 0.00)", async () =>
        {
            await SendOrderAsync(channel, e3, "ob_user2", 500m, "web", "sandwich");
            await WaitAsync(2000);

            var starsBefore = await GetBalanceAsync("ob_user2", "Stars");
            if (starsBefore != 200m)
                throw new Exception($"setup error: ob_user2 stars {starsBefore} != 200 (Kış 150 + İlk 50)");

            // Move the earns outside the expiration_days(365) window
            await ExecuteSqlAsync("""
                UPDATE ledger_entries
                SET created_at = now() - interval '400 days'
                WHERE tenant_id='starbucks' AND contact_key='ob_user2' AND reason='earn'
                """);

            await RunJobAsync<PointsExpirationJob>(j => j.RunAsync());

            var stars = await GetBalanceAsync("ob_user2", "Stars");
            Assert("Stars 200 → 0", stars, 0m);

            var payload = await ScalarStringAsync("""
                SELECT payload::text FROM outbox_events
                WHERE tenant_id='starbucks' AND contact_key='ob_user2'
                  AND event_type='loyalty.points.expired'
                """);
            Assert("points.expired row exists", payload is not null ? "ok" : "missing", "ok");

            using var doc = JsonDocument.Parse(payload!);
            var data = doc.RootElement.GetProperty("data");
            Assert("amount = 200.00", data.GetProperty("amount").GetString(), "200.00");
            Assert("balance = 0.00", data.GetProperty("balance").GetString(), "0.00");
            Assert("code = Stars", data.GetProperty("code").GetString(), "Stars");
            Assert("account_type = POINTS", data.GetProperty("account_type").GetString(), "POINTS");
            Assert("eventId consistent", doc.RootElement.GetProperty("eventId").GetString() is { Length: 36 } ? "ok" : "short", "ok");

            var ledgerCount = await ScalarIntAsync(
                "SELECT COUNT(*) FROM ledger_entries WHERE tenant_id='starbucks' AND contact_key='ob_user2' AND reason='points_expired' AND delta = -200");
            Assert("ledger: points_expired −200", ledgerCount, 1);
        });

        // ── OB07: detector → points.expiring (warning) ─────────────────────
        await RunTest("OB07 — PointsExpiringDetectorJob → points.expiring (200.00, today+5), dedup", async () =>
        {
            // Turn the warning on — it is turned off at the end of the test so the
            // real nightly 01:00 run does not produce warnings for the test users.
            await ExecuteSqlAsync("UPDATE account_types SET config = jsonb_set(config, '{warning_days}', '7') WHERE tenant_id = 'starbucks' AND type = 'POINTS'");

            await SendOrderAsync(channel, e4, "ob_user3", 500m, "web", "sandwich");
            await WaitAsync(2000);

            var starsBefore = await GetBalanceAsync("ob_user3", "Stars");
            if (starsBefore != 200m)
                throw new Exception($"setup error: ob_user3 stars {starsBefore} != 200 (Kış 150 + İlk 50)");

            // Pretend the earn happened 360 days ago: it lapses on day 365, i.e. today+5
            // → inside the 7-day warning window.
            await ExecuteSqlAsync("""
                UPDATE ledger_entries
                SET created_at = now() - interval '360 days'
                WHERE tenant_id='starbucks' AND contact_key='ob_user3' AND reason='earn'
                """);

            await RunJobAsync<PointsExpiringDetectorJob>(j => j.RunAsync());

            var expiresOn = DateTime.UtcNow.Date.AddDays(5).ToString("yyyy-MM-dd");
            var payload = await ScalarStringAsync("""
                SELECT payload::text FROM outbox_events
                WHERE tenant_id='starbucks' AND contact_key='ob_user3'
                  AND event_type='loyalty.points.expiring'
                """);
            Assert("points.expiring row exists", payload is not null ? "ok" : "missing", "ok");

            using var doc = JsonDocument.Parse(payload!);
            var data = doc.RootElement.GetProperty("data");
            Assert("amount = 200.00", data.GetProperty("amount").GetString(), "200.00");
            Assert("code = Stars", data.GetProperty("code").GetString(), "Stars");
            Assert("account_type = POINTS", data.GetProperty("account_type").GetString(), "POINTS");
            Assert($"expires_on = {expiresOn} (today+5)", data.GetProperty("expires_on").GetString(), expiresOn);

            var dedupOk = await ScalarIntAsync($"""
                SELECT COUNT(*) FROM outbox_events
                WHERE tenant_id='starbucks' AND event_type='loyalty.points.expiring'
                  AND dedup_key LIKE 'expiring:%:{expiresOn}'
                  AND event_id::text = payload->>'eventId'
                """);
            Assert("dedup_key dated to the window + eventId consistent", dedupOk, 1);

            // The warning is informational — it never touches the ledger, balance unchanged
            var stars = await GetBalanceAsync("ob_user3", "Stars");
            Assert("balance unchanged (200)", stars, 200m);
            var ledgerCount = await ScalarIntAsync(
                "SELECT COUNT(*) FROM ledger_entries WHERE tenant_id='starbucks' AND contact_key='ob_user3' AND reason='points_expired'");
            Assert("no points_expired written to the ledger", ledgerCount, 0);

            // Second run: same window → dedup. Nothing outside the window either:
            // ob_user1's earns are fresh, ob_user2's balance is 0.
            await RunJobAsync<PointsExpiringDetectorJob>(j => j.RunAsync());
            var total = await ScalarIntAsync(
                "SELECT COUNT(*) FROM outbox_events WHERE tenant_id='starbucks' AND event_type='loyalty.points.expiring'");
            Assert("total expiring = 1 (dedup + window filter)", total, 1);

            await ExecuteSqlAsync("UPDATE account_types SET config = config - 'warning_days' WHERE tenant_id = 'starbucks' AND type = 'POINTS'");
        });

        // ── OB08: publisher end-to-end ──────────────────────────────────────
        await RunTest("OB08 — Publisher: 12 events published, queue counts correct", async () =>
        {
            var pending = -1;
            for (int i = 0; i < 20; i++)
            {
                pending = await ScalarIntAsync(
                    "SELECT COUNT(*) FROM outbox_events WHERE tenant_id='starbucks' AND status='pending'");
                if (pending == 0) break;
                await Task.Delay(500);
            }
            Assert("no outbox rows left pending", pending, 0);

            var published = await ScalarIntAsync(
                "SELECT COUNT(*) FROM outbox_events WHERE tenant_id='starbucks' AND status='published' AND published_at IS NOT NULL");
            Assert("outbox published = 12", published, 12);

            // Let the publisher's final batch land in the queue
            await Task.Delay(1000);

            int earned = 0, reversed = 0, tier = 0, expired = 0, expiring = 0, other = 0;
            var routingOk = true;
            while (channel.BasicGet(TestQueue, autoAck: true) is { } msg)
            {
                if (!msg.RoutingKey.StartsWith("starbucks.loyalty.")) routingOk = false;
                using var json = JsonDocument.Parse(Encoding.UTF8.GetString(msg.Body.ToArray()));
                switch (json.RootElement.GetProperty("eventType").GetString())
                {
                    case "loyalty.points.earned":   earned++;   break;
                    case "loyalty.points.reversed": reversed++; break;
                    case "loyalty.tier.changed":    tier++;     break;
                    case "loyalty.points.expired":  expired++;  break;
                    case "loyalty.points.expiring": expiring++; break;
                    default: other++; break;
                }
            }

            Assert("queue: points.earned = 4", earned, 4);
            Assert("queue: points.reversed = 1", reversed, 1);
            Assert("queue: tier.changed = 5", tier, 5);
            Assert("queue: points.expired = 1", expired, 1);
            Assert("queue: points.expiring = 1", expiring, 1);
            Assert("queue: no other events", other, 0);
            Assert("routing key {tenant}.{event_type}", routingOk ? "ok" : "broken", "ok");
        });

        TeardownAsync(channel);

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
            "DELETE FROM reward_log WHERE tenant_id = 'starbucks'",
            "DELETE FROM outbox_events WHERE tenant_id = 'starbucks'",
            "DELETE FROM tier_upgrade_log WHERE tenant_id = 'starbucks'",
            "DELETE FROM ledger_entries WHERE tenant_id = 'starbucks'",
            "DELETE FROM customer_accounts WHERE tenant_id = 'starbucks'",
            "DELETE FROM event_inbox WHERE tenant_id = 'starbucks'",
            // If a previous run crashed mid-OB07 this may have been left on
            "UPDATE account_types SET config = config - 'warning_days' WHERE tenant_id = 'starbucks' AND type = 'POINTS'"
        })
            await ExecuteSqlAsync(sql);

        await FlushRedisLimitsAsync();
        AnsiConsole.MarkupLine("[grey]DB (starbucks) and Redis limit cache reset.[/]\n");
    }

    static void TeardownAsync(IModel channel)
    {
        channel.QueueDelete(TestQueue);
        AnsiConsole.MarkupLine("\n[grey]Teardown: test queue deleted.[/]");
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

    static Task WaitAsync(int ms = 1200) => Task.Delay(ms);

    static async Task FlushRedisLimitsAsync()
    {
        var lua = "local ks=redis.call('keys','limit:starbucks:*') for _,k in ipairs(ks) do redis.call('del',k) end return #ks";
        var psi = new ProcessStartInfo("/usr/local/bin/docker")
        {
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            UseShellExecute        = false
        };
        psi.ArgumentList.Add("exec"); psi.ArgumentList.Add("loyalty-redis-1");
        psi.ArgumentList.Add("redis-cli"); psi.ArgumentList.Add("eval");
        psi.ArgumentList.Add(lua); psi.ArgumentList.Add("0");
        using var proc = Process.Start(psi)!;
        await Task.WhenAll(proc.StandardOutput.ReadToEndAsync(), proc.StandardError.ReadToEndAsync());
        await proc.WaitForExitAsync();
    }

    // ── Sending events ───────────────────────────────────────────────────

    static async Task SendOrderAsync(IModel ch, string eventId, string contact, decimal amount, string channel, string category)
    {
        PublishRaw(ch, "order.created", eventId, new
        {
            contact_key    = contact,
            amount         = amount.ToString("F2", System.Globalization.CultureInfo.InvariantCulture),
            channel,
            payment_method = "card",
            items          = new[] { new { sku = "SKU-001", category, qty = 1, total = amount.ToString("F2", System.Globalization.CultureInfo.InvariantCulture) } }
        });
        await Task.CompletedTask;
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

    // ── DB helpers ───────────────────────────────────────────────────────

    static async Task<decimal> GetBalanceAsync(string contactKey, string accountName)
    {
        await using var db = new NpgsqlConnection(PG);
        await db.OpenAsync();
        const string sql = """
            SELECT ca.balance FROM customer_accounts ca
            JOIN account_types at ON at.id = ca.account_type_id
            WHERE ca.tenant_id = @t AND ca.contact_key = @c AND at.name = @n
            """;
        await using var cmd = new NpgsqlCommand(sql, db);
        cmd.Parameters.AddWithValue("t", TENANT);
        cmd.Parameters.AddWithValue("c", contactKey);
        cmd.Parameters.AddWithValue("n", accountName);
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
