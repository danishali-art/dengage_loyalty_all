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
/// Tiqmo SOW acceptance suite — end-to-end coverage of the requirements that fall within the engine's scope:
///   TQ01  Cashback rule flagship (KYC 1-hour window) + realtime credit signal + idempotency
///   TQ02  Points earn: SpendRule on a generic event (floor + zero-delta)
///   TQ03  Points burn: points.redeem (rate/min_points, below_minimum)
///   TQ04  Points transfer: two-leg + insufficient + daily_limit
///   TQ05  Points expiry: REAL PointsExpirationJob (FIFO, 180 days)
///   TQ06  Streak flagship: 3 consecutive months with total remittance >= 1000 SAR
///   TQ07  Streak country variant + transaction-count metric (count)
///   TQ08  Card: normal (captured) vs pre-auth distinction
///   TQ09  Combined card bucket: MCC + amount + zone + segment + frequency cap in ONE rule
///   TQ10  Refund policy: card.refund is ignored, no clawback (permanent decision)
///
/// Engine code stays unchanged; rules are set up temporarily against the fintech tenant. Required
/// bindings: signup, kyc.completed, card.transaction, remittance (DOTNET_ENVIRONMENT=Development).
/// TQ05 runs the real job — it may also sweep up stale expiry candidates left pending on other
/// tenants (harmless in dev; every suite reseeds its own data).
/// Cannot run IN PARALLEL with --streak-test / --card-test (streak_* tables are shared).
/// </summary>
public static class TiqmoTest
{
    const string PG        = "Host=localhost;Database=loyalty_dev;Username=postgres;Password=postgres";
    const string TENANT    = "fintech";
    const string FinPuanId = "019fd002-0000-7000-8000-000000000001"; // POINTS (redemption 0.01/min 1000, expiry 180d)
    const string CashId    = "019fd002-0000-7000-8000-000000000002"; // CASH
    const string ProgramId = "019fd001-0000-7000-8000-000000000001";

    const string Tq01RuleId = "019fd003-dddd-7000-8000-000000000001"; // KYC flagship → +2 CASH
    const string Tq02RuleId = "019fd003-dddd-7000-8000-000000000002"; // SpendRule remittance rate 0.1 → FinPuan
    const string Tq06RuleId = "019fd003-dddd-7000-8000-000000000003"; // monthly streak sum>=1000 ×3 → +25 CASH
    const string Tq07SaRuleId = "019fd003-dddd-7000-8000-000000000004"; // daily streak count>=2 ×2, SA → +10
    const string Tq07AeRuleId = "019fd003-dddd-7000-8000-000000000005"; // daily streak count>=2 ×2, AE → +5
    const string Tq08RuleId = "019fd003-dddd-7000-8000-000000000006"; // captured + amount>=100 → +3
    const string Tq09RuleId = "019fd003-dddd-7000-8000-000000000007"; // combined bucket → +8, once per day

    static int _passed = 0;
    static int _failed = 0;

    // tenants.id (uuid) for TENANT — resolved once at startup. tenant_id on most tables is now
    // this Guid (not the slug); ledger_entries/event_inbox/event_log are the deliberate exception
    // (still keyed by the slug — see Tenant.cs's remarks on why those 3 stay varchar).
    static string _tid = "";

    public static async Task RunAsync(IModel channel)
    {
        AnsiConsole.Write(new Rule("[yellow bold]Tiqmo SOW Acceptance Suite — fintech[/]").RuleStyle("grey"));
        AnsiConsole.MarkupLine("[grey]Cashback + points earn/burn/transfer/expiry + streak + card pre-auth + combined bucket + refund policy.[/]\n");

        _tid = await ScalarStringAsync($"SELECT id::text FROM tenants WHERE slug='{TENANT}'");

        var anchor = DateTime.UtcNow.Date.AddHours(12);
        // Safe anchor for month-period calculations: the 15th of this month, 12:00 UTC
        var monthAnchor = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 15, 12, 0, 0, DateTimeKind.Utc);

