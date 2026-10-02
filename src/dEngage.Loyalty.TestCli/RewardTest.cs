using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Npgsql;
using RabbitMQ.Client;
using Spectre.Console;

public static class RewardTest
{
    const string PG     = "Host=localhost;Database=loyalty_dev;Username=postgres;Password=postgres";
    const string TENANT = "starbucks";
    const string StarsAccountId = "018fcd02-0000-7000-8000-000000000001";
    const string StampAccountId = "018fcd02-0000-7000-8000-000000000003";
    const string OutboundExchange = "loyalty.outbound";
    const string TestQueue = "q.test.outbound";

    static int _passed = 0;
    static int _failed = 0;
    static string _tid = "";

    public static async Task RunAsync(IModel channel)
    {
        _tid = await ScalarStringAsync($"SELECT id::text FROM tenants WHERE slug='{TENANT}'") ?? "";
        AnsiConsole.Write(new Rule("[yellow bold]Reward + Outbox Test — Starbucks[/]").RuleStyle("grey"));
        AnsiConsole.MarkupLine("[grey]reward_definitions is seeded, stamp/purchase/outbox flows are tested.[/]\n");

        // The listener queue must be bound BEFORE any publishes — if bound later,
        // earlier messages are lost at the exchange (topic exchanges do not store messages).
        channel.ExchangeDeclare(OutboundExchange, ExchangeType.Topic, durable: true);
        channel.QueueDeclare(TestQueue, durable: true, exclusive: false, autoDelete: false);
        channel.QueueBind(TestQueue, OutboundExchange, "starbucks.#");
        channel.QueuePurge(TestQueue);

        await SetupAsync();

        var stampTarget = await ScalarIntAsync(
            $"SELECT (config->>'stamp_target')::int FROM account_types WHERE id = '{StampAccountId}'");

        // ── RW01: stamp card completes → reward.earned outbox (CR 2026-09-30 A1/O1) ──
        // Stamp-completion reward definitions are retired; the completion is still announced,
        // named after the STAMP account's config.reward_type, with reward_type null.
        var stampRewardName = await ScalarStringAsync(
            $"SELECT config->>'reward_type' FROM account_types WHERE id = '{StampAccountId}'") ?? "";
        await RunTest($"RW01 — {stampTarget} coffees → stamp card fills, '{stampRewardName}' notified + reward.earned outbox", async () =>
        {
            for (int i = 0; i < stampTarget; i++)
            {
                await SendOrderAsync(channel, "rw_user1", 50m, "store", "coffee");
                await Task.Delay(400);
            }
            await WaitAsync(4000);

            var stamp = await GetBalanceAsync("rw_user1", "Kahve Damgası");
            Assert("stamp reset to 0", stamp, 0m);

            var rewardCount = await ScalarIntAsync(
                $"SELECT COUNT(*) FROM reward_log WHERE tenant_id='{_tid}' AND contact_key='rw_user1' AND reward_name='{stampRewardName}' AND status='notified' AND reward_definition_id IS NULL AND delivered_at IS NOT NULL");
            Assert($"reward_log: {stampRewardName} notified (no definition)", rewardCount, 1);

            var outboxCount = await ScalarIntAsync($"""
                SELECT COUNT(*) FROM outbox_events
                WHERE tenant_id='{_tid}' AND contact_key='rw_user1'
                  AND event_type='loyalty.reward.earned'
                  AND payload->'data'->>'reward_type' IS NULL
                  AND payload->'data'->>'source' = 'stamp_completion'
                """);
            Assert("outbox: reward.earned (stamp, reward_type null)", outboxCount, 1);
        });

        // ── RW02: reward.purchase — successful purchase ───────────────────
        await RunTest("RW02 — 500★ buys 'cashback_50' → −500★, +50 on the card + reward.earned outbox", async () =>
        {
            await SendOrderAsync(channel, "rw_user2", 1000m, "mobile", "coffee");
            await SendOrderAsync(channel, "rw_user2", 1000m, "mobile", "coffee");
            await WaitAsync(2500);

            var starsBefore = await GetBalanceAsync("rw_user2", "Stars");
            if (starsBefore < 500m)
                throw new Exception($"setup error: rw_user2 stars {starsBefore} < 500 — rules may have changed");
            var cardBefore = await GetBalanceAsync("rw_user2", "Starbucks Card");

            await SendRewardPurchaseAsync(channel, Guid.NewGuid().ToString(), "rw_user2", "cashback_50");
            await WaitAsync();

            var starsAfter = await GetBalanceAsync("rw_user2", "Stars");
            Assert("Stars −500", starsAfter, starsBefore - 500m);
            // CR 2026-09-30 (A2): the cashback is actually paid into the CASH wallet.
            Assert("Starbucks Card +50 (cashback)", await GetBalanceAsync("rw_user2", "Starbucks Card"), cardBefore + 50m);

            var ledgerCount = await ScalarIntAsync(
                "SELECT COUNT(*) FROM ledger_entries WHERE tenant_id='starbucks' AND contact_key='rw_user2' AND reason='reward_purchase' AND delta = -500");
            Assert("ledger: reward_purchase −500", ledgerCount, 1);

            var rewardCount = await ScalarIntAsync(
                $"SELECT COUNT(*) FROM reward_log WHERE tenant_id='{_tid}' AND contact_key='rw_user2' AND reward_name='cashback_50' AND status='notified' AND reward_definition_id IS NOT NULL");
            Assert("reward_log: cashback_50 notified", rewardCount, 1);

            var outboxCount = await ScalarIntAsync($"""
                SELECT COUNT(*) FROM outbox_events
                WHERE tenant_id='{_tid}' AND contact_key='rw_user2'
                  AND event_type='loyalty.reward.earned'
                  AND payload->'data'->>'reward_type' = 'cashback'
                  AND payload->'data'->>'source' = 'points_purchase'
                  AND payload->'data'->>'points_spent' = '500.00'
                  AND payload->'data'->>'cashback_amount' = '50.00'
                """);
            Assert("outbox: reward.earned (cashback, points_purchase)", outboxCount, 1);
        });

        // ── RW03: Same reward.purchase event twice → single deduction ─────
        await RunTest("RW03 — same event_id twice → points deducted once, single reward + single outbox", async () =>
        {
            await SendOrderAsync(channel, "rw_user3", 1000m, "mobile", "coffee");
            await SendOrderAsync(channel, "rw_user3", 1000m, "mobile", "coffee");
            await WaitAsync(2500);

            var starsBefore = await GetBalanceAsync("rw_user3", "Stars");
            if (starsBefore < 500m)
                throw new Exception($"setup error: rw_user3 stars {starsBefore} < 500");

            var eventId = Guid.NewGuid().ToString();
            await SendRewardPurchaseAsync(channel, eventId, "rw_user3", "cashback_50");
            await WaitAsync();
            await SendRewardPurchaseAsync(channel, eventId, "rw_user3", "cashback_50");
            await WaitAsync();

            var starsAfter = await GetBalanceAsync("rw_user3", "Stars");
            Assert("Stars −500 only once", starsAfter, starsBefore - 500m);

            var rewardCount = await ScalarIntAsync(
                $"SELECT COUNT(*) FROM reward_log WHERE tenant_id='{_tid}' AND contact_key='rw_user3'");
            Assert("single reward_log row", rewardCount, 1);

            var outboxCount = await ScalarIntAsync(
                $"SELECT COUNT(*) FROM outbox_events WHERE tenant_id='{_tid}' AND contact_key='rw_user3' AND event_type='loyalty.reward.earned'");
            Assert("single outbox reward.earned", outboxCount, 1);
        });

        // ── RW04: Insufficient balance → purchase_failed, balance unchanged ──
        await RunTest("RW04 — insufficient points → balance unchanged, purchase_failed outbox, inbox processed", async () =>
        {
            await SendOrderAsync(channel, "rw_user4", 200m, "web", "sandwich");
            await WaitAsync();

            var starsBefore = await GetBalanceAsync("rw_user4", "Stars");
            if (starsBefore >= 500m)
                throw new Exception($"setup error: rw_user4 stars {starsBefore} >= 500");

            var eventId = Guid.NewGuid().ToString();
            await SendRewardPurchaseAsync(channel, eventId, "rw_user4", "cashback_50");
            await WaitAsync();

            var starsAfter = await GetBalanceAsync("rw_user4", "Stars");
            Assert("Stars unchanged", starsAfter, starsBefore);

            var rewardCount = await ScalarIntAsync(
                $"SELECT COUNT(*) FROM reward_log WHERE tenant_id='{_tid}' AND contact_key='rw_user4'");
            Assert("no reward_log row written", rewardCount, 0);

            var outboxCount = await ScalarIntAsync($"""
                SELECT COUNT(*) FROM outbox_events
                WHERE tenant_id='{_tid}' AND contact_key='rw_user4'
                  AND event_type='loyalty.reward.purchase_failed'
                  AND payload->'data'->>'reason' = 'insufficient_balance'
                """);
            Assert("outbox: purchase_failed", outboxCount, 1);

            var inboxStatus = await ScalarStringAsync(
                $"SELECT status FROM event_inbox WHERE tenant_id='starbucks' AND event_id='{eventId}'");
            Assert("inbox processed (business outcome, not an error)", inboxStatus, "processed");
        });

        // ── RW05: Undefined reward → inbox failed ─────────────────────────
        await RunTest("RW05 — undefined reward_name → inbox failed (unknown_reward)", async () =>
        {
            var eventId = Guid.NewGuid().ToString();
            await SendRewardPurchaseAsync(channel, eventId, "rw_user4", "no_such_reward");
            await WaitAsync();

            var inboxStatus = await ScalarStringAsync(
                $"SELECT status FROM event_inbox WHERE tenant_id='starbucks' AND event_id='{eventId}'");
            Assert("inbox failed", inboxStatus, "failed");

            var error = await ScalarStringAsync(
                $"SELECT error FROM event_inbox WHERE tenant_id='starbucks' AND event_id='{eventId}'");
            Assert("error message unknown_reward", error is not null && error.StartsWith("unknown_reward") ? "ok" : error, "ok");
        });

        // ── RW06: Publisher — outbox rows are pushed to RabbitMQ ──────────
        // Orders now also produce points.earned/tier.changed; this suite only
        // counts reward.* events (the other producers are OutboundTest's scope).
        await RunTest("RW06 — Publisher: reward outbox rows published, 4 reward messages land on the queue", async () =>
        {
            // The publisher polls once per second; wait until pending is drained (max 10 s)
            var pending = -1;
            for (int i = 0; i < 20; i++)
            {
                pending = await ScalarIntAsync(
                    $"SELECT COUNT(*) FROM outbox_events WHERE tenant_id='{_tid}' AND status='pending'");
                if (pending == 0) break;
                await Task.Delay(500);
            }
            Assert("no outbox rows left pending", pending, 0);

            var publishedCount = await ScalarIntAsync(
                $"SELECT COUNT(*) FROM outbox_events WHERE tenant_id='{_tid}' AND status='published' AND published_at IS NOT NULL AND event_type LIKE 'loyalty.reward.%'");
            Assert("outbox published reward.* = 4", publishedCount, 4);

            int earned = 0, failed = 0, other = 0;
            string? sampleRoutingKey = null;
            while (channel.BasicGet(TestQueue, autoAck: true) is { } msg)
            {
                sampleRoutingKey ??= msg.RoutingKey;
                using var json = JsonDocument.Parse(Encoding.UTF8.GetString(msg.Body.ToArray()));
                var et = json.RootElement.GetProperty("eventType").GetString();
                if (et == "loyalty.reward.earned") earned++;
                else if (et == "loyalty.reward.purchase_failed") failed++;
                else if (et is "loyalty.points.earned" or "loyalty.points.reversed"
                            or "loyalty.tier.changed" or "loyalty.points.expired") { /* out of scope */ }
                else other++;
            }

            Assert("queue: reward.earned = 3", earned, 3);
            Assert("queue: purchase_failed = 1", failed, 1);
            Assert("queue: no unrecognized events", other, 0);
            Assert("routing key {tenant}.{event_type}",
                sampleRoutingKey is not null && sampleRoutingKey.StartsWith("starbucks.loyalty.") ? "ok" : sampleRoutingKey, "ok");
        });

        await TeardownAsync(channel);

        AnsiConsole.WriteLine();
        AnsiConsole.Write(new Rule().RuleStyle("grey"));
        var color = _failed == 0 ? "green" : "red";
        AnsiConsole.MarkupLine($"[{color} bold]Result: {_passed} asserts passed, {_failed} tests failed[/]");
    }

