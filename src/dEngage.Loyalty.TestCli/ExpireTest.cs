using Npgsql;
using Spectre.Console;

/// <summary>
/// Demonstrates the points-expire scenario live.
///
/// Scenario:
///   Customer named cust_expire:
///     [400 days ago]  +600 ★  earn  (expire zone — cutoff=365 days ago)
///     [300 days ago]  −200 ★  points_redeemed  (FIFO: 200 comes out of the old points)
///     [200 days ago]  +300 ★  earn  (fresh — does not expire)
///     [100 days ago]  +100 ★  earn  (fresh — does not expire)
///
///   Starting balance = 600 - 200 + 300 + 100 = 800 ★
///
///   FIFO math:
///     Earned before cutoff     = 600
///     Total spent              = 200
///     Expirable                = 600 - 200 = 400
///     → 400 ★ is expired
///
///   Final balance = 800 - 400 = 400 ★
/// </summary>
public static class ExpireTest
{
    const string PG     = "Host=localhost;Database=loyalty_dev;Username=postgres;Password=postgres";
    const string TENANT = "starbucks";

    // Stars account type ID from the Starbucks seed
    const string STARS_ACCOUNT_TYPE_ID = "018fcd02-0000-7000-8000-000000000001";
    const string CONTACT                = "cust_expire";

    static string _tid = "";

    static async Task EnsureTenantIdAsync(NpgsqlConnection db)
    {
        if (_tid != "") return;
        await using var cmd = new NpgsqlCommand($"SELECT id::text FROM tenants WHERE slug='{TENANT}'", db);
        _tid = (string)(await cmd.ExecuteScalarAsync())!;
    }

    public static async Task RunJobOnlyAsync()
    {
        AnsiConsole.Write(new Rule("[bold yellow]PointsExpirationJob — Live Data[/]").RuleStyle("grey"));

        await using var db = new NpgsqlConnection(PG);
        await db.OpenAsync();
        await EnsureTenantIdAsync(db);

        // How many customers are candidates?
        await using var countCmd = new NpgsqlCommand("""
            SELECT COUNT(DISTINCT ca.id)
            FROM customer_accounts ca
            JOIN account_types at ON at.id = ca.account_type_id
            WHERE at.type = 'POINTS'
              AND (at.config->>'expiration_days') IS NOT NULL
              AND ca.balance > 0
              AND EXISTS (
                  SELECT 1 FROM ledger_entries le
                  WHERE le.customer_account_id = ca.id
                    AND le.reason IN ('earn','transfer_in')
                    AND le.created_at <= NOW() - ((at.config->>'expiration_days')::int || ' days')::interval
              )
            """, db);
        var candidates = (long)(await countCmd.ExecuteScalarAsync() ?? 0L);

        AnsiConsole.MarkupLine($"Expire-candidate customer count: [yellow]{candidates}[/]");

        if (candidates == 0)
        {
            AnsiConsole.MarkupLine("[green]✓ No one to expire — expected result.[/]");
            return;
        }

        var (customers, points) = await RunExpireAllAsync(db);
        AnsiConsole.MarkupLine($"[green]✓ Expire complete: {customers} customers, {points:F0} ★[/]");
    }

    public static async Task RunAsync()
    {
        AnsiConsole.Write(new Rule("[bold yellow]Points Expire Scenario[/]").RuleStyle("grey"));
        AnsiConsole.MarkupLine("""
[grey]Customer: cust_expire
  400 days ago: +600 ★  (expire zone)
  300 days ago: −200 ★  points_redeemed
  200 days ago: +300 ★  (fresh)
  100 days ago: +100 ★  (fresh)
  Expected starting balance : 800 ★
  FIFO expire calculation   : 600 (old earn) − 200 (spend) = 400 expire
  Expected final balance    : 400 ★[/]
""");

        await using var db = new NpgsqlConnection(PG);
        await db.OpenAsync();
        await EnsureTenantIdAsync(db);

        // 1. Clean up this test user first
        await CleanupAsync(db);

        // 2. Add expiration_days = 365 (if not already present)
        await SetExpirationDaysAsync(db, 365);

        // 3. Create the customer_account
        var accountId = await EnsureCustomerAccountAsync(db);

        // 4. Create backdated ledger entries
        await SeedLedgerEntriesAsync(db, accountId);

        // 5. Compute the balance from the current ledger and update it
        await RecalcBalanceAsync(db, accountId);

        AnsiConsole.MarkupLine("[bold]── BEFORE ─────────────────────────────────────[/]");
        await ShowLedgerAsync(db);
        await ShowBalanceAsync(db);

        // 6. Run the expire job
        AnsiConsole.MarkupLine("\n[bold yellow]⚙  Running PointsExpirationJob...[/]");
        var expiredPts = await RunExpireJobAsync(db, accountId);

        AnsiConsole.MarkupLine($"[green]✓ Expire complete: {expiredPts:F0} ★ expired[/]\n");

        AnsiConsole.MarkupLine("[bold]── AFTER ──────────────────────────────────────[/]");
        await ShowLedgerAsync(db);
        await ShowBalanceAsync(db);

        AnsiConsole.Write(new Rule("[grey]Scenario complete[/]").RuleStyle("grey"));
    }