        await SetupAsync();

        // ── TQ01: SOW cashback flagship ───────────────────────────────────
        await RunTest("TQ01 — user without KYC completes KYC within 1 hour of signup → +2 SAR", async () =>
        {
            // tq_a: signup 30 min ago → KYC now → +2 CASH + points.earned signal
            await PublishRawAsync(channel, "signup", Data("tq_a", "0"), DateTime.UtcNow.AddMinutes(-30));
            await WaitAsync();
            var kycEventId = Guid.NewGuid().ToString();
            var kycData = Data("tq_a", "0");
            kycData["profile"] = new Dictionary<string, object?> { ["kyc_status"] = "none" };
            await PublishRawAsync(channel, "kyc.completed", kycData, DateTime.UtcNow, kycEventId);
            await WaitAsync();
            Assert("KYC within window: +2 CASH", await GetBalanceAsync("tq_a", CashId), 2m);
            Assert("realtime credit signal (loyalty.points.earned outbox)", await ScalarIntAsync(
                $"SELECT COUNT(*) FROM outbox_events WHERE tenant_id='{_tid}' AND event_type='loyalty.points.earned' AND dedup_key='points_earned:{kycEventId}'"), 1);

            // Publish same eventId again → no double payment (idempotency)
            await PublishRawAsync(channel, "kyc.completed", kycData, DateTime.UtcNow, kycEventId);
            await WaitAsync();
            Assert("duplicate eventId: balance still 2", await GetBalanceAsync("tq_a", CashId), 2m);

            // tq_b: signup 2 hours ago → window has passed
            await PublishRawAsync(channel, "signup", Data("tq_b", "0"), DateTime.UtcNow.AddHours(-2));
            await WaitAsync();
            var kycB = Data("tq_b", "0");
            kycB["profile"] = new Dictionary<string, object?> { ["kyc_status"] = "none" };
            await PublishRawAsync(channel, "kyc.completed", kycB, DateTime.UtcNow);
            await WaitAsync();
            Assert("outside window (2 hours): no reward", await GetBalanceAsync("tq_b", CashId), 0m);

            // tq_c: no signup at all → occurred_within false (safe fallback)
            var kycC = Data("tq_c", "0");
            kycC["profile"] = new Dictionary<string, object?> { ["kyc_status"] = "none" };
            await PublishRawAsync(channel, "kyc.completed", kycC, DateTime.UtcNow);
            await WaitAsync();
            Assert("KYC without signup: no reward", await GetBalanceAsync("tq_c", CashId), 0m);
        });

        // ── TQ02: Points earn — SpendRule on a generic event ───────────────
        await RunTest("TQ02 — every 10 SAR remittance = 1 FinPuan (SpendRule, floor)", async () =>
        {
            await PublishRawAsync(channel, "remittance", Data("tq_d", "1055.99"), DateTime.UtcNow);
            await WaitAsync();
            Assert("1055.99 SAR × 0.1 → floor = 105 points", await GetBalanceAsync("tq_d", FinPuanId), 105m);

            await PublishRawAsync(channel, "remittance", Data("tq_d", "9.99"), DateTime.UtcNow);
            await WaitAsync();
            Assert("9.99 SAR → floor(0.999) = 0: points unchanged", await GetBalanceAsync("tq_d", FinPuanId), 105m);
            Assert("zero-delta not written to ledger: single earn row", await ScalarIntAsync(
                $"SELECT COUNT(*) FROM ledger_entries WHERE tenant_id='{TENANT}' AND contact_key='tq_d' AND rule_id='{Tq02RuleId}' AND reason='earn'"), 1);
        });

