using dEngage.Loyalty.Ledger;
using dEngage.Loyalty.Schema;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;
using Spectre.Console;

/// <summary>
/// Points Expiration Job — live verification.
///
/// The prod job (dEngage.Loyalty.Ledger.PointsExpirationJob) is run in 6 different scenarios.
/// In each scenario the expected result is compared against the actual DB state.
/// </summary>
public static class ExpireJobVerify
{
    const string PG = "Host=localhost;Database=loyalty_dev;Username=postgres;Password=postgres";

    // Fixed IDs come from the seed scripts
    const string SBUX          = "starbucks";
    const string BK            = "burgerking";
    static readonly Guid SbuxStars = Guid.Parse("018fcd02-0000-7000-8000-000000000001");   // 365 days
    static readonly Guid SbuxCard  = Guid.Parse("018fcd02-0000-7000-8000-000000000002");   // CASH (no expire)
    static readonly Guid BkCrown   = Guid.Parse("018fce02-0000-7000-8000-000000000001");   // 180 days

    static int _pass, _fail;
    static readonly Dictionary<string, Guid> TenantIds = new();

    static async Task LoadTenantIdsAsync()
    {
        await using var db = new NpgsqlConnection(PG);
        await db.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT slug, id FROM tenants WHERE slug IN (@s, @b)", db);
        cmd.Parameters.AddWithValue("s", SBUX);
        cmd.Parameters.AddWithValue("b", BK);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
            TenantIds[r.GetString(0)] = r.GetGuid(1);
    }

    public static async Task RunAsync()
    {
        AnsiConsole.Write(new Rule("[bold yellow]Points Expiration Job — Verify[/]").RuleStyle("grey"));
        AnsiConsole.MarkupLine("[grey]Prod job (dEngage.Loyalty.Ledger.PointsExpirationJob) is tested across 6 scenarios.[/]\n");

        // Set up DI — same stack as prod
        var services = new ServiceCollection();
        services.AddLogging(b => b.AddConsole().SetMinimumLevel(LogLevel.Warning));
        services.AddDbContext<LoyaltyDbContext>(opts => opts.UseNpgsql(PG));
        services.AddScoped<LedgerService>();
        services.AddScoped<PointsExpirationJob>();
        var sp = services.BuildServiceProvider();

        await LoadTenantIdsAsync();
        await ResetAsync();

        await Scenario1_SimpleExpireAsync(sp);
        await Scenario2_FifoPartialConsumeAsync(sp);
        await Scenario3_CutoffProtectsNewEarnAsync(sp);
        await Scenario4_MultiTenantDifferentCutoffAsync(sp);
        await Scenario5_IdempotencySameDayAsync(sp);
        await Scenario6_CashAccountNotAffectedAsync(sp);

        AnsiConsole.Write(new Rule("[grey]Result[/]").RuleStyle("grey"));
        var color = _fail == 0 ? "green" : "red";
        AnsiConsole.MarkupLine($"[{color}]PASSED: {_pass}[/]  [red]FAILED: {_fail}[/]");
    }

    // ── Scenario 1: old earn never consumed → expires entirely ──────────────
    static async Task Scenario1_SimpleExpireAsync(IServiceProvider sp)
    {
        AnsiConsole.MarkupLine("\n[bold cyan]Scenario 1[/] — Simple expire: old, unconsumed earn → all of it must expire");

        await ResetAsync();
        await SetupAccountAsync("s1_user", SBUX, SbuxStars);
        await InsertEarnAsync(SBUX, "s1_user", SbuxStars, 500m, daysAgo: 400);
        await SetBalanceAsync(SBUX, "s1_user", SbuxStars, 500m);

        var (beforeBalance, _) = await QueryAsync(SBUX, "s1_user", SbuxStars);
        Check("balance before = 500", 500m, beforeBalance);

        await RunJobAsync(sp);

        var (afterBalance, expired) = await QueryAsync(SBUX, "s1_user", SbuxStars);
        Check("balance after = 0", 0m, afterBalance);
        Check("expired entry = -500", -500m, expired);
    }

