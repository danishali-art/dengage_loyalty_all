using dEngage.Loyalty.Ledger;
using dEngage.Loyalty.RuleEngine;
using dEngage.Loyalty.Schema;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;
using Spectre.Console;

/// <summary>
/// Tier jobs — live multi-tenant verification.
///
/// The prod code (TierDowngradeJob + TierEvaluationService) is run against the
/// seeded tier definitions of two tenants:
///   starbucks : yesil(0) / altin(1000) / siyah(5000) — qualifying 365d, grace 30d
///   burgerking: bronz(0, lifetime) / silver(500, 90d/0d) / gold(2000, 90d/7d)
///
/// All test contacts carry the 'tier_' prefix and are cleaned up at the start.
/// </summary>
public static class TierJobVerify
{
    const string PG   = "Host=localhost;Database=loyalty_dev;Username=postgres;Password=postgres";
    const string SBUX = "starbucks";
    const string BK   = "burgerking";

    static readonly Guid SbuxProgram = Guid.Parse("018fcd01-0000-7000-8000-000000000001");
    static readonly Guid SbuxStars   = Guid.Parse("018fcd02-0000-7000-8000-000000000001");
    static readonly Guid BkProgram   = Guid.Parse("018fce01-0000-7000-8000-000000000001");
    static readonly Guid BkCrown     = Guid.Parse("018fce02-0000-7000-8000-000000000001");

    static readonly Dictionary<string, Guid> Tiers = new(); // "starbucks/altin" → id
    static readonly Dictionary<string, Guid> TenantIds = new(); // "starbucks" → tenants.id
    static int _pass, _fail;

    public static async Task RunAsync()
    {
        AnsiConsole.Write(new Rule("[bold yellow]Tier Jobs — Multi-Tenant Verify[/]").RuleStyle("grey"));

        var services = new ServiceCollection();
        services.AddLogging(b => b.AddConsole().SetMinimumLevel(LogLevel.Warning));
        services.AddDbContext<LoyaltyDbContext>(opts => opts.UseNpgsql(PG));
        services.AddScoped<TierDowngradeJob>();
        services.AddScoped<ITierEvaluationService, TierEvaluationService>();
        services.AddScoped<IOutboxService, OutboxService>(); // TierEvaluationService dependency
        services.AddSingleton<TenantSlugCache>();
        services.AddScoped<ITenantSlugResolver, TenantSlugResolver>();
        var sp = services.BuildServiceProvider();

        await LoadTenantIdsAsync();
        await LoadTierIdsAsync();
        await ResetAsync();

        await S1_RealtimeUpgrade_PerTenantRules(sp);
        await S2_SameTierRequalify_PeriodResets(sp);
        await S3_Downgrade_ByPeriodPoints(sp);
        await S4_GraceStart_NoImmediateDowngrade(sp);
        await S5_GraceEarnings_DoNotCount(sp);
        await S6_TenantIsolation_SameContactKey(sp);
        await S7_LifetimeTier_NeverExpires(sp);

        AnsiConsole.Write(new Rule("[grey]Result[/]").RuleStyle("grey"));
        var color = _fail == 0 ? "green" : "red";
        AnsiConsole.MarkupLine($"[{color}]PASSED: {_pass}[/]  [red]FAILED: {_fail}[/]");
    }