    // ─────────────────────────────────────────────────────────────────────────

    static async Task CleanupAsync(NpgsqlConnection db)
    {
        foreach (var sql in new[]
        {
            $"DELETE FROM ledger_entries WHERE tenant_id='{TENANT}' AND contact_key='{CONTACT}'",
            $"DELETE FROM customer_accounts WHERE tenant_id='{_tid}' AND contact_key='{CONTACT}'"
        })
        {
            await using var cmd = new NpgsqlCommand(sql, db);
            await cmd.ExecuteNonQueryAsync();
        }
        AnsiConsole.MarkupLine("[grey]Test user cleaned up.[/]");
    }

    static async Task SetExpirationDaysAsync(NpgsqlConnection db, int days)
    {
        const string sql = """
            UPDATE account_types
            SET config = config || jsonb_build_object('expiration_days', @days)
            WHERE id = @id AND tenant_id = @t
            """;
        await using var cmd = new NpgsqlCommand(sql, db);
        cmd.Parameters.AddWithValue("days", days);
        cmd.Parameters.AddWithValue("id", Guid.Parse(STARS_ACCOUNT_TYPE_ID));
        cmd.Parameters.AddWithValue("t", Guid.Parse(_tid));
        await cmd.ExecuteNonQueryAsync();
        AnsiConsole.MarkupLine($"[grey]Stars config: expiration_days={days} set.[/]");
    }

    static async Task<Guid> EnsureCustomerAccountAsync(NpgsqlConnection db)
    {
        var id = Guid.NewGuid();
        const string sql = """
            INSERT INTO customer_accounts
                (id, tenant_id, contact_key, account_type_id, balance, updated_at)
            VALUES
                (@id, @t, @c, @at, 0, NOW())
            ON CONFLICT (tenant_id, contact_key, account_type_id) DO NOTHING
            RETURNING id
            """;
        await using var cmd = new NpgsqlCommand(sql, db);
        cmd.Parameters.AddWithValue("id", id);
        cmd.Parameters.AddWithValue("t", Guid.Parse(_tid));
        cmd.Parameters.AddWithValue("c", CONTACT);
        cmd.Parameters.AddWithValue("at", Guid.Parse(STARS_ACCOUNT_TYPE_ID));

        var result = await cmd.ExecuteScalarAsync();
        if (result is Guid returnedId) return returnedId;

        // ON CONFLICT — fetch existing
        const string fetch = """
            SELECT id FROM customer_accounts
            WHERE tenant_id=@t AND contact_key=@c AND account_type_id=@at
            """;
        await using var cmd2 = new NpgsqlCommand(fetch, db);
        cmd2.Parameters.AddWithValue("t", Guid.Parse(_tid));
        cmd2.Parameters.AddWithValue("c", CONTACT);
        cmd2.Parameters.AddWithValue("at", Guid.Parse(STARS_ACCOUNT_TYPE_ID));
        return (Guid)(await cmd2.ExecuteScalarAsync())!;
    }