    // ── Scenario 2: old earn + old redeem → the remainder expires ────────────
    static async Task Scenario2_FifoPartialConsumeAsync(IServiceProvider sp)
    {
        AnsiConsole.MarkupLine("\n[bold cyan]Scenario 2[/] — FIFO partial: old earn=500, old redeem=200 → expire=300");

        await ResetAsync();
        await SetupAccountAsync("s2_user", SBUX, SbuxStars);
        await InsertEarnAsync(SBUX, "s2_user", SbuxStars, 500m, daysAgo: 400);
        await InsertRedeemAsync(SBUX, "s2_user", SbuxStars, 200m, daysAgo: 380);
        await SetBalanceAsync(SBUX, "s2_user", SbuxStars, 300m);

        await RunJobAsync(sp);

        var (afterBalance, expired) = await QueryAsync(SBUX, "s2_user", SbuxStars);
        Check("balance after = 0", 0m, afterBalance);
        Check("expired = -300 (FIFO)", -300m, expired);
    }

    // ── Scenario 3: old earn + new earn + new redeem
    //    The new redeem consumed the old earn (FIFO); the new earn was untouched
    static async Task Scenario3_CutoffProtectsNewEarnAsync(IServiceProvider sp)
    {
        AnsiConsole.MarkupLine("\n[bold cyan]Scenario 3[/] — CRITICAL FIFO: old 100 + new 50 + new redeem 30");
        AnsiConsole.MarkupLine("[grey]  Correct: the new redeem was consumed from the old earn → 70 remains of the old earn, which expires[/]");
        AnsiConsole.MarkupLine("[grey]  Wrong: also counting the new earn → 100-30=70 is the same number but different semantics[/]");

        await ResetAsync();
        await SetupAccountAsync("s3_user", SBUX, SbuxStars);
        await InsertEarnAsync(SBUX,  "s3_user", SbuxStars, 100m, daysAgo: 400); // before cutoff
        await InsertEarnAsync(SBUX,  "s3_user", SbuxStars,  50m, daysAgo: 100); // AFTER cutoff
        await InsertRedeemAsync(SBUX, "s3_user", SbuxStars, 30m, daysAgo: 50);  // AFTER cutoff
        await SetBalanceAsync(SBUX, "s3_user", SbuxStars, 120m);

        await RunJobAsync(sp);

        var (afterBalance, expired) = await QueryAsync(SBUX, "s3_user", SbuxStars);
        Check("expired = -70 (old 100 - 30 redeem = 70)", -70m, expired);
        Check("balance after = 50 (new earn preserved)", 50m, afterBalance);
    }

    // ── Scenario 4: multi-tenant with different expiration_days ────────────
    // Starbucks: 365 days, entry from 400 days ago → expires
    // Burger King: 180 days, entry from 200 days ago → expires
    // But if the Burger King entry is from 100 days ago → protected
    static async Task Scenario4_MultiTenantDifferentCutoffAsync(IServiceProvider sp)
    {
        AnsiConsole.MarkupLine("\n[bold cyan]Scenario 4[/] — Multi-tenant: Starbucks 365d, Burger King 180d different cutoffs");

        await ResetAsync();

        // Starbucks: 400 days ago → expires (365 cutoff)
        await SetupAccountAsync("sbux_s4", SBUX, SbuxStars);
        await InsertEarnAsync(SBUX, "sbux_s4", SbuxStars, 200m, daysAgo: 400);
        await SetBalanceAsync(SBUX, "sbux_s4", SbuxStars, 200m);

        // Burger King: 200 days ago → expires (180 cutoff)
        await SetupAccountAsync("bk_s4a", BK, BkCrown);
        await InsertEarnAsync(BK, "bk_s4a", BkCrown, 300m, daysAgo: 200);
        await SetBalanceAsync(BK, "bk_s4a", BkCrown, 300m);

        // Burger King: 100 days ago → PROTECTED (within the 180 cutoff)
        await SetupAccountAsync("bk_s4b", BK, BkCrown);
        await InsertEarnAsync(BK, "bk_s4b", BkCrown, 400m, daysAgo: 100);
        await SetBalanceAsync(BK, "bk_s4b", BkCrown, 400m);

        await RunJobAsync(sp);

        var (sbuxBal, sbuxExp) = await QueryAsync(SBUX, "sbux_s4", SbuxStars);
        Check("Starbucks 400d → expired", -200m, sbuxExp);
        Check("Starbucks balance = 0",     0m,   sbuxBal);

        var (bk1Bal, bk1Exp) = await QueryAsync(BK, "bk_s4a", BkCrown);
        Check("BK 200d → expired",  -300m, bk1Exp);
        Check("BK balance = 0",     0m,    bk1Bal);

        var (bk2Bal, bk2Exp) = await QueryAsync(BK, "bk_s4b", BkCrown);
        Check("BK 100d → protected, expired = 0", 0m, bk2Exp);
        Check("BK balance = 400 (untouched)",     400m, bk2Bal);
    }