    // ── S1: Real-time upgrade — same contact_key, same points, different tier per tenant ──
    static async Task S1_RealtimeUpgrade_PerTenantRules(IServiceProvider sp)
    {
        AnsiConsole.MarkupLine("\n[bold cyan]S1[/] — Realtime upgrade: same contact, same 1200 pts → SBUX altin(1000), BK silver(500)");
        await ResetAsync();

        await SetupAccountAsync(SBUX, "tier_mt", SbuxStars, balance: 1200);
        await SetupAccountAsync(BK,   "tier_mt", BkCrown,   balance: 1200);
        await InsertEarnAsync(SBUX, "tier_mt", SbuxStars, 1200m, daysAgo: 30);
        await InsertEarnAsync(BK,   "tier_mt", BkCrown,   1200m, daysAgo: 30);

        await EvaluateAsync(sp, SBUX, "tier_mt", SbuxProgram, SbuxStars);
        await EvaluateAsync(sp, BK,   "tier_mt", BkProgram,   BkCrown);

        var sbux = await GetTierStateAsync(SBUX, "tier_mt", SbuxStars);
        var bk   = await GetTierStateAsync(BK,   "tier_mt", BkCrown);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        Check("SBUX tier = altin",            Tiers[$"{SBUX}/altin"],  sbux.TierId ?? Guid.Empty);
        Check("BK tier = silver",             Tiers[$"{BK}/silver"],   bk.TierId ?? Guid.Empty);
        Check("SBUX period_start = today",    today, sbux.PeriodStart ?? default);
        Check("BK period_start = today",      today, bk.PeriodStart ?? default);
        Check("SBUX upgrade log = 1",         1L, await CountLogAsync(SBUX, "tier_mt"));
        Check("BK upgrade log = 1",           1L, await CountLogAsync(BK, "tier_mt"));
    }

    // ── S2: An account requalifying for the same tier MUST have its period RESET (bug fix) ──
    static async Task S2_SameTierRequalify_PeriodResets(IServiceProvider sp)
    {
        AnsiConsole.MarkupLine("\n[bold cyan]S2[/] — Same-tier requalify: stays altin but period resets (bug fix)");
        await ResetAsync();

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        await SetupAccountAsync(SBUX, "tier_s2", SbuxStars, balance: 1500,
            tierId: Tiers[$"{SBUX}/altin"], periodStartDaysAgo: 400, expiresDaysAgo: 5);
        await InsertEarnAsync(SBUX, "tier_s2", SbuxStars, 1500m, daysAgo: 350); // within period

        await RunDowngradeAsync(sp);

        var st = await GetTierStateAsync(SBUX, "tier_s2", SbuxStars);
        Check("tier still altin",             Tiers[$"{SBUX}/altin"], st.TierId ?? Guid.Empty);
        Check("period_start = today (reset)", today, st.PeriodStart ?? default);
        Check("tier_expires_at = NULL",       true,  st.ExpiresAt is null);
        Check("qualifying_pts = 0",           0m,    st.QualifyingPts);
        Check("no downgrade log (tier unchanged)", 0L,   await CountLogAsync(SBUX, "tier_s2"));

        // 2nd run: no longer in the expired set → nothing should change
        await RunDowngradeAsync(sp);
        var st2 = await GetTierStateAsync(SBUX, "tier_s2", SbuxStars);
        Check("2nd run: period_start unchanged", today, st2.PeriodStart ?? default);
        Check("2nd run: expires still NULL",     true,  st2.ExpiresAt is null);
    }

    // ── S3: Period points insufficient → drop one tier down + log ──
    static async Task S3_Downgrade_ByPeriodPoints(IServiceProvider sp)
    {
        AnsiConsole.MarkupLine("\n[bold cyan]S3[/] — Downgrade: siyah(5000) → period points 1200 → altin + log");
        await ResetAsync();

        await SetupAccountAsync(SBUX, "tier_s3", SbuxStars, balance: 1200,
            tierId: Tiers[$"{SBUX}/siyah"], periodStartDaysAgo: 400, expiresDaysAgo: 5);
        await InsertEarnAsync(SBUX, "tier_s3", SbuxStars, 1200m, daysAgo: 350);

        await RunDowngradeAsync(sp);

        var st = await GetTierStateAsync(SBUX, "tier_s3", SbuxStars);
        Check("tier = altin",                 Tiers[$"{SBUX}/altin"], st.TierId ?? Guid.Empty);
        Check("downgrade log = 1",            1L, await CountLogAsync(SBUX, "tier_s3"));

        var (from, to, pts) = await GetLastLogAsync(SBUX, "tier_s3");
        Check("log from = siyah",             Tiers[$"{SBUX}/siyah"], from ?? Guid.Empty);
        Check("log to = altin",               Tiers[$"{SBUX}/altin"], to);
        Check("log qualifying_pts = 1200",    1200m, pts);
    }

