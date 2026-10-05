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
/// Multi-tenant scenario test:
///
/// STARBUCKS (expiration_days=365, tier: yesil/altin/siyah, qualifying=365 days)
///   sbux_alice : 10 orders → tier upgrade → some points backdated (expire candidates)
///   sbux_bob   : 10 orders → tier upgrade → redeem → backdated points
///
/// BURGER KING (expiration_days=180, tier: bronz/silver/gold LIFETIME+periodic 90 days)
///   bk_carol   : 10 orders → silver tier → backdated points (180-day expire)
///   bk_dave    : 8 orders  → gold tier   → backdated points
///   bk_eve     : 5 orders  → bronz tier  (LIFETIME — no downgrade)
///
/// Then:
///   1. Run the expire job → only the backdated ones should be affected
///   2. Run the tier downgrade job → only periodic tiers should be affected
/// </summary>
public static class MultiTenantTest
{
    const string PG_DEV = "Host=localhost;Database=loyalty_dev;Username=postgres;Password=postgres";

    // Tenants
    const string SBUX = "starbucks";
    const string BK   = "burgerking";

    // Starbucks program/account type
    const string SBUX_PROGRAM  = "018fcd01-0000-7000-8000-000000000001";
    const string SBUX_STARS_AT = "018fcd02-0000-7000-8000-000000000001";

    // Burger King program/account type
    const string BK_PROGRAM  = "018fce01-0000-7000-8000-000000000001";
    const string BK_CROWN_AT = "018fce02-0000-7000-8000-000000000001";
    const string BK_CARD_AT  = "018fce02-0000-7000-8000-000000000002";

    static IModel? _channel;
    static readonly Dictionary<string, Guid> TenantIds = new();

    static async Task LoadTenantIdsAsync()
    {
        await using var db = new NpgsqlConnection(PG_DEV);
        await db.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT slug, id FROM tenants WHERE slug IN (@s, @b)", db);
        cmd.Parameters.AddWithValue("s", SBUX);
        cmd.Parameters.AddWithValue("b", BK);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
            TenantIds[r.GetString(0)] = r.GetGuid(1);
    }