    // ── Scenario 5: 2nd run on the same day is idempotent ──────────────────
    static async Task Scenario5_IdempotencySameDayAsync(IServiceProvider sp)
    {
        AnsiConsole.MarkupLine("\n[bold cyan]Scenario 5[/] — Idempotency: a 2nd run on the same day must not expire again");

        await ResetAsync();
        await SetupAccountAsync("s5_user", SBUX, SbuxStars);
        await InsertEarnAsync(SBUX, "s5_user", SbuxStars, 300m, daysAgo: 400);
        await SetBalanceAsync(SBUX, "s5_user", SbuxStars, 300m);

        await RunJobAsync(sp);
        var (bal1, exp1) = await QueryAsync(SBUX, "s5_user", SbuxStars);
        Check("run 1: balance = 0", 0m, bal1);
        Check("run 1: expired = -300", -300m, exp1);

        await RunJobAsync(sp);
        var (bal2, exp2) = await QueryAsync(SBUX, "s5_user", SbuxStars);
        Check("run 2: balance still 0 (no re-expiry)", 0m, bal2);
        Check("run 2: expired still -300 (no new entry)",  -300m, exp2);

        // How many 'points_expired' entries are in the ledger? Must be exactly 1.
        await using var db = new NpgsqlConnection(PG);
        await db.OpenAsync();
        await using var cnt = new NpgsqlCommand(
            "SELECT COUNT(*) FROM ledger_entries WHERE contact_key='s5_user' AND reason='points_expired'", db);
        var count = (long)(await cnt.ExecuteScalarAsync())!;
        Check("single expire entry in the ledger", 1L, count);
    }