    static async Task SeedLedgerEntriesAsync(NpgsqlConnection db, Guid accountId)
    {
        var now = DateTime.UtcNow;
        var entries = new[]
        {
            // (delta, reason, days_ago, note)
            ( 600m, "earn",             400, "Old earn — expire zone"),
            (-200m, "points_redeemed",  300, "Spend — FIFO: comes out of the old points"),
            ( 300m, "earn",             200, "Fresh earn — does not expire"),
            ( 100m, "earn",             100, "Fresh earn — does not expire"),
        };

        foreach (var (delta, reason, daysAgo, note) in entries)
        {
            var ts = now.AddDays(-daysAgo);
            var key = $"seed:{accountId}:{reason}:{daysAgo}d";
            const string sql = """
                INSERT INTO ledger_entries
                    (id, tenant_id, customer_account_id, contact_key, delta, reason,
                     source_event_id, idempotency_key, metadata, created_at)
                VALUES
                    (gen_random_uuid(), @t, @acct, @c, @delta, @reason,
                     @src, @idem, '{}', @ts)
                ON CONFLICT (tenant_id, idempotency_key) DO NOTHING
                """;
            await using var cmd = new NpgsqlCommand(sql, db);
            cmd.Parameters.AddWithValue("t", TENANT);
            cmd.Parameters.AddWithValue("acct", accountId);
            cmd.Parameters.AddWithValue("c", CONTACT);
            cmd.Parameters.AddWithValue("delta", delta);
            cmd.Parameters.AddWithValue("reason", reason);
            cmd.Parameters.AddWithValue("src", key);
            cmd.Parameters.AddWithValue("idem", key);
            cmd.Parameters.AddWithValue("ts", ts);
            await cmd.ExecuteNonQueryAsync();

            var sign = delta >= 0 ? "+" : "";
            AnsiConsole.MarkupLine($"[grey]  Ledger seeded: {sign}{delta:F0} ★  {reason}  ({daysAgo} days ago) — {note}[/]");
        }
    }

    static async Task RecalcBalanceAsync(NpgsqlConnection db, Guid accountId)
    {
        const string sql = """
            UPDATE customer_accounts
            SET balance = (
                SELECT COALESCE(SUM(delta), 0)
                FROM ledger_entries
                WHERE customer_account_id = @id
            ),
            updated_at = NOW()
            WHERE id = @id
            """;
        await using var cmd = new NpgsqlCommand(sql, db);
        cmd.Parameters.AddWithValue("id", accountId);
        await cmd.ExecuteNonQueryAsync();
    }

    // ─── Whole DB — set-based expire (same logic as the job) ────────────────

    static async Task<(long customers, decimal points)> RunExpireAllAsync(NpgsqlConnection db)
    {
        var today    = DateOnly.FromDateTime(DateTime.UtcNow);
        var todayStr = today.ToString("yyyy-MM-dd");

        // Write the expire ledger entries (INSERT ... SELECT, FIFO math inside the SQL)
        await using var ins = new NpgsqlCommand("""
            INSERT INTO ledger_entries (
                id, tenant_id, customer_account_id, contact_key,
                delta, reason, source_event_id, idempotency_key, metadata, created_at
            )
            SELECT
                gen_random_uuid(),
                tn.slug,
                ca.id,
                ca.contact_key,
                -LEAST(
                    GREATEST(0, COALESCE(e.earned, 0) - COALESCE(c.consumed, 0)),
                    ca.balance
                ),
                'points_expired',
                'expire:' || ca.id::text || ':' || @today,
                'expire:' || ca.id::text || ':' || @today,
                jsonb_build_object(
                    'account_type_id', ca.account_type_id::text,
                    'expiration_days', (at.config->>'expiration_days')::int,
                    'cutoff_date',     (NOW() - ((at.config->>'expiration_days')::int || ' days')::interval)::text
                ),
                NOW()
            FROM customer_accounts ca
            JOIN account_types at ON at.id = ca.account_type_id
            JOIN tenants tn ON tn.id = ca.tenant_id
            CROSS JOIN LATERAL (
                SELECT COALESCE(SUM(delta), 0) AS earned
                FROM ledger_entries
                WHERE customer_account_id = ca.id
                  AND reason IN ('earn','transfer_in')
                  AND created_at <= NOW() - ((at.config->>'expiration_days')::int || ' days')::interval
            ) e
            CROSS JOIN LATERAL (
                SELECT ABS(COALESCE(SUM(delta), 0)) AS consumed
                FROM ledger_entries
                WHERE customer_account_id = ca.id
                  AND reason IN ('points_redeemed','points_redeemed_cash','refund','points_expired','reward_purchase','transfer_out')
            ) c
            WHERE at.type = 'POINTS'
              AND (at.config->>'expiration_days') IS NOT NULL
              AND ca.balance > 0
              AND GREATEST(0, COALESCE(e.earned, 0) - COALESCE(c.consumed, 0)) > 0
              AND NOT EXISTS (
                  SELECT 1 FROM ledger_entries ex
                  WHERE ex.tenant_id       = tn.slug
                    AND ex.idempotency_key = 'expire:' || ca.id::text || ':' || @today
              )
            """, db);
        ins.Parameters.AddWithValue("today", todayStr);
        await ins.ExecuteNonQueryAsync();

        // Update the balance
        await using var upd = new NpgsqlCommand("""
            UPDATE customer_accounts ca
            SET balance    = ca.balance + le.delta,
                updated_at = NOW()
            FROM ledger_entries le
            WHERE le.customer_account_id = ca.id
              AND le.idempotency_key = 'expire:' || ca.id::text || ':' || @today
              AND le.reason = 'points_expired'
            """, db);
        upd.Parameters.AddWithValue("today", todayStr);
        await upd.ExecuteNonQueryAsync();

        // Summary
        await using var sum = new NpgsqlCommand("""
            SELECT COUNT(*), ABS(COALESCE(SUM(delta), 0))
            FROM ledger_entries
            WHERE reason = 'points_expired'
              AND idempotency_key LIKE 'expire:%:' || @today
            """, db);
        sum.Parameters.AddWithValue("today", todayStr);
        await using var r = await sum.ExecuteReaderAsync();
        await r.ReadAsync();
        return (r.GetInt64(0), r.GetDecimal(1));
    }