    // ── S4: Period ended but grace exists → tier kept, only expires_at is set ──
    static async Task S4_GraceStart_NoImmediateDowngrade(IServiceProvider sp)
    {
        AnsiConsole.MarkupLine("\n[bold cyan]S4[/] — Grace start: period ended 5d ago, grace 30d → tier preserved");
        await ResetAsync();

        await SetupAccountAsync(SBUX, "tier_s4", SbuxStars, balance: 100,
            tierId: Tiers[$"{SBUX}/altin"], periodStartDaysAgo: 370, expiresDaysAgo: null);

        await RunDowngradeAsync(sp);

        var st = await GetTierStateAsync(SBUX, "tier_s4", SbuxStars);
        var expectedExpiry = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-370 + 365 + 30); // +25 days

        Check("tier still altin",             Tiers[$"{SBUX}/altin"], st.TierId ?? Guid.Empty);
        Check("tier_expires_at = today+25",   expectedExpiry, st.ExpiresAt ?? default);
        Check("no downgrade log yet",         0L, await CountLogAsync(SBUX, "tier_s4"));
    }

    // ── S5: Points earned during grace DO NOT count toward requalification (window fix) ──
    static async Task S5_GraceEarnings_DoNotCount(IServiceProvider sp)
    {
        AnsiConsole.MarkupLine("\n[bold cyan]S5[/] — Grace points don't count: period 800 + grace 400 = 1200 but only 800 → yesil");
        await ResetAsync();

        await SetupAccountAsync(SBUX, "tier_s5", SbuxStars, balance: 1200,
            tierId: Tiers[$"{SBUX}/altin"], periodStartDaysAgo: 400, expiresDaysAgo: 5);
        await InsertEarnAsync(SBUX, "tier_s5", SbuxStars, 800m, daysAgo: 350); // within period
        await InsertEarnAsync(SBUX, "tier_s5", SbuxStars, 400m, daysAgo: 20);  // grace zone (period ended 35d ago)

        await RunDowngradeAsync(sp);

        var st = await GetTierStateAsync(SBUX, "tier_s5", SbuxStars);
        Check("tier = yesil (800 < 1000)",    Tiers[$"{SBUX}/yesil"], st.TierId ?? Guid.Empty);

        var (_, to, pts) = await GetLastLogAsync(SBUX, "tier_s5");
        Check("log qualifying_pts = 800 (excludes the 400 in grace)", 800m, pts);
        Check("log to = yesil",               Tiers[$"{SBUX}/yesil"], to);
    }

    // ── S6: Tenant isolation — SAME contact_key, opposite outcomes across two tenants ──
    static async Task S6_TenantIsolation_SameContactKey(IServiceProvider sp)
    {
        AnsiConsole.MarkupLine("\n[bold cyan]S6[/] — Isolation: same contact — SBUX 0 pts → drops; BK 600 pts → silver preserved");
        await ResetAsync();

        // SBUX: altin, period points 0 → must drop to yesil
        await SetupAccountAsync(SBUX, "tier_s6", SbuxStars, balance: 0,
            tierId: Tiers[$"{SBUX}/altin"], periodStartDaysAgo: 400, expiresDaysAgo: 5);

        // BK: silver (90d period), period points 600 ≥ 500 → silver kept + period reset
        await SetupAccountAsync(BK, "tier_s6", BkCrown, balance: 600,
            tierId: Tiers[$"{BK}/silver"], periodStartDaysAgo: 100, expiresDaysAgo: 2);
        await InsertEarnAsync(BK, "tier_s6", BkCrown, 600m, daysAgo: 50); // within BK window [100d, 10d)

        await RunDowngradeAsync(sp);

        var sbux  = await GetTierStateAsync(SBUX, "tier_s6", SbuxStars);
        var bk    = await GetTierStateAsync(BK,   "tier_s6", BkCrown);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        Check("SBUX dropped to yesil (BK's points didn't leak)", Tiers[$"{SBUX}/yesil"], sbux.TierId ?? Guid.Empty);
        Check("BK silver preserved (SBUX's 0 didn't leak)", Tiers[$"{BK}/silver"], bk.TierId ?? Guid.Empty);
        Check("BK period reset = today",       today, bk.PeriodStart ?? default);
        Check("SBUX downgrade log = 1",        1L, await CountLogAsync(SBUX, "tier_s6"));
        Check("BK downgrade log = 0",          0L, await CountLogAsync(BK, "tier_s6"));
    }

    // ── S7: Lifetime tier (qualifying_days NULL) — the job never touches it ──
    static async Task S7_LifetimeTier_NeverExpires(IServiceProvider sp)
    {
        AnsiConsole.MarkupLine("\n[bold cyan]S7[/] — Lifetime: BK bronz (qualifying_days NULL) → no grace/downgrade");
        await ResetAsync();

        await SetupAccountAsync(BK, "tier_s7", BkCrown, balance: 50,
            tierId: Tiers[$"{BK}/bronz"], periodStartDaysAgo: 500, expiresDaysAgo: null);

        await RunDowngradeAsync(sp);

        var st = await GetTierStateAsync(BK, "tier_s7", BkCrown);
        Check("tier still bronz",             Tiers[$"{BK}/bronz"], st.TierId ?? Guid.Empty);
        Check("tier_expires_at still NULL",   true, st.ExpiresAt is null);
        Check("no log",                       0L, await CountLogAsync(BK, "tier_s7"));
    }

    // ── Running jobs / services ─────────────────────────────────────────────

    static async Task RunDowngradeAsync(IServiceProvider sp)
    {
        using var scope = sp.CreateScope();
        var job = scope.ServiceProvider.GetRequiredService<TierDowngradeJob>();
        await job.RunAsync();
    }

    static async Task EvaluateAsync(IServiceProvider sp, string tenant, string contact, Guid programId, Guid accountTypeId)
    {
        using var scope = sp.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<ITierEvaluationService>();
        await svc.EvaluateAsync(tenant, contact, programId, accountTypeId, $"verify:{tenant}:{contact}");
    }

    // ── Seed helpers ────────────────────────────────────────────────────────

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

    static async Task LoadTierIdsAsync()
    {
        await using var db = new NpgsqlConnection(PG);
        await db.OpenAsync();
        await using var cmd = new NpgsqlCommand("""
            SELECT tn.slug, td.name, td.id
            FROM tier_definitions td
            JOIN programs p ON p.id = td.program_id
            JOIN tenants tn ON tn.id = p.tenant_id
            """, db);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
            Tiers[$"{r.GetString(0)}/{r.GetString(1)}"] = r.GetGuid(2);
    }

    static async Task ResetAsync()
    {
        await using var db = new NpgsqlConnection(PG);
        await db.OpenAsync();
        foreach (var sql in new[]
        {
            "DELETE FROM tier_upgrade_log WHERE contact_key LIKE 'tier_%'",
            "DELETE FROM ledger_entries WHERE contact_key LIKE 'tier_%'",
            "DELETE FROM customer_accounts WHERE contact_key LIKE 'tier_%'"
        })
        {
            await using var cmd = new NpgsqlCommand(sql, db);
            await cmd.ExecuteNonQueryAsync();
        }
    }

    static async Task SetupAccountAsync(
        string tenant, string contact, Guid accountTypeId, decimal balance,
        Guid? tierId = null, int? periodStartDaysAgo = null, int? expiresDaysAgo = null)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        await using var db = new NpgsqlConnection(PG);
        await db.OpenAsync();
        await using var cmd = new NpgsqlCommand("""
            INSERT INTO customer_accounts
                (id, tenant_id, contact_key, account_type_id, balance, updated_at,
                 tier_id, tier_period_start, tier_expires_at, tier_qualifying_pts)
            VALUES
                (gen_random_uuid(), @t, @c, @at, @b, NOW(), @tier, @ps, @exp, 0)
            ON CONFLICT (tenant_id, contact_key, account_type_id) DO NOTHING
            """, db);
        cmd.Parameters.AddWithValue("t",   TenantIds[tenant]);
        cmd.Parameters.AddWithValue("c",   contact);
        cmd.Parameters.AddWithValue("at",  accountTypeId);
        cmd.Parameters.AddWithValue("b",   balance);
        cmd.Parameters.AddWithValue("tier", (object?)tierId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("ps",  periodStartDaysAgo is int p ? today.AddDays(-p) : DBNull.Value);
        cmd.Parameters.AddWithValue("exp", expiresDaysAgo is int e ? today.AddDays(-e) : DBNull.Value);
        await cmd.ExecuteNonQueryAsync();
    }

    static async Task InsertEarnAsync(string tenant, string contact, Guid accountTypeId, decimal amount, int daysAgo)
    {
        await using var db = new NpgsqlConnection(PG);
        await db.OpenAsync();

        await using var acc = new NpgsqlCommand(
            "SELECT id FROM customer_accounts WHERE tenant_id=@t AND contact_key=@c AND account_type_id=@at", db);
        acc.Parameters.AddWithValue("t", TenantIds[tenant]);
        acc.Parameters.AddWithValue("c", contact);
        acc.Parameters.AddWithValue("at", accountTypeId);
        var acctId = (Guid)(await acc.ExecuteScalarAsync())!;

        var key = $"tier-seed:{acctId}:{daysAgo}d:{amount}";
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

    // ── Query helpers ───────────────────────────────────────────────────────

    record TierState(Guid? TierId, DateOnly? PeriodStart, DateOnly? ExpiresAt, decimal QualifyingPts);

    static async Task<TierState> GetTierStateAsync(string tenant, string contact, Guid accountTypeId)
    {
        await using var db = new NpgsqlConnection(PG);
        await db.OpenAsync();
        await using var cmd = new NpgsqlCommand("""
            SELECT tier_id, tier_period_start, tier_expires_at, tier_qualifying_pts
            FROM customer_accounts
            WHERE tenant_id=@t AND contact_key=@c AND account_type_id=@at
            """, db);
        cmd.Parameters.AddWithValue("t", TenantIds[tenant]);
        cmd.Parameters.AddWithValue("c", contact);
        cmd.Parameters.AddWithValue("at", accountTypeId);
        await using var r = await cmd.ExecuteReaderAsync();
        if (!await r.ReadAsync()) return new TierState(null, null, null, 0);
        return new TierState(
            r.IsDBNull(0) ? null : r.GetGuid(0),
            r.IsDBNull(1) ? null : r.GetFieldValue<DateOnly>(1),
            r.IsDBNull(2) ? null : r.GetFieldValue<DateOnly>(2),
            r.GetDecimal(3));
    }

    static async Task<long> CountLogAsync(string tenant, string contact)
    {
        await using var db = new NpgsqlConnection(PG);
        await db.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT COUNT(*) FROM tier_upgrade_log WHERE tenant_id=@t AND contact_key=@c", db);
        cmd.Parameters.AddWithValue("t", TenantIds[tenant]);
        cmd.Parameters.AddWithValue("c", contact);
        return (long)(await cmd.ExecuteScalarAsync())!;
    }

    static async Task<(Guid? from, Guid to, decimal pts)> GetLastLogAsync(string tenant, string contact)
    {
        await using var db = new NpgsqlConnection(PG);
        await db.OpenAsync();
        await using var cmd = new NpgsqlCommand("""
            SELECT from_tier_id, to_tier_id, qualifying_pts
            FROM tier_upgrade_log
            WHERE tenant_id=@t AND contact_key=@c
            ORDER BY created_at DESC LIMIT 1
            """, db);
        cmd.Parameters.AddWithValue("t", TenantIds[tenant]);
        cmd.Parameters.AddWithValue("c", contact);
        await using var r = await cmd.ExecuteReaderAsync();
        if (!await r.ReadAsync()) return (null, Guid.Empty, -1);
        return (r.IsDBNull(0) ? null : r.GetGuid(0), r.GetGuid(1), r.GetDecimal(2));
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