    public static async Task RunAsync(IModel channel)
    {
        _channel = channel;

        AnsiConsole.Write(new Rule("[bold yellow]Multi-Tenant Test[/]").RuleStyle("grey"));
        AnsiConsole.MarkupLine("[grey]Starbucks (365-day expire, 365-day tier) + Burger King (180-day expire, 90-day tier)[/]\n");

        await LoadTenantIdsAsync();
        await CleanupAsync();

        // ── Phase 1: Normal orders (today, to qualify for tiers) ─────────────
        await AnsiConsole.Status().StartAsync("Phase 1: sending orders...", async ctx =>
        {
            // STARBUCKS
            // sbux_alice: 10 orders × 150 TL = Kış 3x → floor(150*0.30)=45 × 10 = 450 pts
            // altin threshold is 1000 → not enough, stays yesil
            ctx.Status("sbux_alice: 10 orders...");
            for (int i = 0; i < 10; i++)
                await SendOrderAsync(SBUX, "sbux_alice", 150m, "store", "food");
            await Task.Delay(1500);

            // sbux_bob: 10 orders × 200 TL = 60 pts × 10 = 600 pts (altin = 1000, not enough)
            // 3 of them are coffee orders (they no longer earn stamps — retired by CR 2026-10-05)
            ctx.Status("sbux_bob: 10 orders...");
            for (int i = 0; i < 7; i++)
                await SendOrderAsync(SBUX, "sbux_bob", 200m, "mobile", "food");
            for (int i = 0; i < 3; i++)
                await SendOrderAsync(SBUX, "sbux_bob", 200m, "store", "coffee");
            await Task.Delay(1500);

            // BURGER KING
            // bk_carol: 10 orders × 200 TL = Whopper 2x → floor(200*0.20)=40 × 10 = 400 pts
            // silver threshold is 500 → not enough (will upgrade via backdated points)
            ctx.Status("bk_carol: 10 orders...");
            for (int i = 0; i < 10; i++)
                await SendOrderAsync(BK, "bk_carol", 200m, "store", "burger");
            await Task.Delay(1500);

            // bk_dave: 8 orders × 300 TL = Whopper 2x → floor(300*0.20)=60 × 8 = 480 pts
            ctx.Status("bk_dave: 8 orders...");
            for (int i = 0; i < 8; i++)
                await SendOrderAsync(BK, "bk_dave", 300m, "store", "burger");
            await Task.Delay(1500);

            // bk_eve: 5 orders × 100 TL = base 1x → 10 pts × 5 = 50 pts (stays bronz)
            ctx.Status("bk_eve: 5 orders...");
            for (int i = 0; i < 5; i++)
                await SendOrderAsync(BK, "bk_eve", 100m, "store", "fries");
            await Task.Delay(1500);
        });

        await ShowAllBalancesAsync("After phase 1 — current balances");

        // ── Phase 2: Add backdated earn entries ─────────────────────────────
        // Starbucks: for sbux_alice and sbux_bob, 400 days ago (365 expire → candidates)
        // Burger King: for bk_carol, bk_dave, 200 days ago (180 expire → candidates)
        //              also add for bk_eve 200 days ago, already small → will expire
        AnsiConsole.MarkupLine("\n[grey]Phase 2: adding backdated earn entries...[/]");
        await using var db = new NpgsqlConnection(PG_DEV);
        await db.OpenAsync();

        // sbux_alice: +800 pts, 400 days ago (past the 365 cutoff → expire candidate)
        await BackdateEarnAsync(db, SBUX, "sbux_alice", SBUX_STARS_AT, 800m, 400);
        // sbux_bob: +600 pts, 400 days ago + spend 300 pts (FIFO test)
        await BackdateEarnAsync(db, SBUX, "sbux_bob",   SBUX_STARS_AT, 600m, 400);
        await BackdateSpendAsync(db, SBUX, "sbux_bob",  SBUX_STARS_AT, 300m, 350);

        // bk_carol: +1200 pts, 200 days ago (180 cutoff → expire candidate) → upgrades to silver (500 threshold)
        await BackdateEarnAsync(db, BK, "bk_carol", BK_CROWN_AT, 1200m, 200);
        // bk_dave: +2500 pts, 200 days ago → upgrades to gold (2000 threshold)
        await BackdateEarnAsync(db, BK, "bk_dave",  BK_CROWN_AT, 2500m, 200);
        // bk_eve: +100 pts, 200 days ago (expire candidate, but bronz is LIFETIME → no downgrade)
        await BackdateEarnAsync(db, BK, "bk_eve",   BK_CROWN_AT, 100m,  200);

        // Update balances so they also include the backdated entries
        await RecalcAllBalancesAsync(db);

        AnsiConsole.MarkupLine("[grey]  sbux_alice : +800 ★ (400 days ago)[/]");
        AnsiConsole.MarkupLine("[grey]  sbux_bob   : +600 ★ (400 days ago) −300 ★ (350 days ago)[/]");
        AnsiConsole.MarkupLine("[grey]  bk_carol   : +1200 Crown (200 days ago)[/]");
        AnsiConsole.MarkupLine("[grey]  bk_dave    : +2500 Crown (200 days ago)[/]");
        AnsiConsole.MarkupLine("[grey]  bk_eve     : +100 Crown  (200 days ago)[/]");

        await ShowAllBalancesAsync("After phase 2 — backdated entries added");

        // ── Phase 3: Trigger the Consumer — tier will be recalculated ───────
        // Balances changed; sending one more order triggers tier evaluation
        AnsiConsole.MarkupLine("\n[grey]Phase 3: triggering tier evaluation (1 trigger order each)...[/]");
        await SendOrderAsync(SBUX, "sbux_alice", 10m, "store", "food");
        await SendOrderAsync(SBUX, "sbux_bob",   10m, "store", "food");
        await SendOrderAsync(BK,   "bk_carol",   10m, "store", "fries");
        await SendOrderAsync(BK,   "bk_dave",    10m, "store", "fries");
        await Task.Delay(2000);

        await ShowTierStatusAsync("After phase 3 — tier status");

        // ── Phase 4: Run the expire job ──────────────────────────────────────
        AnsiConsole.MarkupLine("\n[bold yellow]⚙  Running PointsExpirationJob...[/]");
        var (expCustomers, expPoints) = await RunExpireJobAsync();
        AnsiConsole.MarkupLine($"[green]✓ Expire: {expCustomers} customers, {expPoints:F0} points expired[/]");
        AnsiConsole.MarkupLine("[grey]Expected: sbux_alice 800★, sbux_bob 300★ (600-300 FIFO), bk_carol 1200Crown, bk_dave 2500Crown, bk_eve 100Crown[/]");

        await ShowAllBalancesAsync("After phase 4 — expire result");
        await ShowExpireLogAsync(db);

        // ── Phase 5: Run the tier downgrade job ─────────────────────────────
        // The backdated points have now been expired.
        // BK tiers' period_start is today → downgrade would trigger in 90 days
        // To simulate that, pull tier_expires_at into the past
        AnsiConsole.MarkupLine("\n[grey]Phase 5: pushing BK tier periods into the past (downgrade simulation)...[/]");
        await SimulateTierExpiryAsync(db);

        AnsiConsole.MarkupLine("[bold yellow]⚙  Running TierDowngradeJob...[/]");
        await RunDowngradeJobAsync();

        await ShowTierStatusAsync("After phase 5 — downgrade result");
        AnsiConsole.MarkupLine("[grey]Expected:[/]");
        AnsiConsole.MarkupLine("[grey]  bk_carol: not Gold, drops to whatever the balance supports → Silver or Bronze[/]");
        AnsiConsole.MarkupLine("[grey]  bk_dave:  Gold → based on the post-expire balance[/]");
        AnsiConsole.MarkupLine("[grey]  bk_eve:   Bronze — LIFETIME, unaffected by downgrade[/]");
        AnsiConsole.MarkupLine("[grey]  sbux_*:   qualifying_days=365, 365 days haven't passed → unchanged[/]");

        await ShowTierHistoryAsync(db);

        AnsiConsole.Write(new Rule("[grey]Multi-tenant test complete[/]").RuleStyle("grey"));
    }