    // ─── Expire job (raw SQL, same FIFO algorithm) ───────────────────────────

    static async Task<decimal> RunExpireJobAsync(NpgsqlConnection db, Guid accountId)
    {
        var today  = DateOnly.FromDateTime(DateTime.UtcNow);
        var cutoff = DateTime.UtcNow.AddDays(-365);

        var idempotencyKey = $"expire:{accountId}:{today:yyyy-MM-dd}";

        // Idempotency: has it already run today?
        await using (var chk = new NpgsqlCommand(
            "SELECT 1 FROM ledger_entries WHERE tenant_id=@t AND idempotency_key=@k LIMIT 1", db))
        {
            chk.Parameters.AddWithValue("t", TENANT);
            chk.Parameters.AddWithValue("k", idempotencyKey);
            var exists = await chk.ExecuteScalarAsync();
            if (exists is not null)
            {
                AnsiConsole.MarkupLine("[yellow]Expire already ran today (idempotency_key exists), skipping.[/]");
                return 0;
            }
        }

        // Current balance
        decimal balance;
        await using (var bq = new NpgsqlCommand(
            "SELECT balance FROM customer_accounts WHERE id=@id", db))
        {
            bq.Parameters.AddWithValue("id", accountId);
            balance = (decimal)(await bq.ExecuteScalarAsync())!;
        }

        if (balance <= 0) return 0;

        // FIFO math
        decimal totalEarned;
        await using (var eq = new NpgsqlCommand("""
            SELECT COALESCE(SUM(delta), 0) FROM ledger_entries
            WHERE customer_account_id=@id AND reason IN ('earn','transfer_in') AND created_at <= @cutoff
            """, db))
        {
            eq.Parameters.AddWithValue("id", accountId);
            eq.Parameters.AddWithValue("cutoff", cutoff);
            totalEarned = (decimal)(await eq.ExecuteScalarAsync())!;
        }

        decimal spentAndExpired;
        await using (var sq = new NpgsqlCommand("""
            SELECT COALESCE(SUM(delta), 0) FROM ledger_entries
            WHERE customer_account_id=@id
              AND reason IN ('points_redeemed','points_redeemed_cash','refund','points_expired','reward_purchase','transfer_out')
            """, db))
        {
            sq.Parameters.AddWithValue("id", accountId);
            spentAndExpired = (decimal)(await sq.ExecuteScalarAsync())!;
        }

        var totalConsumed = Math.Abs(spentAndExpired);
        var expirable     = Math.Max(0, totalEarned - totalConsumed);
        var expireAmount  = Math.Min(expirable, balance);

        if (expireAmount <= 0)
        {
            AnsiConsole.MarkupLine("[grey]No points to expire.[/]");
            return 0;
        }

        AnsiConsole.MarkupLine($"[grey]  Earned before cutoff     : {totalEarned:F0} ★[/]");
        AnsiConsole.MarkupLine($"[grey]  Total spent/expired      : {totalConsumed:F0} ★[/]");
        AnsiConsole.MarkupLine($"[grey]  Expirable (FIFO)         : {expirable:F0} ★[/]");
        AnsiConsole.MarkupLine($"[grey]  Current balance          : {balance:F0} ★[/]");
        AnsiConsole.MarkupLine($"[yellow]  → To be expired          : {expireAmount:F0} ★[/]");

        // Write the ledger entry
        const string insertSql = """
            INSERT INTO ledger_entries
                (id, tenant_id, customer_account_id, contact_key, delta, reason,
                 source_event_id, idempotency_key, metadata, created_at)
            VALUES
                (gen_random_uuid(), @t, @acct, @c, @delta, 'points_expired',
                 @src, @idem,
                 jsonb_build_object(
                     'account_type_id', @at::text,
                     'expiration_days', 365,
                     'cutoff_date', @cutoff::text
                 ),
                 NOW())
            """;
        await using var ins = new NpgsqlCommand(insertSql, db);
        ins.Parameters.AddWithValue("t", TENANT);
        ins.Parameters.AddWithValue("acct", accountId);
        ins.Parameters.AddWithValue("c", CONTACT);
        ins.Parameters.AddWithValue("delta", -expireAmount);
        ins.Parameters.AddWithValue("src", idempotencyKey);
        ins.Parameters.AddWithValue("idem", idempotencyKey);
        ins.Parameters.AddWithValue("at", STARS_ACCOUNT_TYPE_ID);
        ins.Parameters.AddWithValue("cutoff", cutoff.ToString("O"));
        await ins.ExecuteNonQueryAsync();

        // Update the balance
        await using var upd = new NpgsqlCommand("""
            UPDATE customer_accounts
            SET balance = balance - @delta, updated_at = NOW()
            WHERE id = @id
            """, db);
        upd.Parameters.AddWithValue("delta", expireAmount);
        upd.Parameters.AddWithValue("id", accountId);
        await upd.ExecuteNonQueryAsync();

        return expireAmount;
    }