        // ── TQ03: Points burn — points.redeem ────────────────────────────
        await RunTest("TQ03 — redeem: 1000 points → 10 CASH (rate 0.01); 500 points → below_minimum", async () =>
        {
            await PublishRawAsync(channel, "remittance", Data("tq_e", "12000.00"), DateTime.UtcNow);
            await WaitAsync();
            Assert("precondition: 1200 points", await GetBalanceAsync("tq_e", FinPuanId), 1200m);

            await PublishRawAsync(channel, "points.redeem", new Dictionary<string, object?>
            {
                ["contact_key"] = "tq_e",
                ["points_amount"] = "1000",
                ["source_account_type_id"] = FinPuanId
            }, DateTime.UtcNow);
            await WaitAsync();
            Assert("points after redeem: 200", await GetBalanceAsync("tq_e", FinPuanId), 200m);
            Assert("CASH after redeem: +10", await GetBalanceAsync("tq_e", CashId), 10m);

            var badRedeemId = Guid.NewGuid().ToString();
            await PublishRawAsync(channel, "points.redeem", new Dictionary<string, object?>
            {
                ["contact_key"] = "tq_e",
                ["points_amount"] = "500",
                ["source_account_type_id"] = FinPuanId
            }, DateTime.UtcNow, badRedeemId);
            await WaitAsync(2500);
            Assert("below min 1000: points unchanged", await GetBalanceAsync("tq_e", FinPuanId), 200m);
            Assert("below_minimum: inbox failed", await ScalarIntAsync(
                $"SELECT COUNT(*) FROM event_inbox WHERE tenant_id='{TENANT}' AND event_id='{badRedeemId}' AND status='failed'"), 1);
        });

        // ── TQ04: Points transfer ────────────────────────────────────────
        await RunTest("TQ04 — transfer: 150 points tq_e→tq_f; insufficient balance; daily limit 500", async () =>
        {
            await PublishTransferAsync(channel, "tq_e", "tq_f", "150");
            await WaitAsync();
            Assert("sender: 200-150=50", await GetBalanceAsync("tq_e", FinPuanId), 50m);
            Assert("receiver: +150", await GetBalanceAsync("tq_f", FinPuanId), 150m);
            Assert("outbox points.transferred", await ScalarIntAsync(
                $"SELECT COUNT(*) FROM outbox_events WHERE tenant_id='{_tid}' AND event_type='loyalty.points.transferred' AND payload->'data'->>'target_contact_key'='tq_f'"), 1);

            await PublishTransferAsync(channel, "tq_e", "tq_f", "100");
            await WaitAsync();
            Assert("insufficient balance (50<100): sender unchanged", await GetBalanceAsync("tq_e", FinPuanId), 50m);
            Assert("outbox transfer_failed insufficient_balance", await ScalarIntAsync(
                $"SELECT COUNT(*) FROM outbox_events WHERE tenant_id='{_tid}' AND event_type='loyalty.points.transfer_failed' AND payload->'data'->>'reason'='insufficient_balance'"), 1);

            // Top up balance (5000 SAR → +500 points), limit 500: 150+400 > 500 → rejected
            await PublishRawAsync(channel, "remittance", Data("tq_e", "5000.00"), DateTime.UtcNow);
            await WaitAsync();
            await PublishTransferAsync(channel, "tq_e", "tq_f", "400");
            await WaitAsync();
            Assert("daily limit exceeded: sender stays at 550", await GetBalanceAsync("tq_e", FinPuanId), 550m);
            Assert("outbox transfer_failed daily_limit_exceeded", await ScalarIntAsync(
                $"SELECT COUNT(*) FROM outbox_events WHERE tenant_id='{_tid}' AND event_type='loyalty.points.transfer_failed' AND payload->'data'->>'reason'='daily_limit_exceeded'"), 1);
        });