    // ── Sending events ────────────────────────────────────────────────────────

    static void Publish(string tenant, string routingKey, string eventId, object data)
    {
        var envelope = new
        {
            EventId    = eventId,
            EventType  = routingKey,
            Tenant     = tenant,
            OccurredAt = DateTime.UtcNow,
            Version    = "1",
            Data       = data
        };
        var body = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(envelope,
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
        var props = _channel!.CreateBasicProperties();
        props.Persistent  = true;
        props.ContentType = "application/json";
        _channel.BasicPublish("loyalty.events", routingKey, props, body);
    }

    static async Task SendOrderAsync(string tenant, string contact, decimal amount, string channel, string category)
    {
        var eventId = Guid.NewGuid().ToString();
        Publish(tenant, "order.created", eventId, new
        {
            contact_key    = contact,
            amount         = amount.ToString("F2", System.Globalization.CultureInfo.InvariantCulture),
            channel,
            payment_method = "card",
            items          = new[] { new { sku = "SKU-001", category, qty = 1, total = amount.ToString("F2", System.Globalization.CultureInfo.InvariantCulture) } }
        });
        await Task.Delay(300); // Minimum wait for the Consumer to process
    }

    // ── Backdated seed ────────────────────────────────────────────────────────

    static async Task BackdateEarnAsync(NpgsqlConnection db, string tenant, string contact, string accountTypeId, decimal amount, int daysAgo)
    {
        var accountId = await EnsureAccountAsync(db, tenant, contact, Guid.Parse(accountTypeId));
        var ts  = DateTime.UtcNow.AddDays(-daysAgo);
        var key = $"backdate-earn:{accountId}:{daysAgo}d";
        await using var cmd = new NpgsqlCommand("""
            INSERT INTO ledger_entries
                (id, tenant_id, customer_account_id, contact_key, delta, reason,
                 source_event_id, idempotency_key, metadata, created_at)
            VALUES (gen_random_uuid(), @t, @acct, @c, @delta, 'earn', @src, @idem, '{}', @ts)
            ON CONFLICT (tenant_id, idempotency_key) DO NOTHING
            """, db);
        cmd.Parameters.AddWithValue("t",     tenant);
        cmd.Parameters.AddWithValue("acct",  accountId);
        cmd.Parameters.AddWithValue("c",     contact);
        cmd.Parameters.AddWithValue("delta", amount);
        cmd.Parameters.AddWithValue("src",   key);
        cmd.Parameters.AddWithValue("idem",  key);
        cmd.Parameters.AddWithValue("ts",    ts);
        await cmd.ExecuteNonQueryAsync();
    }

    static async Task BackdateSpendAsync(NpgsqlConnection db, string tenant, string contact, string accountTypeId, decimal amount, int daysAgo)
    {
        var accountId = await EnsureAccountAsync(db, tenant, contact, Guid.Parse(accountTypeId));
        var ts  = DateTime.UtcNow.AddDays(-daysAgo);
        var key = $"backdate-spend:{accountId}:{daysAgo}d";
        await using var cmd = new NpgsqlCommand("""
            INSERT INTO ledger_entries
                (id, tenant_id, customer_account_id, contact_key, delta, reason,
                 source_event_id, idempotency_key, metadata, created_at)
            VALUES (gen_random_uuid(), @t, @acct, @c, @delta, 'points_redeemed', @src, @idem, '{}', @ts)
            ON CONFLICT (tenant_id, idempotency_key) DO NOTHING
            """, db);
        cmd.Parameters.AddWithValue("t",     tenant);
        cmd.Parameters.AddWithValue("acct",  accountId);
        cmd.Parameters.AddWithValue("c",     contact);
        cmd.Parameters.AddWithValue("delta", -amount);
        cmd.Parameters.AddWithValue("src",   key);
        cmd.Parameters.AddWithValue("idem",  key);
        cmd.Parameters.AddWithValue("ts",    ts);
        await cmd.ExecuteNonQueryAsync();
    }

    static async Task<Guid> EnsureAccountAsync(NpgsqlConnection db, string tenant, string contact, Guid accountTypeId)
    {
        await using var sel = new NpgsqlCommand("""
            SELECT id FROM customer_accounts
            WHERE tenant_id=@t AND contact_key=@c AND account_type_id=@at
            """, db);
        sel.Parameters.AddWithValue("t",  TenantIds[tenant]);
        sel.Parameters.AddWithValue("c",  contact);
        sel.Parameters.AddWithValue("at", accountTypeId);
        var existing = await sel.ExecuteScalarAsync();
        if (existing is Guid g) return g;

        var newId = Guid.NewGuid();
        await using var ins = new NpgsqlCommand("""
            INSERT INTO customer_accounts (id, tenant_id, contact_key, account_type_id, balance, updated_at)
            VALUES (@id, @t, @c, @at, 0, NOW())
            ON CONFLICT (tenant_id, contact_key, account_type_id) DO NOTHING
            """, db);
        ins.Parameters.AddWithValue("id", newId);
        ins.Parameters.AddWithValue("t",  TenantIds[tenant]);
        ins.Parameters.AddWithValue("c",  contact);
        ins.Parameters.AddWithValue("at", accountTypeId);
        await ins.ExecuteNonQueryAsync();
        return (Guid)(await new NpgsqlCommand("""
            SELECT id FROM customer_accounts
            WHERE tenant_id=@t AND contact_key=@c AND account_type_id=@at
            """, db) { Parameters = { new("t", TenantIds[tenant]), new("c", contact), new("at", accountTypeId) } }
            .ExecuteScalarAsync())!;
    }

    static async Task RecalcAllBalancesAsync(NpgsqlConnection db)
    {
        await using var cmd = new NpgsqlCommand("""
            UPDATE customer_accounts ca
            SET balance = (
                SELECT COALESCE(SUM(delta), 0)
                FROM ledger_entries
                WHERE customer_account_id = ca.id
            ),
            updated_at = NOW()
            """, db);
        await cmd.ExecuteNonQueryAsync();
    }

    // ── Expire job ────────────────────────────────────────────────────────────
    // The prod jobs (PointsExpirationJob / TierDowngradeJob) are run directly —
    // the test carries no SQL copy of its own; the real job SQL is always verified.

    static ServiceProvider BuildJobServices()
    {
        var services = new ServiceCollection();
        services.AddLogging(b => b.AddConsole().SetMinimumLevel(LogLevel.Warning));
        services.AddDbContext<LoyaltyDbContext>(opts => opts.UseNpgsql(PG_DEV));
        services.AddScoped<PointsExpirationJob>();
        services.AddScoped<TierDowngradeJob>();
        return services.BuildServiceProvider();
    }

    static async Task<(long customers, decimal points)> RunExpireJobAsync()
    {
        await using var sp = BuildJobServices();
        using var scope = sp.CreateScope();
        var job = scope.ServiceProvider.GetRequiredService<PointsExpirationJob>();
        var result = await job.RunAsync();
        return (result.CustomersAffected, result.TotalPointsExpired);
    }

    // ── Downgrade simulation ──────────────────────────────────────────────────

    static async Task SimulateTierExpiryAsync(NpgsqlConnection db)
    {
        // Manually promote the BK customers to silver/gold and
        // move their periods into the past to trigger the downgrade.
        // bk_carol: silver (500 threshold), bk_dave: gold (2000 threshold)
        // period_start = 91 days ago, tier_expires_at = yesterday

        // First set the tiers manually
        await using var setTier = new NpgsqlCommand($"""
            UPDATE customer_accounts ca
            SET tier_id           = '018fce04-0000-7000-8000-000000000002',  -- silver
                tier_period_start = CURRENT_DATE - INTERVAL '91 days',
                tier_expires_at   = CURRENT_DATE - INTERVAL '1 day',
                updated_at        = NOW()
            WHERE ca.tenant_id   = '{TenantIds[BK]}'
              AND ca.contact_key = 'bk_carol'
              AND ca.account_type_id = '018fce02-0000-7000-8000-000000000001'
            """, db);
        await setTier.ExecuteNonQueryAsync();

        await using var setTier2 = new NpgsqlCommand($"""
            UPDATE customer_accounts ca
            SET tier_id           = '018fce04-0000-7000-8000-000000000003',  -- gold
                tier_period_start = CURRENT_DATE - INTERVAL '91 days',
                tier_expires_at   = CURRENT_DATE - INTERVAL '1 day',
                updated_at        = NOW()
            WHERE ca.tenant_id   = '{TenantIds[BK]}'
              AND ca.contact_key = 'bk_dave'
              AND ca.account_type_id = '018fce02-0000-7000-8000-000000000001'
            """, db);
        await setTier2.ExecuteNonQueryAsync();

        AnsiConsole.MarkupLine("[grey]  bk_carol → Silver, bk_dave → Gold (period_start=91 days ago, expires_at=yesterday)[/]");
        AnsiConsole.MarkupLine("[grey]  bk_eve → Bronze (LIFETIME, untouched)[/]");
        AnsiConsole.MarkupLine("[grey]  sbux_* → Starbucks, 365 days haven't elapsed, untouched[/]");
    }

    static async Task RunDowngradeJobAsync()
    {
        await using var sp = BuildJobServices();
        using var scope = sp.CreateScope();
        var job = scope.ServiceProvider.GetRequiredService<TierDowngradeJob>();
        await job.RunAsync();
    }

    // ── Display ───────────────────────────────────────────────────────────────

    static async Task CleanupAsync()
    {
        await using var db = new NpgsqlConnection(PG_DEV);
        await db.OpenAsync();
        foreach (var (tenant, contacts) in new[]
        {
            (SBUX, new[] { "sbux_alice", "sbux_bob" }),
            (BK,   new[] { "bk_carol", "bk_dave", "bk_eve" })
        })
        {
            foreach (var c in contacts)
            {
                foreach (var sql in new[]
                {
                    $"DELETE FROM ledger_entries   WHERE tenant_id='{tenant}' AND contact_key='{c}'",
                    $"DELETE FROM tier_upgrade_log WHERE tenant_id='{TenantIds[tenant]}' AND contact_key='{c}'",
                    $"DELETE FROM customer_accounts WHERE tenant_id='{TenantIds[tenant]}' AND contact_key='{c}'"
                })
                {
                    await using var cmd = new NpgsqlCommand(sql, db);
                    await cmd.ExecuteNonQueryAsync();
                }
            }
        }
        AnsiConsole.MarkupLine("[grey]Test users cleaned up.[/]");
    }

    static async Task ShowAllBalancesAsync(string title)
    {
        await using var db = new NpgsqlConnection(PG_DEV);
        await db.OpenAsync();

        var table = new Table().Border(TableBorder.Rounded).Title($"[bold]{title}[/]");
        table.AddColumn("Tenant");
        table.AddColumn("Customer");
        table.AddColumn("Account");
        table.AddColumn(new TableColumn("Balance").RightAligned());

        const string sql = """
            SELECT tn.slug, ca.contact_key, at.name, at.type, ca.balance
            FROM customer_accounts ca
            JOIN account_types at ON at.id = ca.account_type_id
            JOIN tenants tn ON tn.id = ca.tenant_id
            WHERE ca.contact_key IN ('sbux_alice','sbux_bob','bk_carol','bk_dave','bk_eve')
              AND at.type = 'POINTS'
            ORDER BY tn.slug, ca.contact_key
            """;
        await using var cmd = new NpgsqlCommand(sql, db);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
        {
            var tenant  = r.GetString(0);
            var contact = r.GetString(1);
            var acct    = r.GetString(2);
            var balance = r.GetDecimal(4);
            var color   = tenant == SBUX ? "green" : "yellow";
            table.AddRow($"[{color}]{tenant}[/]", contact, acct, $"[bold]{balance:F0}[/]");
        }
        AnsiConsole.WriteLine();
        AnsiConsole.Write(table);
    }

    static async Task ShowTierStatusAsync(string title)
    {
        await using var db = new NpgsqlConnection(PG_DEV);
        await db.OpenAsync();

        var table = new Table().Border(TableBorder.Rounded).Title($"[bold]{title}[/]");
        table.AddColumn("Tenant");
        table.AddColumn("Customer");
        table.AddColumn("Tier");
        table.AddColumn("Qualifying Pts");
        table.AddColumn("Period Start");
        table.AddColumn("Expires At");

        const string sql = """
            SELECT tn.slug, ca.contact_key,
                   COALESCE(td.display_name, '(none)'),
                   ca.tier_qualifying_pts,
                   ca.tier_period_start,
                   ca.tier_expires_at
            FROM customer_accounts ca
            JOIN account_types at ON at.id = ca.account_type_id
            JOIN tenants tn ON tn.id = ca.tenant_id
            LEFT JOIN tier_definitions td ON td.id = ca.tier_id
            WHERE ca.contact_key IN ('sbux_alice','sbux_bob','bk_carol','bk_dave','bk_eve')
              AND at.type = 'POINTS'
            ORDER BY tn.slug, ca.contact_key
            """;
        await using var cmd = new NpgsqlCommand(sql, db);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
        {
            var tenant  = r.GetString(0);
            var contact = r.GetString(1);
            var tier    = r.GetString(2);
            var pts     = r.GetDecimal(3);
            var pstart  = r.IsDBNull(4) ? "-" : r.GetFieldValue<DateOnly>(4).ToString("yyyy-MM-dd");
            var pexp    = r.IsDBNull(5) ? "-" : r.GetFieldValue<DateOnly>(5).ToString("yyyy-MM-dd");
            var color   = tenant == SBUX ? "green" : "yellow";
            table.AddRow($"[{color}]{tenant}[/]", contact, $"[bold]{tier}[/]", $"{pts:F0}", pstart, pexp);
        }
        AnsiConsole.WriteLine();
        AnsiConsole.Write(table);
    }

    static async Task ShowExpireLogAsync(NpgsqlConnection db)
    {
        var today    = DateOnly.FromDateTime(DateTime.UtcNow);
        var todayStr = today.ToString("yyyy-MM-dd");

        var table = new Table().Border(TableBorder.Simple).Title("[bold]Expire Ledger Entries[/]");
        table.AddColumn("Tenant");
        table.AddColumn("Customer");
        table.AddColumn(new TableColumn("Expired").RightAligned());
        table.AddColumn("Cutoff");

        await using var cmd = new NpgsqlCommand("""
            SELECT le.tenant_id, le.contact_key, ABS(le.delta),
                   le.metadata->>'cutoff_date'
            FROM ledger_entries le
            WHERE le.reason = 'points_expired'
              AND le.idempotency_key LIKE 'expire:%:' || @today
            ORDER BY le.tenant_id, le.contact_key
            """, db);
        cmd.Parameters.AddWithValue("today", todayStr);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
        {
            var tenant  = r.GetString(0);
            var contact = r.GetString(1);
            var pts     = r.GetDecimal(2);
            var cutoff  = r.IsDBNull(3) ? "-" : r.GetString(3)[..10];
            var color   = tenant == SBUX ? "green" : "yellow";
            table.AddRow($"[{color}]{tenant}[/]", contact, $"[orange1]{pts:F0}[/]", cutoff);
        }
        AnsiConsole.WriteLine();
        AnsiConsole.Write(table);
    }

    static async Task ShowTierHistoryAsync(NpgsqlConnection db)
    {
        var table = new Table().Border(TableBorder.Simple).Title("[bold]Tier History (all customers)[/]");
        table.AddColumn("Tenant");
        table.AddColumn("Customer");
        table.AddColumn("From");
        table.AddColumn("To");
        table.AddColumn("Qualifying Pts");
        table.AddColumn("Date");

        await using var cmd = new NpgsqlCommand("""
            SELECT tn.slug, tul.contact_key,
                   COALESCE(td_from.display_name, '(none)'),
                   td_to.display_name,
                   tul.qualifying_pts,
                   tul.created_at
            FROM tier_upgrade_log tul
            JOIN tenants tn ON tn.id = tul.tenant_id
            LEFT JOIN tier_definitions td_from ON td_from.id = tul.from_tier_id
            LEFT JOIN tier_definitions td_to   ON td_to.id   = tul.to_tier_id
            WHERE tul.contact_key IN ('sbux_alice','sbux_bob','bk_carol','bk_dave','bk_eve')
            ORDER BY tn.slug, tul.contact_key, tul.created_at
            """, db);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
        {
            var tenant  = r.GetString(0);
            var contact = r.GetString(1);
            var from    = r.GetString(2);
            var to      = r.GetString(3);
            var pts     = r.GetDecimal(4);
            var ts      = r.GetDateTime(5).ToLocalTime().ToString("MM-dd HH:mm");
            var color   = tenant == SBUX ? "green" : "yellow";
            var arrow   = from == "(none)" ? "[grey]first assignment[/]" : $"{from} → [bold]{to}[/]";
            table.AddRow($"[{color}]{tenant}[/]", contact, from, $"[bold]{to}[/]", $"{pts:F0}", ts);
        }
        AnsiConsole.WriteLine();
        AnsiConsole.Write(table);
    }
}