    // ── Setup / Teardown ─────────────────────────────────────────────────

    static async Task SetupAsync()
    {
        await using var db = new NpgsqlConnection(PG);
        await db.OpenAsync();

        foreach (var sql in new[]
        {
            $"DELETE FROM reward_log WHERE tenant_id = '{_tid}'",
            $"DELETE FROM reward_definitions WHERE tenant_id = '{_tid}'",
            $"DELETE FROM outbox_events WHERE tenant_id = '{_tid}'",
            "DELETE FROM ledger_entries WHERE tenant_id = 'starbucks'",
            $"DELETE FROM customer_accounts WHERE tenant_id = '{_tid}'",
            "DELETE FROM event_inbox WHERE tenant_id = 'starbucks'"
        })
        {
            await using var cmd = new NpgsqlCommand(sql, db);
            await cmd.ExecuteNonQueryAsync();
        }

        await using var seed = new NpgsqlCommand($$"""
            INSERT INTO reward_definitions
                (id, tenant_id, program_id, name, display_name, acquisition,
                 stamp_account_type_id, points_price, points_account_type_id,
                 reward_type, type_config, is_active, created_at)
            SELECT gen_random_uuid(), '{{_tid}}'::uuid, p.id, d.name, d.display_name, d.acquisition,
                   d.stamp_id::uuid, d.price, d.points_id::uuid, d.reward_type, d.type_config::jsonb, true, now()
            FROM (SELECT id FROM programs WHERE tenant_id = '{{_tid}}' LIMIT 1) p
            CROSS JOIN (VALUES
                -- CR 2026-09-30: stamp-completion definitions are retired (RW01 runs without one);
                -- the purchasable reward is a cashback paid into the tenant's CASH card.
                ('cashback_50', '50 TL Cashback', 'points_purchase',
                 NULL, 500::numeric, @stars_account, 'cashback',
                 (SELECT jsonb_build_object('amount', '50.00', 'currency', at.config->>'currency', 'cash_account_type_id', at.id::text)::text
                  FROM account_types at
                  WHERE at.tenant_id = '{{_tid}}'::uuid AND at.type = 'CASH' AND at.name = 'Starbucks Card'))
            ) AS d(name, display_name, acquisition, stamp_id, price, points_id, reward_type, type_config)
            """, db);
        seed.Parameters.AddWithValue("stars_account", StarsAccountId);
        await seed.ExecuteNonQueryAsync();

        AnsiConsole.MarkupLine("[grey]DB reset, reward_definitions seeded (cashback_50).[/]");

        await FlushRedisLimitsAsync();
        AnsiConsole.MarkupLine("[grey]Redis limit cache cleared.[/]\n");
    }