        // ── TQ05: Points expiry — real job ─────────────────────────────
        await RunTest("TQ05 — expiry: 800 points from 400 days ago expire (FIFO, 180 days), fresh 300 preserved", async () =>
        {
            await SeedAccountWithLedgerAsync("tq_g", FinPuanId, (800m, 400), (300m, 100));
            Assert("precondition: balance 1100", await GetBalanceAsync("tq_g", FinPuanId), 1100m);

            await RunExpirationJobAsync();

            Assert("balance after job: 300", await GetBalanceAsync("tq_g", FinPuanId), 300m);
            Assert("ledger points_expired = -800", await ScalarIntAsync(
                $"SELECT COUNT(*) FROM ledger_entries WHERE tenant_id='{TENANT}' AND contact_key='tq_g' AND reason='points_expired' AND delta=-800"), 1);
        });

        // ── TQ06: SOW streak flagship — 3 consecutive months >= 1000 SAR ─────────
        await RunTest("TQ06 — 3 consecutive months with total remittance 1000+ SAR → +25 SAR; below-threshold months don't count", async () =>
        {
            // month-2: 600 + 500 = 1100 (sum aggregation crossed via two transactions)
            await PublishRawAsync(channel, "remittance", Data("tq_h", "600.00"), monthAnchor.AddMonths(-2));
            await WaitAsync();
            await PublishRawAsync(channel, "remittance", Data("tq_h", "500.00"), monthAnchor.AddMonths(-2).AddDays(3));
            await WaitAsync();
            Assert("month-2 total 1100: streak_count=1", await ProgressCountAsync(Tq06RuleId, "tq_h"), 1);

            // month-1: single transaction 1200
            await PublishRawAsync(channel, "remittance", Data("tq_h", "1200.00"), monthAnchor.AddMonths(-1));
            await WaitAsync();
            Assert("month-1: streak_count=2", await ProgressCountAsync(Tq06RuleId, "tq_h"), 2);

            // this month: 800 → below threshold, counter doesn't advance (no break either — month isn't over)
            await PublishRawAsync(channel, "remittance", Data("tq_h", "800.00"), monthAnchor);
            await WaitAsync();
            Assert("this month 800 < 1000: counter stays at 2", await ProgressCountAsync(Tq06RuleId, "tq_h"), 2);

            // +300 → month total 1100 → 3rd month complete → +25 CASH
            await PublishRawAsync(channel, "remittance", Data("tq_h", "300.00"), monthAnchor.AddDays(1));
            await WaitAsync();
            Assert("3rd month complete: +25 CASH", await GetBalanceAsync("tq_h", CashId), 25m);
            Assert("streak_log completion_no=1", await ScalarIntAsync(
                $"SELECT COUNT(*) FROM streak_log WHERE tenant_id='{_tid}' AND campaign_id='{Tq06RuleId}' AND contact_key='tq_h' AND completion_no=1"), 1);
            Assert("outbox streak.completed", await ScalarIntAsync(
                $"SELECT COUNT(*) FROM outbox_events WHERE tenant_id='{_tid}' AND event_type='loyalty.streak.completed' AND dedup_key='streak_completed:{Tq06RuleId}:tq_h:1'"), 1);
        });

        // ── TQ07: streak country variant + transaction-count metric ───────────
        await RunTest("TQ07 — 2+ transactions per day, 2 consecutive days: SA rule awards +10, AE rule stays silent", async () =>
        {
            var saProfile = new Dictionary<string, object?> { ["country"] = "SA" };

            var d1 = Data("tq_i", "50.00"); d1["profile"] = saProfile;
            await PublishRawAsync(channel, "remittance", d1, anchor.AddDays(-1));
            await WaitAsync();
            var d2 = Data("tq_i", "50.00"); d2["profile"] = saProfile;
            await PublishRawAsync(channel, "remittance", d2, anchor.AddDays(-1).AddHours(1));
            await WaitAsync();
            Assert("yesterday 2 transactions: SA streak_count=1", await ProgressCountAsync(Tq07SaRuleId, "tq_i"), 1);

            var d3 = Data("tq_i", "50.00"); d3["profile"] = saProfile;
            await PublishRawAsync(channel, "remittance", d3, anchor);
            await WaitAsync();
            Assert("today 1 transaction: counter doesn't advance", await ProgressCountAsync(Tq07SaRuleId, "tq_i"), 1);

            var d4 = Data("tq_i", "50.00"); d4["profile"] = saProfile;
            await PublishRawAsync(channel, "remittance", d4, anchor.AddHours(1));
            await WaitAsync();
            Assert("today's 2nd transaction: complete → +10 CASH (SA reward)", await GetBalanceAsync("tq_i", CashId), 10m);
            Assert("AE rule never matched (streak_log empty)", await ScalarIntAsync(
                $"SELECT COUNT(*) FROM streak_log WHERE tenant_id='{_tid}' AND campaign_id='{Tq07AeRuleId}'"), 0);
        });