    // ── Scenario 6: CASH account must never be touched ─────────────────────
    static async Task Scenario6_CashAccountNotAffectedAsync(IServiceProvider sp)
    {
        AnsiConsole.MarkupLine("\n[bold cyan]Scenario 6[/] — Cash account (no expiration_days) must not be touched");

        await ResetAsync();
        await SetupAccountAsync("s6_user", SBUX, SbuxCard);

        // We use the "cash_load" reason for cash — the expire job considers earn/refund/redeemed
        // reasons, but it must skip the cash account via the at.type != 'POINTS' guard.
        await using (var db = new NpgsqlConnection(PG))
        {
            await db.OpenAsync();
            await using var acc = new NpgsqlCommand(
                "SELECT id FROM customer_accounts WHERE tenant_id=@t AND contact_key=@c AND account_type_id=@at", db);
            acc.Parameters.AddWithValue("t", TenantIds[SBUX]);
            acc.Parameters.AddWithValue("c", "s6_user");
            acc.Parameters.AddWithValue("at", SbuxCard);
            var accountId = (Guid)(await acc.ExecuteScalarAsync())!;

            await using var ins = new NpgsqlCommand("""
                INSERT INTO ledger_entries (id, tenant_id, customer_account_id, contact_key, delta, reason,
                                            source_event_id, idempotency_key, metadata, created_at)
                VALUES (gen_random_uuid(), @t, @acct, @c, 500, 'cash_load', 'seed', 'seed:s6', '{}', NOW() - INTERVAL '400 days')
                """, db);
            ins.Parameters.AddWithValue("t", SBUX);
            ins.Parameters.AddWithValue("acct", accountId);
            ins.Parameters.AddWithValue("c", "s6_user");
            await ins.ExecuteNonQueryAsync();

            await using var upd = new NpgsqlCommand(
                "UPDATE customer_accounts SET balance = 500 WHERE id = @id", db);
            upd.Parameters.AddWithValue("id", accountId);
            await upd.ExecuteNonQueryAsync();
        }

        await RunJobAsync(sp);

        // Cash balance = 500, must stay. No 'points_expired' entry in the ledger.
        await using var qdb = new NpgsqlConnection(PG);
        await qdb.OpenAsync();
        await using var q = new NpgsqlCommand("""
            SELECT balance FROM customer_accounts
            WHERE tenant_id=@t AND contact_key=@c AND account_type_id=@at
            """, qdb);
        q.Parameters.AddWithValue("t", TenantIds[SBUX]);
        q.Parameters.AddWithValue("c", "s6_user");
        q.Parameters.AddWithValue("at", SbuxCard);
        var bal = (decimal)(await q.ExecuteScalarAsync())!;
        Check("Cash balance untouched = 500", 500m, bal);

        await using var expQ = new NpgsqlCommand(
            "SELECT COUNT(*) FROM ledger_entries WHERE contact_key='s6_user' AND reason='points_expired'", qdb);
        var expCount = (long)(await expQ.ExecuteScalarAsync())!;
        Check("no expire entry at all for the cash account", 0L, expCount);
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    static async Task RunJobAsync(IServiceProvider sp)
    {
        using var scope = sp.CreateScope();
        var job = scope.ServiceProvider.GetRequiredService<PointsExpirationJob>();
        await job.RunAsync();
    }

    static async Task ResetAsync()
    {
        await using var db = new NpgsqlConnection(PG);
        await db.OpenAsync();
        foreach (var sql in new[]
        {
            "DELETE FROM ledger_entries WHERE contact_key LIKE 's%_user' OR contact_key LIKE 'sbux_s%' OR contact_key LIKE 'bk_s%'",
            "DELETE FROM customer_accounts WHERE contact_key LIKE 's%_user' OR contact_key LIKE 'sbux_s%' OR contact_key LIKE 'bk_s%'"
        })
        {
            await using var cmd = new NpgsqlCommand(sql, db);
            await cmd.ExecuteNonQueryAsync();
        }
    }

    static async Task SetupAccountAsync(string contact, string tenant, Guid accountTypeId)
    {
        await using var db = new NpgsqlConnection(PG);
        await db.OpenAsync();
        await using var cmd = new NpgsqlCommand("""
            INSERT INTO customer_accounts (id, tenant_id, contact_key, account_type_id, balance, updated_at)
            VALUES (gen_random_uuid(), @t, @c, @at, 0, NOW())
            ON CONFLICT (tenant_id, contact_key, account_type_id) DO NOTHING
            """, db);
        cmd.Parameters.AddWithValue("t", TenantIds[tenant]);
        cmd.Parameters.AddWithValue("c", contact);
        cmd.Parameters.AddWithValue("at", accountTypeId);
        await cmd.ExecuteNonQueryAsync();
    }

    static async Task InsertEarnAsync(string tenant, string contact, Guid accountTypeId, decimal amount, int daysAgo)
    {
        await using var db = new NpgsqlConnection(PG);
        await db.OpenAsync();
        var acctId = await GetAccountIdAsync(db, tenant, contact, accountTypeId);
        var key = $"seed-earn:{acctId}:{daysAgo}d:{amount}";
        await using var cmd = new NpgsqlCommand("""
            INSERT INTO ledger_entries (id, tenant_id, customer_account_id, contact_key, delta, reason,
                                        source_event_id, idempotency_key, metadata, created_at)
            VALUES (gen_random_uuid(), @t, @acct, @c, @delta, 'earn', @src, @idem, '{}', NOW() - (@days || ' days')::interval)
            """, db);
        cmd.Parameters.AddWithValue("t",     tenant);
        cmd.Parameters.AddWithValue("acct",  acctId);
        cmd.Parameters.AddWithValue("c",     contact);
        cmd.Parameters.AddWithValue("delta", amount);
        cmd.Parameters.AddWithValue("src",   key);
        cmd.Parameters.AddWithValue("idem",  key);
        cmd.Parameters.AddWithValue("days",  daysAgo.ToString());
        await cmd.ExecuteNonQueryAsync();
    }

    static async Task InsertRedeemAsync(string tenant, string contact, Guid accountTypeId, decimal amount, int daysAgo)
    {
        await using var db = new NpgsqlConnection(PG);
        await db.OpenAsync();
        var acctId = await GetAccountIdAsync(db, tenant, contact, accountTypeId);
        var key = $"seed-redeem:{acctId}:{daysAgo}d:{amount}";
        await using var cmd = new NpgsqlCommand("""
            INSERT INTO ledger_entries (id, tenant_id, customer_account_id, contact_key, delta, reason,
                                        source_event_id, idempotency_key, metadata, created_at)
            VALUES (gen_random_uuid(), @t, @acct, @c, @delta, 'points_redeemed', @src, @idem, '{}', NOW() - (@days || ' days')::interval)
            """, db);
        cmd.Parameters.AddWithValue("t",     tenant);
        cmd.Parameters.AddWithValue("acct",  acctId);
        cmd.Parameters.AddWithValue("c",     contact);
        cmd.Parameters.AddWithValue("delta", -amount);
        cmd.Parameters.AddWithValue("src",   key);
        cmd.Parameters.AddWithValue("idem",  key);
        cmd.Parameters.AddWithValue("days",  daysAgo.ToString());
        await cmd.ExecuteNonQueryAsync();
    }

    static async Task SetBalanceAsync(string tenant, string contact, Guid accountTypeId, decimal balance)
    {
        await using var db = new NpgsqlConnection(PG);
        await db.OpenAsync();
        await using var cmd = new NpgsqlCommand("""
            UPDATE customer_accounts SET balance = @b, updated_at = NOW()
            WHERE tenant_id=@t AND contact_key=@c AND account_type_id=@at
            """, db);
        cmd.Parameters.AddWithValue("b",  balance);
        cmd.Parameters.AddWithValue("t",  TenantIds[tenant]);
        cmd.Parameters.AddWithValue("c",  contact);
        cmd.Parameters.AddWithValue("at", accountTypeId);
        await cmd.ExecuteNonQueryAsync();
    }

    static async Task<Guid> GetAccountIdAsync(NpgsqlConnection db, string tenant, string contact, Guid accountTypeId)
    {
        await using var cmd = new NpgsqlCommand(
            "SELECT id FROM customer_accounts WHERE tenant_id=@t AND contact_key=@c AND account_type_id=@at", db);
        cmd.Parameters.AddWithValue("t",  TenantIds[tenant]);
        cmd.Parameters.AddWithValue("c",  contact);
        cmd.Parameters.AddWithValue("at", accountTypeId);
        return (Guid)(await cmd.ExecuteScalarAsync())!;
    }

    // Return balance + total delta of expire entries
    static async Task<(decimal balance, decimal expired)> QueryAsync(string tenant, string contact, Guid accountTypeId)
    {
        await using var db = new NpgsqlConnection(PG);
        await db.OpenAsync();

        await using var balCmd = new NpgsqlCommand("""
            SELECT balance FROM customer_accounts
            WHERE tenant_id=@t AND contact_key=@c AND account_type_id=@at
            """, db);
        balCmd.Parameters.AddWithValue("t",  TenantIds[tenant]);
        balCmd.Parameters.AddWithValue("c",  contact);
        balCmd.Parameters.AddWithValue("at", accountTypeId);
        var bal = (decimal?)(await balCmd.ExecuteScalarAsync()) ?? 0m;

        await using var expCmd = new NpgsqlCommand("""
            SELECT COALESCE(SUM(delta), 0) FROM ledger_entries
            WHERE tenant_id=@t AND contact_key=@c AND reason='points_expired'
              AND customer_account_id IN (
                  SELECT id FROM customer_accounts
                  WHERE tenant_id=@tg AND contact_key=@c AND account_type_id=@at
              )
            """, db);
        expCmd.Parameters.AddWithValue("t",  tenant);
        expCmd.Parameters.AddWithValue("tg", TenantIds[tenant]);
        expCmd.Parameters.AddWithValue("c",  contact);
        expCmd.Parameters.AddWithValue("at", accountTypeId);
        var exp = (decimal?)(await expCmd.ExecuteScalarAsync()) ?? 0m;

        return (bal, exp);
    }

    static void Check<T>(string label, T expected, T actual) where T : IEquatable<T>
    {
        if (expected.Equals(actual))
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