    // Deactivate the definitions: older suites (test/D01 etc.) expect the old
    // definition-less behavior (config reward_type + pending) and break if an active definition remains.
    static async Task TeardownAsync(IModel channel)
    {
        await ExecuteSqlAsync($"UPDATE reward_definitions SET is_active = false WHERE tenant_id = '{_tid}'");
        channel.QueueDelete(TestQueue);
        AnsiConsole.MarkupLine("\n[grey]Teardown: reward_definitions deactivated, test queue deleted.[/]");
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

    // ── Sending events ───────────────────────────────────────────────────

    static async Task SendOrderAsync(IModel ch, string contact, decimal amount, string channel, string category)
    {
        PublishRaw(ch, "order.created", Guid.NewGuid().ToString(), new
        {
            contact_key    = contact,
            amount         = amount.ToString("F2", System.Globalization.CultureInfo.InvariantCulture),
            channel,
            payment_method = "card",
            items          = new[] { new { sku = "SKU-001", category, qty = 1, total = amount.ToString("F2", System.Globalization.CultureInfo.InvariantCulture) } }
        });
        await Task.CompletedTask;
    }

    static async Task SendRewardPurchaseAsync(IModel ch, string eventId, string contact, string rewardName)
    {
        PublishRaw(ch, "reward.purchase", eventId, new
        {
            contact_key = contact,
            reward_name = rewardName,
            channel     = "mobile"
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
            WHERE ca.tenant_id = @t::uuid AND ca.contact_key = @c AND at.name = @n
            """;
        await using var cmd = new NpgsqlCommand(sql, db);
        cmd.Parameters.AddWithValue("t", _tid);
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