        // ── TQ08: card — normal (captured) vs pre-auth ───────────────────
        await RunTest("TQ08 — tx_status=captured earns cashback, pre_auth doesn't, earns once captured", async () =>
        {
            var c1 = Data("tq_j", "150.00"); c1["channel"] = "card"; c1["tx_status"] = "captured";
            await PublishRawAsync(channel, "card.transaction", c1, DateTime.UtcNow);
            await WaitAsync();
            Assert("captured 150 SAR: +3", await GetBalanceAsync("tq_j", CashId), 3m);

            var c2 = Data("tq_j", "150.00"); c2["channel"] = "card"; c2["tx_status"] = "pre_auth";
            await PublishRawAsync(channel, "card.transaction", c2, DateTime.UtcNow);
            await WaitAsync();
            Assert("pre_auth: no reward, still 3", await GetBalanceAsync("tq_j", CashId), 3m);

            var c3 = Data("tq_j", "150.00"); c3["channel"] = "card"; c3["tx_status"] = "captured";
            await PublishRawAsync(channel, "card.transaction", c3, DateTime.UtcNow);
            await WaitAsync();
            Assert("once pre-auth is captured (new captured event): +3 → 6", await GetBalanceAsync("tq_j", CashId), 6m);
        });

        // ── TQ09: combined card bucket — all dimensions in one rule ─────────
        await RunTest("TQ09 — MCC + amount + zone + segment + frequency cap in ONE rule", async () =>
        {
            var premium = new Dictionary<string, object?> { ["segment"] = "premium", ["country"] = "SA" };

            var e1 = Data("tq_k", "250.00"); e1["channel"] = "card"; e1["mcc"] = "5411"; e1["country"] = "SA"; e1["profile"] = premium;
            await PublishRawAsync(channel, "card.transaction", e1, DateTime.UtcNow);
            await WaitAsync();
            Assert("all dimensions match: +8", await GetBalanceAsync("tq_k", CashId), 8m);

            var standard = new Dictionary<string, object?> { ["segment"] = "standard", ["country"] = "SA" };
            var e2 = Data("tq_k", "250.00"); e2["channel"] = "card"; e2["mcc"] = "5411"; e2["country"] = "SA"; e2["profile"] = standard;
            await PublishRawAsync(channel, "card.transaction", e2, DateTime.UtcNow);
            await WaitAsync();
            Assert("segment=standard: no reward", await GetBalanceAsync("tq_k", CashId), 8m);

            var e3 = Data("tq_k", "250.00"); e3["channel"] = "card"; e3["mcc"] = "5999"; e3["country"] = "SA"; e3["profile"] = premium;
            await PublishRawAsync(channel, "card.transaction", e3, DateTime.UtcNow);
            await WaitAsync();
            Assert("outside MCC bucket: no reward", await GetBalanceAsync("tq_k", CashId), 8m);

            var e4 = Data("tq_k", "250.00"); e4["channel"] = "card"; e4["mcc"] = "5541"; e4["country"] = "SA"; e4["profile"] = premium;
            await PublishRawAsync(channel, "card.transaction", e4, DateTime.UtcNow);
            await WaitAsync();
            Assert("2nd eligible transaction same day: daily points cap reached (cap=8), still 8", await GetBalanceAsync("tq_k", CashId), 8m);
        });