    // ─── Display ─────────────────────────────────────────────────────────────

    static async Task ShowLedgerAsync(NpgsqlConnection db)
    {
        const string sql = """
            SELECT created_at, reason, delta
            FROM ledger_entries
            WHERE tenant_id=@t AND contact_key=@c
            ORDER BY created_at
            """;
        await using var cmd = new NpgsqlCommand(sql, db);
        cmd.Parameters.AddWithValue("t", TENANT);
        cmd.Parameters.AddWithValue("c", CONTACT);
        await using var r = await cmd.ExecuteReaderAsync();

        var table = new Table()
            .Border(TableBorder.Simple)
            .Title($"[bold]Ledger — {CONTACT}[/]");
        table.AddColumn("Date");
        table.AddColumn("Reason");
        table.AddColumn(new TableColumn("Delta").RightAligned());

        while (await r.ReadAsync())
        {
            var ts     = r.GetDateTime(0).ToLocalTime().ToString("yyyy-MM-dd HH:mm");
            var reason = r.GetString(1);
            var delta  = r.GetDecimal(2);
            var color  = delta >= 0 ? "green" : reason == "points_expired" ? "orange1" : "red";
            var sign   = delta >= 0 ? "+" : "";
            var reasonMarkup = reason == "points_expired"
                ? $"[orange1]{reason}[/]"
                : $"[grey]{reason}[/]";
            table.AddRow($"[grey]{ts}[/]", reasonMarkup, $"[{color}]{sign}{delta:F0} ★[/]");
        }

        AnsiConsole.Write(table);
    }

    static async Task ShowBalanceAsync(NpgsqlConnection db)
    {
        const string sql = """
            SELECT ca.balance FROM customer_accounts ca
            WHERE ca.tenant_id=@t AND ca.contact_key=@c
            """;
        await using var cmd = new NpgsqlCommand(sql, db);
        cmd.Parameters.AddWithValue("t", Guid.Parse(_tid));
        cmd.Parameters.AddWithValue("c", CONTACT);
        var balance = (decimal?)await cmd.ExecuteScalarAsync() ?? 0m;

        var color = balance > 0 ? "green" : "red";
        AnsiConsole.MarkupLine($"  Balance: [{color}][bold]{balance:F0} ★[/][/]\n");
    }
}