        // ── TQ10: refund policy — card.refund is ignored ────────────────
        await RunTest("TQ10 — card.refund is ignored: no clawback (permanent policy)", async () =>
        {
            var refundId = Guid.NewGuid().ToString();
            var r = Data("tq_k", "250.00"); r["original_event_id"] = "irrelevant";
            await PublishRawAsync(channel, "card.refund", r, DateTime.UtcNow, refundId);
            await WaitAsync();
            Assert("balance unchanged (no clawback)", await GetBalanceAsync("tq_k", CashId), 8m);
            Assert("event never enters the engine (no binding, inbox empty)", await ScalarIntAsync(
                $"SELECT COUNT(*) FROM event_inbox WHERE tenant_id='{TENANT}' AND event_id='{refundId}'"), 0);
        });

        await TeardownAsync();

        AnsiConsole.WriteLine();
        AnsiConsole.Write(new Rule().RuleStyle("grey"));
        var color = _failed == 0 ? "green" : "red";
        AnsiConsole.MarkupLine($"[{color} bold]Result: {_passed} asserts passed, {_failed} tests failed[/]");
    }

    // ── Setup / Teardown ─────────────────────────────────────────────────

    static readonly string[] AllRuleIds =
        { Tq01RuleId, Tq02RuleId, Tq08RuleId, Tq09RuleId };
    static readonly string[] AllCampaignIds =
        { Tq06RuleId, Tq07SaRuleId, Tq07AeRuleId };

    static async Task SetupAsync()
    {
        var idList = string.Join("','", AllRuleIds);
        var campaignIdList = string.Join("','", AllCampaignIds);
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
            // the rules soft-delete trigger is two-phase — the double DELETE also cleans up crash leftovers
            $"DELETE FROM rules WHERE id IN ('{idList}')",
            $"DELETE FROM rules WHERE id IN ('{idList}')",
            $"DELETE FROM streak_campaigns WHERE id IN ('{campaignIdList}')"
        })
            await ExecuteSqlAsync(sql);

        await ExecuteSqlAsync($$$"""
            INSERT INTO rules (id, tenant_id, program_id, name, type, trigger,
                conditions, calculation, target_account_type_id,
                limits, priority, stackable, active_from, active_to, status, created_at, updated_at)
            VALUES
            ('{{{Tq01RuleId}}}', '{{{_tid}}}', '{{{ProgramId}}}',
             'TQ KYC Fast Cashback', 'FixedBonusRule', 'kyc.completed',
             '[{"field": "profile.kyc_status", "op": "eq", "value": "none"}, {"field": "event", "op": "occurred_within", "value": {"after_event": "signup", "hours": 1}}]',
             '{"type": "fixed", "amount": 2}', '{{{CashId}}}',
             NULL, 100, false, NULL, NULL, 'active', now(), now()),
            ('{{{Tq02RuleId}}}', '{{{_tid}}}', '{{{ProgramId}}}',
             'TQ Remittance Points', 'SpendRule', 'remittance',
             '[]',
             '{"type": "rate", "rate": 0.1}', '{{{FinPuanId}}}',
             NULL, 100, false, NULL, NULL, 'active', now(), now()),
            ('{{{Tq08RuleId}}}', '{{{_tid}}}', '{{{ProgramId}}}',
             'TQ Card Captured Cashback', 'FixedBonusRule', 'card.transaction',
             '[{"field": "tx_status", "op": "eq", "value": "captured"}, {"field": "amount", "op": "gte", "value": 100}]',
             '{"type": "fixed", "amount": 3}', '{{{CashId}}}',
             NULL, 100, false, NULL, NULL, 'active', now(), now()),
            ('{{{Tq09RuleId}}}', '{{{_tid}}}', '{{{ProgramId}}}',
             'TQ Combined Bucket', 'FixedBonusRule', 'card.transaction',
             '[{"field": "mcc", "op": "in", "value": ["5411", "5541"]}, {"field": "amount", "op": "gte", "value": 100}, {"field": "country", "op": "eq", "value": "SA"}, {"field": "profile.segment", "op": "eq", "value": "premium"}]',
             '{"type": "fixed", "amount": 8}', '{{{CashId}}}',
             '{"per_customer_per_day": 8}', 100, false, NULL, NULL, 'active', now(), now())
            """);

        await ExecuteSqlAsync($$$"""
            INSERT INTO streak_campaigns (id, tenant_id, program_id, name, trigger,
                target_account_type_id, conditions, config, active_from, active_to, status, created_at, updated_at)
            VALUES
            ('{{{Tq06RuleId}}}', '{{{_tid}}}', '{{{ProgramId}}}',
             'TQ Monthly Remittance Streak', 'remittance', '{{{CashId}}}',
             '[]',
             '{"period": "month", "target_periods": 3, "aggregate": {"metric": "sum", "threshold": 1000}, "timezone": "UTC", "on_complete": "restart", "reward": {"kind": "fixed_bonus", "amount": 25}}',
             NULL, NULL, 'active', now(), now()),
            ('{{{Tq07SaRuleId}}}', '{{{_tid}}}', '{{{ProgramId}}}',
             'TQ Daily Count Streak SA', 'remittance', '{{{CashId}}}',
             '[{"field": "profile.country", "op": "eq", "value": "SA"}]',
             '{"period": "day", "target_periods": 2, "aggregate": {"metric": "count", "threshold": 2}, "timezone": "UTC", "on_complete": "restart", "reward": {"kind": "fixed_bonus", "amount": 10}}',
             NULL, NULL, 'active', now(), now()),
            ('{{{Tq07AeRuleId}}}', '{{{_tid}}}', '{{{ProgramId}}}',
             'TQ Daily Count Streak AE', 'remittance', '{{{CashId}}}',
             '[{"field": "profile.country", "op": "eq", "value": "AE"}]',
             '{"period": "day", "target_periods": 2, "aggregate": {"metric": "count", "threshold": 2}, "timezone": "UTC", "on_complete": "restart", "reward": {"kind": "fixed_bonus", "amount": 5}}',
             NULL, NULL, 'active', now(), now())
            """);

        // Transfer block is added temporarily (redemption + expiration_days stay permanent in the seed, untouched)
        await ExecuteSqlAsync($$"""
            UPDATE account_types SET config = jsonb_set(config, '{transfer}', '{"daily_limit": 500}'::jsonb)
            WHERE id = '{{FinPuanId}}'
            """);

        await FlushRulesCacheAsync();
        await FlushCampaignsCacheAsync();
        await FlushRedisPatternAsync("limit:fintech:*");
        AnsiConsole.MarkupLine("[grey]fintech data reset, 4 TQ rules + 3 TQ streak campaigns added, transfer block applied to FinPuan, Redis cleared.[/]\n");
    }

    static async Task TeardownAsync()
    {
        var idList = string.Join("','", AllRuleIds);
        var campaignIdList = string.Join("','", AllCampaignIds);
        await ExecuteSqlAsync($"DELETE FROM rules WHERE id IN ('{idList}')");
        await ExecuteSqlAsync($"DELETE FROM streak_campaigns WHERE id IN ('{campaignIdList}')");
        await ExecuteSqlAsync($"UPDATE account_types SET config = config - 'transfer' WHERE id = '{FinPuanId}'");
        await FlushRulesCacheAsync();
        await FlushCampaignsCacheAsync();
        AnsiConsole.MarkupLine("\n[grey]Teardown: TQ rules and streak campaigns removed, transfer block lifted, rule cache cleared.[/]");
    }

    // ── Expiration job (the real prod job, ExpireJobVerify pattern) ───────

    static async Task RunExpirationJobAsync()
    {
        var services = new ServiceCollection();
        services.AddLogging(b => b.AddConsole().SetMinimumLevel(LogLevel.Warning));
        services.AddDbContext<LoyaltyDbContext>(opts => opts.UseNpgsql(PG));
        services.AddScoped<LedgerService>();
        services.AddScoped<PointsExpirationJob>();
        await using var sp = services.BuildServiceProvider();
        using var scope = sp.CreateScope();
        var job = scope.ServiceProvider.GetRequiredService<PointsExpirationJob>();
        await job.RunAsync();
    }

    static async Task SeedAccountWithLedgerAsync(string contact, string accountTypeId, params (decimal amount, int daysAgo)[] earns)
    {
        await ExecuteSqlAsync($"""
            INSERT INTO customer_accounts (id, tenant_id, contact_key, account_type_id, balance, updated_at)
            VALUES (gen_random_uuid(), '{_tid}', '{contact}', '{accountTypeId}', 0, NOW())
            ON CONFLICT (tenant_id, contact_key, account_type_id) DO NOTHING
            """);
        var total = 0m;
        foreach (var (amount, daysAgo) in earns)
        {
            total += amount;
            await ExecuteSqlAsync($$"""
                INSERT INTO ledger_entries (id, tenant_id, customer_account_id, contact_key, delta, reason,
                                            source_event_id, idempotency_key, metadata, created_at)
                SELECT gen_random_uuid(), '{{TENANT}}', ca.id, '{{contact}}', {{amount}}, 'earn',
                       'tq-seed', 'tq-seed:{{contact}}:{{daysAgo}}d:{{amount}}', '{}', NOW() - interval '{{daysAgo}} days'
                FROM customer_accounts ca
                WHERE ca.tenant_id='{{_tid}}' AND ca.contact_key='{{contact}}' AND ca.account_type_id='{{accountTypeId}}'
                """);
        }
        await ExecuteSqlAsync($"""
            UPDATE customer_accounts SET balance = {total}
            WHERE tenant_id='{_tid}' AND contact_key='{contact}' AND account_type_id='{accountTypeId}'
            """);
    }

    // ── Publish ──────────────────────────────────────────────────────────

    static Dictionary<string, object?> Data(string contact, string amount) => new()
    {
        ["contact_key"] = contact,
        ["amount"]      = amount,
        ["channel"]     = "app",
        ["currency"]    = "SAR"
    };

    static Task PublishTransferAsync(IModel ch, string from, string to, string points) =>
        PublishRawAsync(ch, "points.transfer", new Dictionary<string, object?>
        {
            ["contact_key"] = from,
            ["target_contact_key"] = to,
            ["points_amount"] = points,
            ["source_account_type_id"] = FinPuanId
        }, DateTime.UtcNow);

    static Task PublishRawAsync(IModel ch, string eventType, Dictionary<string, object?> data,
        DateTime occurredAt, string? eventId = null)
    {
        var envelope = new Dictionary<string, object?>
        {
            ["eventId"]    = eventId ?? Guid.NewGuid().ToString(),
            ["eventType"]  = eventType,
            ["tenant"]     = TENANT,
            ["version"]    = "1",
            ["occurredAt"] = occurredAt,
            ["data"]       = data
        };

        var body  = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(envelope));
        var props = ch.CreateBasicProperties();
        props.Persistent  = true;
        props.ContentType = "application/json";
        ch.BasicPublish("loyalty.events", eventType, props, body);
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

    static async Task<string> ScalarStringAsync(string sql)
    {
        await using var db = new NpgsqlConnection(PG);
        await db.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, db);
        return (string)(await cmd.ExecuteScalarAsync())!;
    }
}
