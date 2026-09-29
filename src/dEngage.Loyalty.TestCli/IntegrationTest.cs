using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Npgsql;
using RabbitMQ.Client;
using Spectre.Console;

public static class IntegrationTest
{
    const string PG     = "Host=localhost;Database=loyalty_dev;Username=postgres;Password=postgres";
    const string TENANT = "starbucks";

    static int _passed = 0;
    static int _failed = 0;

    public static async Task RunAsync(IModel channel)
    {
        AnsiConsole.Write(new Rule("[yellow bold]Integration Test — Starbucks Loyalty[/]").RuleStyle("grey"));
        AnsiConsole.MarkupLine("[grey]All test data will be reset, tests will run in sequence.[/]\n");

        await ResetAsync();

        // ── TEST 1: Normal order → Kış Kampanyası (p:100 winner) ──────────
        await RunTest("T01 — Normal order (web, sandwich) → Kış 3x + İlk Alışveriş +50★", async () =>
        {
            await SendOrderAsync(channel, "test_user", 150m, "web", "sandwich");
            await WaitAsync();
            var stars = await GetBalanceAsync("test_user", "Stars");
            // floor(150 * 0.30) = 45 (Kış winner) + 50 first purchase = 95
            Assert("Stars = 95", stars, 95m);
            var stamp = await GetBalanceAsync("test_user", "Kahve Damgası");
            Assert("Stamp = 0 (not sandwich)", stamp, 0m);
        });

        // ── TEST 2: Mobile channel → not Mobil+Kahve 4x, sandwich → Mobil 2x
        await RunTest("T02 — Mobile order (sandwich) → earns Mobil 2x", async () =>
        {
            await SendOrderAsync(channel, "test_user", 100m, "mobile", "sandwich");
            await WaitAsync();
            var stars = await GetBalanceAsync("test_user", "Stars");
            // 95 + floor(100 * 0.20) = 95 + 20 = 115
            // Note: Kış p:100 > Mobil 2x p:20, Kış becomes winner → floor(100*0.30)=30
            // Kış total is currently 45, 45+30=75 < 3000 → passes → 95 + 30 = 125
            Assert("Stars = 125", stars, 125m);
        });

        // ── TEST 3: Coffee, store → not Kahve 1.5x, Kış p:100 winner ────
        await RunTest("T03 — Store coffee → Kış 3x winner + 1 stamp", async () =>
        {
            await SendOrderAsync(channel, "test_user", 100m, "store", "coffee");
            await WaitAsync();
            var stars = await GetBalanceAsync("test_user", "Stars");
            // 125 + floor(100 * 0.30) = 125 + 30 = 155
            Assert("Stars = 155", stars, 155m);
            var stamp = await GetBalanceAsync("test_user", "Kahve Damgası");
            Assert("Stamp = 1", stamp, 1m);
        });

        // ── TEST 4: Mobile + coffee → Kış p:100 > Mobil+Kahve p:50 ───────
        await RunTest("T04 — Mobile + coffee → Kış 3x winner (p:100) + 1 stamp", async () =>
        {
            await SendOrderAsync(channel, "test_user", 200m, "mobile", "coffee");
            await WaitAsync();
            var stars = await GetBalanceAsync("test_user", "Stars");
            // 155 + floor(200 * 0.30) = 155 + 60 = 215
            Assert("Stars = 215", stars, 215m);
            var stamp = await GetBalanceAsync("test_user", "Kahve Damgası");
            Assert("Stamp = 2", stamp, 2m);
        });

        // ── TEST 5: First-purchase bonus must not come again ──────────────
        await RunTest("T05 — First-purchase bonus must not come again on the 2nd order", async () =>
        {
            await SendOrderAsync(channel, "test_user", 100m, "web", "sandwich");
            await WaitAsync();
            var stars = await GetBalanceAsync("test_user", "Stars");
            // 215 + floor(100 * 0.30) = 215 + 30 = 245 (no bonus)
            Assert("Stars = 245 (no repeat bonus)", stars, 245m);
        });

        // ── TEST 6: Cash load and spend ───────────────────────────────────
        await RunTest("T06 — Load 200 TL cash, spend 75 TL → balance 125 TL", async () =>
        {
            await SendCashAddAsync(channel, "test_user", 200m);
            await WaitAsync();
            await SendCashSpendAsync(channel, "test_user", 75m);
            await WaitAsync();
            var card = await GetBalanceAsync("test_user", "Starbucks Card");
            Assert("Card = 125 TL", card, 125m);
            var stars = await GetBalanceAsync("test_user", "Stars");
            Assert("Stars unchanged = 245", stars, 245m);
        });

        // ── TEST 7: 10 coffees → stamp completes, free_drink is written ───
        await RunTest("T07 — 10 coffee orders → stamp completes, free_drink earned", async () =>
        {
            // There are 2 stamps now, 8 more needed
            // 8 x 50TL store/coffee: Kış 3x winner → 8 x floor(50*0.30)=15 → +120★
            for (int i = 0; i < 8; i++)
            {
                await SendOrderAsync(channel, "test_user", 50m, "store", "coffee");
                await Task.Delay(400);
            }
            await WaitAsync(4000);
            var stamp = await GetBalanceAsync("test_user", "Kahve Damgası");
            Assert("Stamp reset = 0", stamp, 0m);
            var rewards = await GetRewardCountAsync("test_user", "free_drink");
            Assert("free_drink reward written = 1", rewards, 1);
        });

        // ── TEST 8: Points redeem — insufficient balance ───────────────────
        // After T07: 245 + 8×floor(50×0.30)=15 → 245+120 = 365★ < 500 → rejected
        await RunTest("T08 — Stars < 500 → redeem rejected, balance unchanged", async () =>
        {
            var starsBefore = await GetBalanceAsync("test_user", "Stars");
            var cardBefore  = await GetBalanceAsync("test_user", "Starbucks Card");
            await SendRedeemAsync(channel, "test_user", 500m);
            await WaitAsync();
            var starsAfter = await GetBalanceAsync("test_user", "Stars");
            var cardAfter  = await GetBalanceAsync("test_user", "Starbucks Card");
            Assert("Stars unchanged", starsAfter, starsBefore);
            Assert("Card unchanged",  cardAfter,  cardBefore);
        });

        // ── TEST 9: Points redeem — sufficient balance ────────────────────
        // 2x 1000TL mobile/coffee → Kış p:100 winner → 2×floor(1000×0.30)=300 → +600★
        await RunTest("T09 — Stars ≥ 500 → redeem succeeds, 500★ → 50 TL", async () =>
        {
            await SendOrderAsync(channel, "test_user", 1000m, "mobile", "coffee");
            await SendOrderAsync(channel, "test_user", 1000m, "mobile", "coffee");
            await WaitAsync(2000);
            var starsBefore = await GetBalanceAsync("test_user", "Stars");
            var cardBefore  = await GetBalanceAsync("test_user", "Starbucks Card");

            await SendRedeemAsync(channel, "test_user", 500m);
            await WaitAsync();

            var starsAfter = await GetBalanceAsync("test_user", "Stars");
            var cardAfter  = await GetBalanceAsync("test_user", "Starbucks Card");
            Assert("Stars −500", starsAfter, starsBefore - 500m);
            Assert("Card +50 TL", cardAfter, cardBefore + 50m);
        });

        // ── TEST 10: Idempotency ───────────────────────────────────────────
        // 100TL web/sandwich → Kış winner → floor(100×0.30)=30★ (once)
        await RunTest("T10 — Sending the same event_id again does not duplicate the ledger", async () =>
        {
            var eventId = Guid.NewGuid().ToString();
            var starsBefore = await GetBalanceAsync("test_user", "Stars");
            PublishRaw(channel, "order.created", eventId, new
            {
                contact_key = "test_user", amount = "100.00",
                channel = "web", payment_method = "card",
                items = new[] { new { sku = "X", category = "sandwich", qty = 1, total = "100.00" } }
            });
            await WaitAsync();
            PublishRaw(channel, "order.created", eventId, new
            {
                contact_key = "test_user", amount = "100.00",
                channel = "web", payment_method = "card",
                items = new[] { new { sku = "X", category = "sandwich", qty = 1, total = "100.00" } }
            });
            await WaitAsync();
            var starsAfter = await GetBalanceAsync("test_user", "Stars");
            // Kış winner: floor(100*0.30)=30, only once
            Assert("Stars must increase only once (+30)", starsAfter, starsBefore + 30m);
        });

        // ── TEST 11: Different-customer isolation ─────────────────────────
        // test_user2: 500TL mobile/coffee → Kış p:100 winner floor(500×0.30)=150 + İlk +50 = 200★
        await RunTest("T11 — A different customer (test_user2) does not affect test_user", async () =>
        {
            var starsBefore = await GetBalanceAsync("test_user", "Stars");
            await SendOrderAsync(channel, "test_user2", 500m, "mobile", "coffee");
            await WaitAsync();
            var starsAfter = await GetBalanceAsync("test_user", "Stars");
            Assert("test_user Stars unchanged", starsAfter, starsBefore);
            var stars2 = await GetBalanceAsync("test_user2", "Stars");
            // Kış floor(500*0.30)=150 + İlk alışveriş +50 = 200
            Assert("test_user2 Stars = 200", stars2, 200m);
        });

        // ── TEST 12: Order refund ──────────────────────────────────────────
        // 200TL web/sandwich → Kış winner floor(200×0.30)=60★; refund → −60★
        await RunTest("T12 — Order refund → points reclaimed", async () =>
        {
            var eventId = Guid.NewGuid().ToString();
            var starsBefore = await GetBalanceAsync("test_user", "Stars");
            PublishRaw(channel, "order.created", eventId, new
            {
                contact_key = "test_user", amount = "200.00",
                channel = "web", payment_method = "card",
                items = new[] { new { sku = "X", category = "sandwich", qty = 1, total = "200.00" } }
            });
            await WaitAsync();
            var starsAfterOrder = await GetBalanceAsync("test_user", "Stars");
            // Kış winner: floor(200*0.30) = 60
            Assert("After order +60★", starsAfterOrder, starsBefore + 60m);

            PublishRaw(channel, "order.refunded", Guid.NewGuid().ToString(), new
            {
                contact_key = "test_user",
                original_event_id = eventId,
                refund_ratio = "1.0"
            });
            await WaitAsync();
            var starsAfterRefund = await GetBalanceAsync("test_user", "Stars");
            Assert("After refund −60★", starsAfterRefund, starsBefore);
        });

        // ══ LIMIT TESTS ═══════════════════════════════════════════════════
        AnsiConsole.WriteLine();
        AnsiConsole.Write(new Rule("[cyan bold]Limit Tests[/]").RuleStyle("grey"));
        AnsiConsole.WriteLine();

        // ── L01: per_customer_total — fill to the cap, boundary ───────────
        // İlk Alışveriş +50★ bonus must be granted only once (limit=50, fixed=50)
        await RunTest("L01 — per_customer_total: first-purchase bonus limit=50 → fills exactly, doesn't come again", async () =>
        {
            // Reset — only for the limit user
            await ResetUserAsync("limit_user");

            // 1st order → Kış winner + İlk Alışveriş bonus (+50★)
            await SendOrderAsync(channel, "limit_user", 100m, "web", "sandwich");
            await WaitAsync();
            var after1 = await GetBalanceAsync("limit_user", "Stars");
            // floor(100*0.30)=30 (Kış winner) + 50 bonus = 80
            Assert("L01 order 1: Stars = 80", after1, 80m);

            // 2nd order → bonus total 50 == limit, must not be granted anymore
            await SendOrderAsync(channel, "limit_user", 100m, "web", "sandwich");
            await WaitAsync();
            var after2 = await GetBalanceAsync("limit_user", "Stars");
            // 80 + floor(100*0.30)=30 + 0 bonus = 110
            Assert("L01 order 2: no bonus, Stars = 110", after2, 110m);
        });

        // ── L02: per_customer_per_day — Temel takes over once Kış cap fills ─
        // Kış 3x total cap=3000. First fill Kış, then Temel (p:10) becomes winner.
        // Temel daily 500★ cap: 4990TL → floor(499)=499★. Then 200TL → remaining 1★ clipped.
        await RunTest("L02 — per_customer_per_day: Temel's daily 500★ clipping once Kış runs out", async () =>
        {
            await ResetUserAsync("daily_user");

            // Fill Kış completely: 10001TL → floor(10001*0.30)=3000, limit=3000 → fills exactly
            await SendOrderAsync(channel, "daily_user", 10001m, "web", "sandwich");
            await WaitAsync();
            var after0 = await GetBalanceAsync("daily_user", "Stars");
            // 3000 (Kış) + 50 (İlk alışveriş) = 3050
            Assert("L02 order 0: Kış full, Stars = 3050", after0, 3050m);

            // Kış now full → Temel (p:10) winner. 4990TL → floor(4990*0.10)=499★ (daily remaining=500)
            await SendOrderAsync(channel, "daily_user", 4990m, "web", "sandwich");
            await WaitAsync();
            var after1 = await GetBalanceAsync("daily_user", "Stars");
            // 3050 + 499 = 3549
            Assert("L02 order 1: Temel 499★, Stars = 3549", after1, 3549m);

            // 200TL → Temel floor(20) but daily remaining=1 → clipped → 1★
            await SendOrderAsync(channel, "daily_user", 200m, "web", "sandwich");
            await WaitAsync();
            var after2 = await GetBalanceAsync("daily_user", "Stars");
            // 3549 + 1 (clipped) = 3550
            Assert("L02 order 2: daily remaining 1★ clipped, Stars = 3550", after2, 3550m);

            // 3rd order → Temel daily 500 full → 0★
            await SendOrderAsync(channel, "daily_user", 500m, "web", "sandwich");
            await WaitAsync();
            var after3 = await GetBalanceAsync("daily_user", "Stars");
            // 3550 + 0 = 3550
            Assert("L02 order 3: daily limit full, Stars unchanged = 3550", after3, 3550m);
        });

        // ── L03: per_customer_total — Kış Kampanyası 3000★ cap, clipping ──
        // Kış Kampanyası 3x, total limit 3000★. 10001TL → floor(3000.3)=3000★ exactly at limit.
        // Old behavior (>): skipped entirely. New (clipping): 3000★ must be granted.
        await RunTest("L03 — per_customer_total: Kış 3x cap=3000, 10001TL → exactly 3000★ granted via clipping", async () =>
        {
            await ResetUserAsync("cap_user");

            // 10001TL web/sandwich → Kış Kampanyası 3x winner (p:100, unconditional)
            // floor(10001 * 0.30) = 3000, limit=3000, remaining=3000 → 3000★ granted
            await SendOrderAsync(channel, "cap_user", 10001m, "web", "sandwich");
            await WaitAsync();
            var after1 = await GetBalanceAsync("cap_user", "Stars");
            // 3000 (Kış) + 50 (ilk alışveriş) = 3050
            Assert("L03 order 1: Stars = 3050 (3000 Kış + 50 bonus)", after1, 3050m);

            // 2nd order → Kış total 3000 == limit full → Temel (p:10) becomes winner
            await SendOrderAsync(channel, "cap_user", 5000m, "web", "sandwich");
            await WaitAsync();
            var after2 = await GetBalanceAsync("cap_user", "Stars");
            // 3050 + floor(5000*0.10)=500 (Temel winner, no daily cap) = 3550
            Assert("L03 order 2: Kış full, Temel winner, Stars = 3550", after2, 3550m);
        });

        // ── L04: per_customer_total partial fill ──────────────────────────
        // İlk Alışveriş limit=50, but a different scenario to test this:
        // Kış 3x cap=3000. 5000TL → 1500★ granted (remaining 3000). Then 5000TL → 1500★ more.
        // Then 5000TL → remaining 0, nothing granted.
        // Also: 9000TL in a single order → floor(2700)=2700, 2700<3000 → granted in full,
        // then 1200TL → remaining 300, floor(360)>300 → clipped, exactly 300★ granted.
        await RunTest("L04 — per_customer_total partial fill: with 300★ remaining, a 1200TL order → clipped to exactly 300★", async () =>
        {
            await ResetUserAsync("partial_user");

            // 9000TL → Kış 3x = floor(2700) = 2700★  (remaining: 3000-2700=300)
            await SendOrderAsync(channel, "partial_user", 9000m, "web", "sandwich");
            await WaitAsync();
            var after1 = await GetBalanceAsync("partial_user", "Stars");
            // 2700 (Kış) + 50 (ilk alışveriş) = 2750
            Assert("L04 order 1: Stars = 2750", after1, 2750m);

            // 1200TL → Kış 3x = floor(360) = 360★ but remaining=300 → clipped → 300★
            await SendOrderAsync(channel, "partial_user", 1200m, "web", "sandwich");
            await WaitAsync();
            var after2 = await GetBalanceAsync("partial_user", "Stars");
            // 2750 + 300 (clipped) = 3050
            Assert("L04 order 2: 360★→300★ clipped, Stars = 3050", after2, 3050m);

            // 3rd order → Kış total 3000 completely full → Temel (p:10) becomes winner
            await SendOrderAsync(channel, "partial_user", 5000m, "web", "sandwich");
            await WaitAsync();
            var after3 = await GetBalanceAsync("partial_user", "Stars");
            // 3050 + floor(5000*0.10)=500 (Temel winner) = 3550
            Assert("L04 order 3: Kış full, Temel winner, Stars = 3550", after3, 3550m);
        });

        // ══ RULE COMBINATION TESTS ═══════════════════════════════════════
        AnsiConsole.WriteLine();
        AnsiConsole.Write(new Rule("[cyan bold]Rule Combination Tests[/]").RuleStyle("grey"));
        AnsiConsole.WriteLine();

        // ── K01: Stackable + non-stackable together ───────────────────────
        // Order: store/coffee → Kış (p:100, stackable:false) winner + Kahve Damgası (stackable:true)
        // Both apply at the same time, the stamp must increase too
        await RunTest("K01 — Stackable+NonStackable: Kış winner + Kahve Damgası at the same time", async () =>
        {
            await ResetUserAsync("combo_user");
            await SendOrderAsync(channel, "combo_user", 100m, "store", "coffee");
            await WaitAsync();
            var stars = await GetBalanceAsync("combo_user", "Stars");
            // floor(100*0.30)=30 (Kış) + 50 (İlk alışveriş) = 80
            Assert("K01 Stars = 80", stars, 80m);
            var stamp = await GetBalanceAsync("combo_user", "Kahve Damgası");
            Assert("K01 Stamp = 1 (stackable worked)", stamp, 1m);
        });

        // ── K02: İlk Alışveriş bonus is stackable, always applies ────────
        // Sandwich order → Kış winner + İlk Alışveriş bonus (stackable:true, unconditional)
        // The İlk Alışveriş bonus has no conditions, only a limit
        await RunTest("K02 — First-purchase stackable: also comes on a non-coffee order", async () =>
        {
            await ResetUserAsync("combo2_user");
            await SendOrderAsync(channel, "combo2_user", 200m, "web", "sandwich");
            await WaitAsync();
            var stars = await GetBalanceAsync("combo2_user", "Stars");
            // floor(200*0.30)=60 (Kış) + 50 (İlk alışveriş stackable) = 110
            Assert("K02 Stars = 110 (60 Kış + 50 bonus)", stars, 110m);
            var stamp = await GetBalanceAsync("combo2_user", "Kahve Damgası");
            Assert("K02 Stamp = 0 (sandwich, the stamp rule requires coffee)", stamp, 0m);
        });

        // ── K03: Winner change — Kış has no condition, so it always wins ──
        // mobile/coffee → Kış p:100 > Mobil+Kahve p:50 > Kahve p:30 > Mobil p:20 > Temel p:10
        // Kış is always the winner; the others can never win (unless Kış is full)
        await RunTest("K03 — Priority order: Kış (p:100) is the winner on a mobile/coffee order", async () =>
        {
            await ResetUserAsync("prio_user");
            await SendOrderAsync(channel, "prio_user", 300m, "mobile", "coffee");
            await WaitAsync();
            var stars = await GetBalanceAsync("prio_user", "Stars");
            // Kış winner: floor(300*0.30)=90 + 50 İlk Alışveriş = 140
            // (Mobil+Kahve 4x or Kahve 1.5x not applied — Kış winner, stackable:false)
            Assert("K03 Stars = 140 (Kış winner, not 4x)", stars, 140m);
        });

        // ══ STAMP EDGE CASE TESTS ═════════════════════════════════════════
        AnsiConsole.WriteLine();
        AnsiConsole.Write(new Rule("[cyan bold]Stamp Edge Case Tests[/]").RuleStyle("grey"));
        AnsiConsole.WriteLine();

        // ── D01: Stamp completes exactly at the 10th coffee, 11th starts a new cycle ──
        await RunTest("D01 — 20 coffees → 2 free_drink, stamp resets again", async () =>
        {
            await ResetUserAsync("stamp20_user");
            // 20 x 50TL store/coffee → 2 cycles
            for (int i = 0; i < 20; i++)
            {
                await SendOrderAsync(channel, "stamp20_user", 50m, "store", "coffee");
                await Task.Delay(300);
            }
            await WaitAsync(5000);
            var stamp = await GetBalanceAsync("stamp20_user", "Kahve Damgası");
            Assert("D01 Stamp reset (2nd cycle completed) = 0", stamp, 0m);
            var rewards = await GetRewardCountAsync("stamp20_user", "free_drink");
            Assert("D01 2 free_drink earned", rewards, 2);
        });

        // ── D02: 9 coffees → stamp not completed, no free_drink ─────────
        await RunTest("D02 — 9 coffees → stamp 9, no free_drink", async () =>
        {
            await ResetUserAsync("stamp9_user");
            for (int i = 0; i < 9; i++)
            {
                await SendOrderAsync(channel, "stamp9_user", 50m, "store", "coffee");
                await Task.Delay(300);
            }
            await WaitAsync(3000);
            var stamp = await GetBalanceAsync("stamp9_user", "Kahve Damgası");
            Assert("D02 Stamp = 9", stamp, 9m);
            var rewards = await GetRewardCountAsync("stamp9_user", "free_drink");
            Assert("D02 free_drink = 0", rewards, 0);
        });

        // ── D03: Sandwich order gives no stamp ────────────────────────────
        await RunTest("D03 — A sandwich order gives no stamp", async () =>
        {
            await ResetUserAsync("nostamp_user");
            await SendOrderAsync(channel, "nostamp_user", 100m, "store", "sandwich");
            await WaitAsync();
            var stamp = await GetBalanceAsync("nostamp_user", "Kahve Damgası");
            Assert("D03 Stamp = 0 (sandwich, no coffee rule)", stamp, 0m);
            var stars = await GetBalanceAsync("nostamp_user", "Stars");
            // floor(100*0.30)=30 (Kış) + 50 bonus = 80
            Assert("D03 Stars = 80 (stars granted)", stars, 80m);
        });

        // ══ IDEMPOTENCY STRESS TESTS ═════════════════════════════════════
        AnsiConsole.WriteLine();
        AnsiConsole.Write(new Rule("[cyan bold]Idempotency Stress Tests[/]").RuleStyle("grey"));
        AnsiConsole.WriteLine();

        // ── I01: Send the same event 5 times — single ledger entry ───────
        await RunTest("I01 — Same order event_id 5 times → ledger written once", async () =>
        {
            await ResetUserAsync("idem_user");
            var eventId = Guid.NewGuid().ToString();
            for (int i = 0; i < 5; i++)
            {
                PublishRaw(channel, "order.created", eventId, new
                {
                    contact_key = "idem_user", amount = "100.00",
                    channel = "web", payment_method = "card",
                    items = new[] { new { sku = "X", category = "sandwich", qty = 1, total = "100.00" } }
                });
                await Task.Delay(100);
            }
            await WaitAsync(2000);
            var stars = await GetBalanceAsync("idem_user", "Stars");
            // Single transaction: floor(100*0.30)=30 + 50 bonus = 80
            Assert("I01 Stars = 80 (single transaction)", stars, 80m);
        });

        // ── I02: Same cash.added event_id 3 times → balance increases once ─
        await RunTest("I02 — Same cash.added event_id 3 times → balance increases once", async () =>
        {
            await ResetUserAsync("idem2_user");
            var eventId = Guid.NewGuid().ToString();
            for (int i = 0; i < 3; i++)
            {
                PublishRaw(channel, "cash.added", eventId, new
                {
                    contact_key = "idem2_user",
                    amount = "500.00",
                    account_type_id = "018fcd02-0000-7000-8000-000000000002"
                });
                await Task.Delay(100);
            }
            await WaitAsync(2000);
            var card = await GetBalanceAsync("idem2_user", "Starbucks Card");
            Assert("I02 Card = 500 TL (single transaction)", card, 500m);
        });

        // ── I03: Same redeem event_id twice → points deducted once ──────
        await RunTest("I03 — Same points.redeem event_id twice → points deducted once", async () =>
        {
            await ResetUserAsync("idem3_user");
            // First earn 2000★
            await SendOrderAsync(channel, "idem3_user", 6000m, "web", "sandwich");
            await WaitAsync();
            var starsBefore = await GetBalanceAsync("idem3_user", "Stars");

            var redeemId = Guid.NewGuid().ToString();
            for (int i = 0; i < 2; i++)
            {
                PublishRaw(channel, "points.redeem", redeemId, new
                {
                    contact_key = "idem3_user",
                    points_amount = "500.00",
                    source_account_type_id = "018fcd02-0000-7000-8000-000000000001"
                });
                await Task.Delay(100);
            }
            await WaitAsync(2000);
            var starsAfter = await GetBalanceAsync("idem3_user", "Stars");
            Assert("I03 Stars deducted only once (-500)", starsAfter, starsBefore - 500m);
        });

        // ══ REFUND EDGE CASE TESTS ════════════════════════════════════════
        AnsiConsole.WriteLine();
        AnsiConsole.Write(new Rule("[cyan bold]Refund Edge Case Tests[/]").RuleStyle("grey"));
        AnsiConsole.WriteLine();

        // ── R01: Refund a nonexistent order — error, balance unchanged ───
        await RunTest("R01 — Refunding a nonexistent order → error, balance unchanged", async () =>
        {
            await ResetUserAsync("refund_user");
            await SendOrderAsync(channel, "refund_user", 100m, "web", "sandwich");
            await WaitAsync();
            var starsBefore = await GetBalanceAsync("refund_user", "Stars");

            // Refund with a nonexistent event_id — consumer sends it to the DLQ
            PublishRaw(channel, "order.refunded", Guid.NewGuid().ToString(), new
            {
                contact_key = "refund_user",
                original_event_id = Guid.NewGuid().ToString(), // nonexistent
                refund_ratio = "1.0"
            });
            await WaitAsync();
            var starsAfter = await GetBalanceAsync("refund_user", "Stars");
            Assert("R01 Stars unchanged (refund failed)", starsAfter, starsBefore);
        });

        // ── R02: Partial refund (0.5 ratio) → half the points reclaimed ──
        await RunTest("R02 — Partial refund ratio=0.5 → half the points reclaimed", async () =>
        {
            await ResetUserAsync("refund2_user");
            var eventId = Guid.NewGuid().ToString();
            PublishRaw(channel, "order.created", eventId, new
            {
                contact_key = "refund2_user", amount = "200.00",
                channel = "web", payment_method = "card",
                items = new[] { new { sku = "X", category = "sandwich", qty = 1, total = "200.00" } }
            });
            await WaitAsync();
            var starsAfterOrder = await GetBalanceAsync("refund2_user", "Stars");
            // floor(200*0.30)=60 (Kış) + 50 bonus = 110

            PublishRaw(channel, "order.refunded", Guid.NewGuid().ToString(), new
            {
                contact_key = "refund2_user",
                original_event_id = eventId,
                refund_ratio = "0.5"
            });
            await WaitAsync();
            var starsAfterRefund = await GetBalanceAsync("refund2_user", "Stars");
            // 60★ * 0.5 = 30★ reclaimed → 110 - 30 = 80
            // (the 50★ bonus also at 0.5 ratio: -25 → 110 - 30 - 25 = 55)
            var expectedRefund = Math.Round(60m * 0.5m, 4) + Math.Round(50m * 0.5m, 4);
            Assert("R02 Half the points reclaimed", starsAfterRefund, starsAfterOrder - expectedRefund);
        });

        // ── R03: Same refund event_id again after refund → idempotent ────
        await RunTest("R03 — Same refund event_id twice → processed once", async () =>
        {
            await ResetUserAsync("refund3_user");
            var orderId = Guid.NewGuid().ToString();
            PublishRaw(channel, "order.created", orderId, new
            {
                contact_key = "refund3_user", amount = "300.00",
                channel = "web", payment_method = "card",
                items = new[] { new { sku = "X", category = "sandwich", qty = 1, total = "300.00" } }
            });
            await WaitAsync();
            var starsAfterOrder = await GetBalanceAsync("refund3_user", "Stars");

            var refundId = Guid.NewGuid().ToString();
            for (int i = 0; i < 2; i++)
            {
                PublishRaw(channel, "order.refunded", refundId, new
                {
                    contact_key = "refund3_user",
                    original_event_id = orderId,
                    refund_ratio = "1.0"
                });
                await Task.Delay(100);
            }
            await WaitAsync(2000);
            var starsAfterRefund = await GetBalanceAsync("refund3_user", "Stars");
            // earned floor(300*0.30)=90 + 50 bonus = 140, full refund → must be 0
            Assert("R03 Full refund, idempotent (Stars = 0)", starsAfterRefund, 0m);
        });

        // ── R04: Partial order refund (amount/original_amount format) ─────
        await RunTest("R04 — Refund amount/original_amount format → ratio computed correctly", async () =>
        {
            await ResetUserAsync("refund4_user");
            var eventId = Guid.NewGuid().ToString();
            PublishRaw(channel, "order.created", eventId, new
            {
                contact_key = "refund4_user", amount = "400.00",
                channel = "web", payment_method = "card",
                items = new[] { new { sku = "X", category = "sandwich", qty = 1, total = "400.00" } }
            });
            await WaitAsync();
            var starsAfterOrder = await GetBalanceAsync("refund4_user", "Stars");
            // floor(400*0.30)=120 (Kış) + 50 bonus = 170

            // 100TL / 400TL = 0.25 ratio
            PublishRaw(channel, "order.refunded", Guid.NewGuid().ToString(), new
            {
                contact_key = "refund4_user",
                original_event_id = eventId,
                amount = "100.00",
                original_amount = "400.00"
            });
            await WaitAsync();
            var starsAfterRefund = await GetBalanceAsync("refund4_user", "Stars");
            // Kış: -round(120*0.25,4)=-30, bonus: -round(50*0.25,4)=-12.5 → 170-42.5=127.5
            var expectedLoss = Math.Round(120m * 0.25m, 4) + Math.Round(50m * 0.25m, 4);
            Assert("R04 Partial refund ratio computed correctly", starsAfterRefund, starsAfterOrder - expectedLoss);
        });

        // ══ CASH EDGE CASE TESTS ══════════════════════════════════════════
        AnsiConsole.WriteLine();
        AnsiConsole.Write(new Rule("[cyan bold]Cash Edge Case Tests[/]").RuleStyle("grey"));
        AnsiConsole.WriteLine();

        // ── C01: Spending more than the balance → error, balance unchanged ─
        await RunTest("C01 — cash.spent exceeding balance → error, balance unchanged", async () =>
        {
            await ResetUserAsync("cash_user");
            await SendCashAddAsync(channel, "cash_user", 100m);
            await WaitAsync();
            var balanceBefore = await GetBalanceAsync("cash_user", "Starbucks Card");
            Assert("C01 Starting balance = 100", balanceBefore, 100m);

            await SendCashSpendAsync(channel, "cash_user", 150m); // 150 > 100
            await WaitAsync();
            var balanceAfter = await GetBalanceAsync("cash_user", "Starbucks Card");
            Assert("C01 Balance unchanged (spend rejected)", balanceAfter, 100m);
        });

        // ── C02: Spending exactly the balance → zero balance ─────────────
        await RunTest("C02 — Spending exactly the balance → balance 0 TL", async () =>
        {
            await ResetUserAsync("cash2_user");
            await SendCashAddAsync(channel, "cash2_user", 250m);
            await WaitAsync();
            await SendCashSpendAsync(channel, "cash2_user", 250m);
            await WaitAsync();
            var balance = await GetBalanceAsync("cash2_user", "Starbucks Card");
            Assert("C02 Balance = 0 TL", balance, 0m);
        });

        // ── C03: Cash top-up does not affect stars ───────────────────────
        await RunTest("C03 — Cash top-up does not affect Stars", async () =>
        {
            await ResetUserAsync("cash3_user");
            var starsBefore = await GetBalanceAsync("cash3_user", "Stars");
            await SendCashAddAsync(channel, "cash3_user", 500m);
            await WaitAsync();
            var starsAfter = await GetBalanceAsync("cash3_user", "Stars");
            Assert("C03 Stars unchanged", starsAfter, starsBefore);
            var card = await GetBalanceAsync("cash3_user", "Starbucks Card");
            Assert("C03 Card = 500 TL", card, 500m);
        });

        // ── C04: Multiple top-ups accumulate ─────────────────────────────
        await RunTest("C04 — 3 separate cash.added → balance total", async () =>
        {
            await ResetUserAsync("cash4_user");
            await SendCashAddAsync(channel, "cash4_user", 100m);
            await WaitAsync();
            await SendCashAddAsync(channel, "cash4_user", 200m);
            await WaitAsync();
            await SendCashAddAsync(channel, "cash4_user", 300m);
            await WaitAsync();
            var card = await GetBalanceAsync("cash4_user", "Starbucks Card");
            Assert("C04 Card = 600 TL", card, 600m);
        });

        // ══ MULTI-CUSTOMER ISOLATION TESTS ══════════════════════════════
        AnsiConsole.WriteLine();
        AnsiConsole.Write(new Rule("[cyan bold]Multi-Customer Isolation Tests[/]").RuleStyle("grey"));
        AnsiConsole.WriteLine();

        // ── M01: 5 different users order at the same time → no cross-effect
        await RunTest("M01 — 5 different users order → each independent", async () =>
        {
            var users = new[] { "multi_u1", "multi_u2", "multi_u3", "multi_u4", "multi_u5" };
            foreach (var u in users) await ResetUserAsync(u);

            // Send them all quickly
            foreach (var u in users)
                await SendOrderAsync(channel, u, 100m, "web", "sandwich");

            await WaitAsync(3000);

            foreach (var u in users)
            {
                var stars = await GetBalanceAsync(u, "Stars");
                // Each one: floor(100*0.30)=30 + 50 bonus = 80
                Assert($"M01 {u} Stars = 80", stars, 80m);
            }
        });

        // ── M02: Limit is counted per user (does not affect another user's limit) ─
        await RunTest("M02 — First-purchase limit is per-user, no cross-user effect", async () =>
        {
            await ResetUserAsync("iso_userA");
            await ResetUserAsync("iso_userB");

            // userA's bonus is used up
            await SendOrderAsync(channel, "iso_userA", 100m, "web", "sandwich");
            await WaitAsync();
            var aAfter1 = await GetBalanceAsync("iso_userA", "Stars");
            Assert("M02 iso_userA order 1: Stars = 80 (30+50)", aAfter1, 80m);

            // userB's bonus is still unused
            await SendOrderAsync(channel, "iso_userB", 100m, "web", "sandwich");
            await WaitAsync();
            var bAfter1 = await GetBalanceAsync("iso_userB", "Stars");
            Assert("M02 iso_userB's bonus came: Stars = 80", bAfter1, 80m);

            // userA second order → no bonus
            await SendOrderAsync(channel, "iso_userA", 100m, "web", "sandwich");
            await WaitAsync();
            var aAfter2 = await GetBalanceAsync("iso_userA", "Stars");
            Assert("M02 iso_userA order 2: no bonus, Stars = 110", aAfter2, 110m);
        });

        // ══ REDEEM EDGE CASE TESTS ════════════════════════════════════════
        AnsiConsole.WriteLine();
        AnsiConsole.Write(new Rule("[cyan bold]Redeem Edge Case Tests[/]").RuleStyle("grey"));
        AnsiConsole.WriteLine();

        // ── P01: Redeem below min points → rejected ───────────────────────
        await RunTest("P01 — Redeem below min points (500) → rejected", async () =>
        {
            await ResetUserAsync("redeem_user");
            // 1000TL → floor(300)=300 + 50 bonus = 350★ (less than 500)
            await SendOrderAsync(channel, "redeem_user", 1000m, "web", "sandwich");
            await WaitAsync();
            var starsBefore = await GetBalanceAsync("redeem_user", "Stars");

            await SendRedeemAsync(channel, "redeem_user", 499m); // 499 < 500 min
            await WaitAsync();
            var starsAfter = await GetBalanceAsync("redeem_user", "Stars");
            Assert("P01 Stars unchanged (below min)", starsAfter, starsBefore);
        });

        // ── P02: Exactly 500★ redeem → succeeds, writes 50 TL ────────────
        await RunTest("P02 — Exactly 500★ redeem → 50 TL written to the card", async () =>
        {
            await ResetUserAsync("redeem2_user");
            // 2000TL → floor(600)=600 + 50 bonus = 650★
            await SendOrderAsync(channel, "redeem2_user", 2000m, "web", "sandwich");
            await WaitAsync();
            var starsBefore = await GetBalanceAsync("redeem2_user", "Stars");
            var cardBefore  = await GetBalanceAsync("redeem2_user", "Starbucks Card");

            await SendRedeemAsync(channel, "redeem2_user", 500m);
            await WaitAsync();
            var starsAfter = await GetBalanceAsync("redeem2_user", "Stars");
            var cardAfter  = await GetBalanceAsync("redeem2_user", "Starbucks Card");
            Assert("P02 Stars −500", starsAfter, starsBefore - 500m);
            Assert("P02 Card +50 TL", cardAfter, cardBefore + 50m);
        });

        // ── P03: Cash spend after redeem → card balance correct ──────────
        await RunTest("P03 — Redeem → 50 TL earned → spend 30 TL → card 20 TL", async () =>
        {
            await ResetUserAsync("redeem3_user");
            // 2000TL → earn 650★
            await SendOrderAsync(channel, "redeem3_user", 2000m, "web", "sandwich");
            await WaitAsync();
            // 500★ → 50 TL
            await SendRedeemAsync(channel, "redeem3_user", 500m);
            await WaitAsync();
            var cardBefore = await GetBalanceAsync("redeem3_user", "Starbucks Card");
            Assert("P03 Card = 50 TL (after redeem)", cardBefore, 50m);

            await SendCashSpendAsync(channel, "redeem3_user", 30m);
            await WaitAsync();
            var cardAfter = await GetBalanceAsync("redeem3_user", "Starbucks Card");
            Assert("P03 Card = 20 TL (30 TL spent)", cardAfter, 20m);
        });

        // ── P04: Redeem exceeding the balance → rejected ──────────────────
        await RunTest("P04 — points.redeem exceeding balance → rejected", async () =>
        {
            await ResetUserAsync("redeem4_user");
            // 2000TL → 650★
            await SendOrderAsync(channel, "redeem4_user", 2000m, "web", "sandwich");
            await WaitAsync();
            var starsBefore = await GetBalanceAsync("redeem4_user", "Stars");

            await SendRedeemAsync(channel, "redeem4_user", starsBefore + 500m); // more than the balance
            await WaitAsync();
            var starsAfter = await GetBalanceAsync("redeem4_user", "Stars");
            Assert("P04 Stars unchanged (insufficient balance)", starsAfter, starsBefore);
        });

        // ══ REDIS REBUILD TESTS ═══════════════════════════════════════════
        AnsiConsole.WriteLine();
        AnsiConsole.Write(new Rule("[cyan bold]Redis Rebuild Tests[/]").RuleStyle("grey"));
        AnsiConsole.WriteLine();

        // ── RB01: per_customer_total is rebuilt correctly after Redis flush
        await RunTest("RB01 — After Redis loss, per_customer_total is correctly rebuilt from the DB", async () =>
        {
            await ResetUserAsync("rb01_user");

            // 5000TL → floor(1500)=1500★ (Kış, total remaining=3000)
            await SendOrderAsync(channel, "rb01_user", 5000m, "web", "sandwich");
            await WaitAsync();
            Assert("RB01 #1 Stars = 1550", await GetBalanceAsync("rb01_user", "Stars"), 1550m);

            // Flush Redis completely (simulates Redis "going down")
            await FlushRedisLimitsAsync("rb01_user");

            // 5000TL more → rebuild: 1500 read from DB, remaining=1500 → floor(1500)=1500★ granted
            await SendOrderAsync(channel, "rb01_user", 5000m, "web", "sandwich");
            await WaitAsync();
            Assert("RB01 #2 Stars = 3050 (1500 rebuilt + 1500 new)", await GetBalanceAsync("rb01_user", "Stars"), 3050m);

            // Now the Kış limit is full (3000), 3rd order → Temel winner
            await SendOrderAsync(channel, "rb01_user", 1000m, "web", "sandwich");
            await WaitAsync();
            Assert("RB01 #3 Stars = 3150 (Temel 100★)", await GetBalanceAsync("rb01_user", "Stars"), 3150m);
        });

        // ── RB02: Net total incl. refunds computed correctly after Redis flush
        await RunTest("RB02 — After Redis loss, refunds are counted too, net limit is correct", async () =>
        {
            await ResetUserAsync("rb02_user");

            // 10001TL → Kış 3000★ completely full
            var orderId = Guid.NewGuid().ToString();
            PublishRaw(channel, "order.created", orderId, new
            {
                contact_key = "rb02_user", amount = "10001.00",
                channel = "web", payment_method = "card",
                items = new[] { new { sku = "X", category = "sandwich", qty = 1, total = "10001.00" } }
            });
            await WaitAsync();
            Assert("RB02 #1 Stars = 3050 (3000+50)", await GetBalanceAsync("rb02_user", "Stars"), 3050m);

            // Refund the order → -3000 reclaimed from Kış
            PublishRaw(channel, "order.refunded", Guid.NewGuid().ToString(), new
            {
                contact_key = "rb02_user",
                original_event_id = orderId,
                refund_ratio = "1.0"
            });
            await WaitAsync();
            Assert("RB02 #2 Stars = 0 (full refund)", await GetBalanceAsync("rb02_user", "Stars"), 0m);

            // Flush Redis
            await FlushRedisLimitsAsync("rb02_user");

            // New order → rebuild: Earn=3000, Refund=-3000 → net=0, remaining=3000
            // Kış winner again: floor(5000*0.30)=1500★
            // İlk Alışveriş bonus: -50 taken via refund, net=0, limit reset → +50 granted
            await SendOrderAsync(channel, "rb02_user", 5000m, "web", "sandwich");
            await WaitAsync();
            Assert("RB02 #3 Stars = 1550 (Kış 1500 + bonus 50)", await GetBalanceAsync("rb02_user", "Stars"), 1550m);
        });

        // ── RB03: per_customer_per_day is rebuilt correctly after Redis flush
        await RunTest("RB03 — After Redis loss, the daily limit is correctly rebuilt from the DB", async () =>
        {
            await ResetUserAsync("rb03_user");

            // Fill Kış
            await SendOrderAsync(channel, "rb03_user", 10001m, "web", "sandwich");
            await WaitAsync();
            Assert("RB03 #0 Stars = 3050", await GetBalanceAsync("rb03_user", "Stars"), 3050m);

            // Earn 400★ from Temel (4000TL → floor(400)=400)
            await SendOrderAsync(channel, "rb03_user", 4000m, "web", "sandwich");
            await WaitAsync();
            Assert("RB03 #1 Stars = 3450 (Temel 400★)", await GetBalanceAsync("rb03_user", "Stars"), 3450m);

            // Flush Redis
            await FlushRedisLimitsAsync("rb03_user");

            // Rebuild: daily Temel=400, remaining=100 → 1500TL → floor(150) > 100 → clipped → 100★
            await SendOrderAsync(channel, "rb03_user", 1500m, "web", "sandwich");
            await WaitAsync();
            Assert("RB03 #2 Stars = 3550 (100★ clipped)", await GetBalanceAsync("rb03_user", "Stars"), 3550m);

            // Daily limit full → 0★
            await SendOrderAsync(channel, "rb03_user", 1000m, "web", "sandwich");
            await WaitAsync();
            Assert("RB03 #3 Stars = 3550 (daily full)", await GetBalanceAsync("rb03_user", "Stars"), 3550m);
        });

        // ══ DEEP REFUND EDGE CASES ════════════════════════════════════════
        AnsiConsole.WriteLine();
        AnsiConsole.Write(new Rule("[cyan bold]Deep Refund Edge Cases[/]").RuleStyle("grey"));
        AnsiConsole.WriteLine();

        // ── RF01: Refund with missing original_event_id → error, event inbox failed
        await RunTest("RF01 — Refund with a completely made-up original_event_id → error, no ledger written at all", async () =>
        {
            await ResetUserAsync("rf01_user");
            await SendOrderAsync(channel, "rf01_user", 500m, "web", "sandwich");
            await WaitAsync();
            var starsBefore = await GetBalanceAsync("rf01_user", "Stars");

            // Not a real order's event_id, a made-up UUID
            PublishRaw(channel, "order.refunded", Guid.NewGuid().ToString(), new
            {
                contact_key       = "rf01_user",
                original_event_id = Guid.NewGuid().ToString(),
                refund_ratio      = "1.0"
            });
            await WaitAsync();
            var starsAfter = await GetBalanceAsync("rf01_user", "Stars");
            Assert("RF01 Stars unchanged", starsAfter, starsBefore);
            // Ledger must contain only earn entries, no refund entry
            var entryCount = await GetLedgerEntryCountAsync("rf01_user");
            Assert("RF01 Ledger entry count unchanged (2: earn+bonus)", entryCount, 2);
        });

        // ── RF02: Refund ratio=0 → no points reclaimed
        await RunTest("RF02 — Refund ratio=0.0 → points unchanged", async () =>
        {
            await ResetUserAsync("rf02_user");
            var orderId = Guid.NewGuid().ToString();
            PublishRaw(channel, "order.created", orderId, new
            {
                contact_key = "rf02_user", amount = "300.00",
                channel = "web", payment_method = "card",
                items = new[] { new { sku = "X", category = "sandwich", qty = 1, total = "300.00" } }
            });
            await WaitAsync();
            var starsAfterOrder = await GetBalanceAsync("rf02_user", "Stars");

            PublishRaw(channel, "order.refunded", Guid.NewGuid().ToString(), new
            {
                contact_key       = "rf02_user",
                original_event_id = orderId,
                refund_ratio      = "0.0"
            });
            await WaitAsync();
            var starsAfterRefund = await GetBalanceAsync("rf02_user", "Stars");
            // delta = entry.delta * 0 = 0 → never written (delta <= 0 guard)
            Assert("RF02 Stars unchanged (ratio=0)", starsAfterRefund, starsAfterOrder);
        });

        // ── RF03: Refund, then refund the same order again → idempotent
        await RunTest("RF03 — Same order fully refunded twice → idempotent, points taken back once", async () =>
        {
            await ResetUserAsync("rf03_user");
            var orderId = Guid.NewGuid().ToString();
            PublishRaw(channel, "order.created", orderId, new
            {
                contact_key = "rf03_user", amount = "200.00",
                channel = "web", payment_method = "card",
                items = new[] { new { sku = "X", category = "sandwich", qty = 1, total = "200.00" } }
            });
            await WaitAsync();

            var refundId = Guid.NewGuid().ToString();
            // Send the same refund event_id twice
            for (int i = 0; i < 2; i++)
            {
                PublishRaw(channel, "order.refunded", refundId, new
                {
                    contact_key       = "rf03_user",
                    original_event_id = orderId,
                    refund_ratio      = "1.0"
                });
                await Task.Delay(200);
            }
            await WaitAsync(2000);
            var starsAfter = await GetBalanceAsync("rf03_user", "Stars");
            // floor(200*0.30)=60 + 50 bonus - 60 - 50 = 0
            Assert("RF03 Full refund applied once, Stars = 0", starsAfter, 0m);
        });

        // ── RF04: Over-refund prevented — even if ratio > 1 is sent, it is clamped to 1.0
        await RunTest("RF04 — Refund ratio=1.5 → clamped to 1.0, at most a full refund (balance never goes negative)", async () =>
        {
            await ResetUserAsync("rf04_user");
            var orderId = Guid.NewGuid().ToString();
            PublishRaw(channel, "order.created", orderId, new
            {
                contact_key = "rf04_user", amount = "100.00",
                channel = "web", payment_method = "card",
                items = new[] { new { sku = "X", category = "sandwich", qty = 1, total = "100.00" } }
            });
            await WaitAsync();
            var starsAfterOrder = await GetBalanceAsync("rf04_user", "Stars");
            Assert("RF04 order earning: floor(100*0.30)=30 + 50 bonus", starsAfterOrder, 80m);

            PublishRaw(channel, "order.refunded", Guid.NewGuid().ToString(), new
            {
                contact_key       = "rf04_user",
                original_event_id = orderId,
                refund_ratio      = "1.5"
            });
            await WaitAsync();
            var starsAfterRefund = await GetBalanceAsync("rf04_user", "Stars");
            // ratio clamped to 1.0 → full refund (80) → balance 0, never negative
            Assert("RF04 ratio=1.5 → clamp 1.0, balance after full refund", starsAfterRefund, 0m);
        });

        // ── RF05: Refund a stamp order → both points and stamp are reclaimed
        await RunTest("RF05 — Refunding a coffee order → both Stars and Stamp are reclaimed", async () =>
        {
            await ResetUserAsync("rf05_user");
            var orderId = Guid.NewGuid().ToString();
            PublishRaw(channel, "order.created", orderId, new
            {
                contact_key = "rf05_user", amount = "100.00",
                channel = "store", payment_method = "card",
                items = new[] { new { sku = "X", category = "coffee", qty = 1, total = "100.00" } }
            });
            await WaitAsync();
            var stampAfterOrder = await GetBalanceAsync("rf05_user", "Kahve Damgası");
            var starsAfterOrder = await GetBalanceAsync("rf05_user", "Stars");
            Assert("RF05 Stamp = 1", stampAfterOrder, 1m);
            // floor(100*0.30)=30 (Kış StampEarn) + 50 bonus (Earn) = 80
            Assert("RF05 Stars = 80", starsAfterOrder, 80m);

            PublishRaw(channel, "order.refunded", Guid.NewGuid().ToString(), new
            {
                contact_key       = "rf05_user",
                original_event_id = orderId,
                refund_ratio      = "1.0"
            });
            await WaitAsync();
            var starsAfterRefund = await GetBalanceAsync("rf05_user", "Stars");
            var stampAfterRefund = await GetBalanceAsync("rf05_user", "Kahve Damgası");
            // Stars: -30 (Kış earn) - 50 (bonus) = 0
            Assert("RF05 Stars = 0 (all earns reclaimed)", starsAfterRefund, 0m);
            // Stamp: the StampEarn entry is also covered by the Refund → stamp -1 → 0
            Assert("RF05 Stamp = 0 (StampEarn was also refunded)", stampAfterRefund, 0m);
        });

        // ══ ZERO AND NEGATIVE VALUE EDGE CASES ═══════════════════════════
        AnsiConsole.WriteLine();
        AnsiConsole.Write(new Rule("[cyan bold]Zero and Negative Value Edge Cases[/]").RuleStyle("grey"));
        AnsiConsole.WriteLine();

        // ── Z01: 0 TL order → delta=0, never written
        await RunTest("Z01 — 0.00 TL order → no points, no stamp", async () =>
        {
            await ResetUserAsync("zero_user");
            await SendOrderAsync(channel, "zero_user", 0m, "web", "sandwich");
            await WaitAsync();
            var stars = await GetBalanceAsync("zero_user", "Stars");
            // floor(0 * 0.30) = 0, delta=0 → guard: delta <= 0 → skip
            // İlk Alışveriş fixed=50, stackable, delta=50 > 0 → granted
            Assert("Z01 Stars = 50 (first-purchase bonus only)", stars, 50m);
        });

        // ── Z02: 0.01 TL order → floor(0.01 * 0.30) = 0 → no points
        await RunTest("Z02 — 0.01 TL order → Kış floor(0.003)=0 → no points, bonus still applies", async () =>
        {
            await ResetUserAsync("zero2_user");
            await SendOrderAsync(channel, "zero2_user", 0.01m, "web", "sandwich");
            await WaitAsync();
            var stars = await GetBalanceAsync("zero2_user", "Stars");
            // floor(0.01 * 0.30) = floor(0.003) = 0 → delta=0 guard skip
            // İlk Alışveriş fixed=50 → granted
            Assert("Z02 Stars = 50 (bonus only, Kış delta=0 skipped)", stars, 50m);
        });

        // ── Z03: Stars never goes negative after redeem (balance check)
        await RunTest("Z03 — Redeem while Stars is exactly zero → rejected", async () =>
        {
            await ResetUserAsync("zero3_user");
            // No orders at all → Stars = 0
            var starsBefore = await GetBalanceAsync("zero3_user", "Stars");
            Assert("Z03 Starting Stars = 0", starsBefore, 0m);

            await SendRedeemAsync(channel, "zero3_user", 500m);
            await WaitAsync();
            var starsAfter = await GetBalanceAsync("zero3_user", "Stars");
            Assert("Z03 Stars still 0 (redeem rejected)", starsAfter, 0m);
        });

        // ── Z04: cash.spent with no cash account at all → error
        await RunTest("Z04 — No top-up made at all, cash.spent → rejected", async () =>
        {
            await ResetUserAsync("zero4_user");
            var cardBefore = await GetBalanceAsync("zero4_user", "Starbucks Card");
            Assert("Z04 Card = 0 (no account)", cardBefore, 0m);

            await SendCashSpendAsync(channel, "zero4_user", 50m);
            await WaitAsync();
            var cardAfter = await GetBalanceAsync("zero4_user", "Starbucks Card");
            Assert("Z04 Card still 0 (spend rejected)", cardAfter, 0m);
        });

        // ══ RULE ACTIVATION DATE EDGE CASES ══════════════════════════════
        AnsiConsole.WriteLine();
        AnsiConsole.Write(new Rule("[cyan bold]Rule Activation Date Edge Cases[/]").RuleStyle("grey"));
        AnsiConsole.WriteLine();

        // ── A01: Kış Kampanyası active_to=2026-12-31 → active, must apply
        await RunTest("A01 — Kış Kampanyası active_to=2026-12-31, today 2026-06-30 → active", async () =>
        {
            await ResetUserAsync("active_user");
            await SendOrderAsync(channel, "active_user", 100m, "web", "sandwich");
            await WaitAsync();
            var stars = await GetBalanceAsync("active_user", "Stars");
            // Kış active: floor(100*0.30)=30 + 50 bonus = 80
            Assert("A01 Kış active, Stars = 80", stars, 80m);
        });

        // ── A02: Rule with past active_to is disabled (change temporarily in DB)
        await RunTest("A02 — If Kış Kampanyası's active_to is moved to the past, Temel (p:10) becomes winner", async () =>
        {
            await ResetUserAsync("active2_user");

            // Temporarily end Kış yesterday
            await using var pg = new Npgsql.NpgsqlConnection(PG);
            await pg.OpenAsync();
            await using (var cmd = new Npgsql.NpgsqlCommand(
                "UPDATE rules SET active_to='2026-01-01' WHERE id='018fcd03-0000-7000-8000-000000000004'", pg))
                await cmd.ExecuteNonQueryAsync();

            // Clear the rule cache: the consumer must reload the rules
            // RuleSync refreshes every 30s; to change the DB and invalidate the cache
            // we either bump cache_version or wait for the consumer
            // Simplest: delete the RuleCache from Redis, not via LimitCache directly
            await FlushRuleCacheAsync();
            await Task.Delay(2000); // wait for cache reload

            await SendOrderAsync(channel, "active2_user", 1000m, "web", "sandwich");
            await WaitAsync(2000);
            var stars = await GetBalanceAsync("active2_user", "Stars");
            // Kış disabled → Temel (p:10) winner: floor(1000*0.10)=100 + 50 bonus = 150
            Assert("A02 Kış disabled, Temel winner, Stars = 150", stars, 150m);

            // Revert
            await using (var cmd = new Npgsql.NpgsqlCommand(
                "UPDATE rules SET active_to='2026-12-31' WHERE id='018fcd03-0000-7000-8000-000000000004'", pg))
                await cmd.ExecuteNonQueryAsync();
            await FlushRuleCacheAsync();
            await Task.Delay(2000);
        });

        // ══ LEDGER INTEGRITY TESTS ════════════════════════════════════════
        AnsiConsole.WriteLine();
        AnsiConsole.Write(new Rule("[cyan bold]Ledger Integrity Tests[/]").RuleStyle("grey"));
        AnsiConsole.WriteLine();

        // ── LB01: balance = SUM(delta) on all accounts — no drift
        await RunTest("LB01 — cached_balance = SUM(ledger delta) on all accounts, drift = 0", async () =>
        {
            await using var pg = new Npgsql.NpgsqlConnection(PG);
            await pg.OpenAsync();
            const string sql = """
                SELECT COUNT(*) FROM customer_accounts ca
                LEFT JOIN (
                    SELECT customer_account_id, SUM(delta) as total
                    FROM ledger_entries GROUP BY customer_account_id
                ) le ON le.customer_account_id = ca.id
                WHERE ca.tenant_id = 'starbucks'
                  AND ca.balance != COALESCE(le.total, 0)
                """;
            await using var cmd = new Npgsql.NpgsqlCommand(sql, pg);
            var driftCount = Convert.ToInt32(await cmd.ExecuteScalarAsync());
            Assert("LB01 Drift = 0 accounts", driftCount, 0);
        });

        // ── LB02: No duplicate idempotency keys at all
        await RunTest("LB02 — No duplicate idempotency_key in the ledger", async () =>
        {
            await using var pg = new Npgsql.NpgsqlConnection(PG);
            await pg.OpenAsync();
            const string sql = """
                SELECT COUNT(*) FROM (
                    SELECT idempotency_key FROM ledger_entries
                    WHERE tenant_id = 'starbucks'
                    GROUP BY idempotency_key HAVING COUNT(*) > 1
                ) dups
                """;
            await using var cmd = new Npgsql.NpgsqlCommand(sql, pg);
            var dupCount = Convert.ToInt32(await cmd.ExecuteScalarAsync());
            Assert("LB02 Duplicate idempotency_key = 0", dupCount, 0);
        });

        // ── LB03: Every reward_log has a matching ledger reset entry
        await RunTest("LB03 — Every reward_log row has a matching ledger reset entry", async () =>
        {
            await using var pg = new Npgsql.NpgsqlConnection(PG);
            await pg.OpenAsync();
            const string sql = """
                SELECT COUNT(*) FROM reward_log rl
                WHERE rl.tenant_id = 'starbucks'
                  AND NOT EXISTS (
                      SELECT 1 FROM ledger_entries le
                      WHERE le.id = rl.ledger_reset_entry_id
                  )
                """;
            await using var cmd = new Npgsql.NpgsqlCommand(sql, pg);
            var orphanCount = Convert.ToInt32(await cmd.ExecuteScalarAsync());
            Assert("LB03 Orphan reward_log = 0", orphanCount, 0);
        });

        // ── LB04: All refund entries carry negative delta
        await RunTest("LB04 — All Refund-reason entries carry a negative delta", async () =>
        {
            await using var pg = new Npgsql.NpgsqlConnection(PG);
            await pg.OpenAsync();
            const string sql = """
                SELECT COUNT(*) FROM ledger_entries
                WHERE tenant_id = 'starbucks'
                  AND reason = 'Refund'
                  AND delta >= 0
                """;
            await using var cmd = new Npgsql.NpgsqlCommand(sql, pg);
            var badCount = Convert.ToInt32(await cmd.ExecuteScalarAsync());
            Assert("LB04 Refund entries with positive delta = 0", badCount, 0);
        });

        // ── LB05: StampReset entries always negative, StampEarn positive
        await RunTest("LB05 — StampReset carries negative delta, StampEarn positive", async () =>
        {
            await using var pg = new Npgsql.NpgsqlConnection(PG);
            await pg.OpenAsync();
            const string sqlReset = """
                SELECT COUNT(*) FROM ledger_entries
                WHERE tenant_id='starbucks' AND reason='StampReset' AND delta >= 0
                """;
            const string sqlEarn = """
                SELECT COUNT(*) FROM ledger_entries
                WHERE tenant_id='starbucks' AND reason='StampEarn' AND delta <= 0
                """;
            await using var cmd1 = new Npgsql.NpgsqlCommand(sqlReset, pg);
            var badReset = Convert.ToInt32(await cmd1.ExecuteScalarAsync());
            await using var cmd2 = new Npgsql.NpgsqlCommand(sqlEarn, pg);
            var badEarn = Convert.ToInt32(await cmd2.ExecuteScalarAsync());
            Assert("LB05 Positive StampReset = 0", badReset, 0);
            Assert("LB05 Negative StampEarn = 0", badEarn, 0);
        });

        // ══ CHAINED SCENARIO TESTS ════════════════════════════════════════
        AnsiConsole.WriteLine();
        AnsiConsole.Write(new Rule("[cyan bold]Chained Scenario Tests[/]").RuleStyle("grey"));
        AnsiConsole.WriteLine();

        // ── S01: Full cycle — earn → redeem → spend → earn again
        await RunTest("S01 — Full cycle: earn → redeem → spend → earn again", async () =>
        {
            await ResetUserAsync("chain_user");

            // 1. Order: 2000TL → floor(600)=600 + 50 = 650★
            await SendOrderAsync(channel, "chain_user", 2000m, "web", "sandwich");
            await WaitAsync();
            Assert("S01 #1 Stars = 650", await GetBalanceAsync("chain_user", "Stars"), 650m);

            // 2. Redeem 500★ → 50 TL card
            await SendRedeemAsync(channel, "chain_user", 500m);
            await WaitAsync();
            Assert("S01 #2 Stars = 150", await GetBalanceAsync("chain_user", "Stars"), 150m);
            Assert("S01 #2 Card = 50", await GetBalanceAsync("chain_user", "Starbucks Card"), 50m);

            // 3. Top up 100 TL cash
            await SendCashAddAsync(channel, "chain_user", 100m);
            await WaitAsync();
            Assert("S01 #3 Card = 150", await GetBalanceAsync("chain_user", "Starbucks Card"), 150m);

            // 4. Spend 50 TL
            await SendCashSpendAsync(channel, "chain_user", 50m);
            await WaitAsync();
            Assert("S01 #4 Card = 100", await GetBalanceAsync("chain_user", "Starbucks Card"), 100m);

            // 5. 2nd order: 1000TL → floor(300)=300★ (Kış total: 600+300=900 < 3000)
            await SendOrderAsync(channel, "chain_user", 1000m, "web", "sandwich");
            await WaitAsync();
            Assert("S01 #5 Stars = 450", await GetBalanceAsync("chain_user", "Stars"), 450m);
        });

        // ── S02: Order + refund + reorder of same amount → limit continues correctly
        await RunTest("S02 — Order → refund → new order: the Redis limit isn't affected by the refund", async () =>
        {
            await ResetUserAsync("chain2_user");

            var orderId = Guid.NewGuid().ToString();
            // 1. Order: 5000TL → floor(1500)=1500★
            PublishRaw(channel, "order.created", orderId, new
            {
                contact_key = "chain2_user", amount = "5000.00",
                channel = "web", payment_method = "card",
                items = new[] { new { sku = "X", category = "sandwich", qty = 1, total = "5000.00" } }
            });
            await WaitAsync();
            var starsAfter1 = await GetBalanceAsync("chain2_user", "Stars");
            Assert("S02 #1 Stars = 1550 (1500+50)", starsAfter1, 1550m);

            // 2. Full refund → -1500 - 50 = 0★
            PublishRaw(channel, "order.refunded", Guid.NewGuid().ToString(), new
            {
                contact_key = "chain2_user",
                original_event_id = orderId,
                refund_ratio = "1.0"
            });
            await WaitAsync();
            Assert("S02 #2 Stars = 0 (refund)", await GetBalanceAsync("chain2_user", "Stars"), 0m);

            // 3. New order: 5000TL → limit in Redis still counts 1500, remaining=1500
            // Kış: floor(5000*0.30)=1500, remaining=3000-1500=1500 → exactly 1500 granted
            await SendOrderAsync(channel, "chain2_user", 5000m, "web", "sandwich");
            await WaitAsync();
            var starsAfter3 = await GetBalanceAsync("chain2_user", "Stars");
            // Bonus: İlk Alışveriş was already used (limit=50 total), not granted
            Assert("S02 #3 Stars = 1500 (Kış remaining=1500)", starsAfter3, 1500m);
        });

        // ── S03: 30 coffees → 3 free_drink, stamp cycle counts correctly
        await RunTest("S03 — 30 coffee orders → 3 free_drink, completion_count 1,2,3", async () =>
        {
            await ResetUserAsync("chain3_user");
            for (int i = 0; i < 30; i++)
            {
                await SendOrderAsync(channel, "chain3_user", 50m, "store", "coffee");
                await Task.Delay(250);
            }
            await WaitAsync(6000);

            var stamp  = await GetBalanceAsync("chain3_user", "Kahve Damgası");
            var drinks = await GetRewardCountAsync("chain3_user", "free_drink");
            Assert("S03 Stamp = 0 (3 cycles completed)", stamp, 0m);
            Assert("S03 3 free_drink earned", drinks, 3);

            // Are completion_counts 1, 2, 3 in order?
            var counts = await GetRewardCompletionCountsAsync("chain3_user");
            Assert("S03 completion_count[0] = 1", counts[0], 1);
            Assert("S03 completion_count[1] = 2", counts[1], 2);
            Assert("S03 completion_count[2] = 3", counts[2], 3);
        });

        // ── S04: Cash add, spend, add, spend: balance correct at each step
        await RunTest("S04 — Sequential cash load/spend chain: balance consistent at every step", async () =>
        {
            await ResetUserAsync("chain4_user");

            await SendCashAddAsync(channel,  "chain4_user", 300m); await WaitAsync();
            Assert("S04 #1 = 300", await GetBalanceAsync("chain4_user", "Starbucks Card"), 300m);

            await SendCashSpendAsync(channel, "chain4_user", 120m); await WaitAsync();
            Assert("S04 #2 = 180", await GetBalanceAsync("chain4_user", "Starbucks Card"), 180m);

            await SendCashAddAsync(channel,  "chain4_user", 200m); await WaitAsync();
            Assert("S04 #3 = 380", await GetBalanceAsync("chain4_user", "Starbucks Card"), 380m);

            await SendCashSpendAsync(channel, "chain4_user", 380m); await WaitAsync();
            Assert("S04 #4 = 0",   await GetBalanceAsync("chain4_user", "Starbucks Card"), 0m);

            await SendCashSpendAsync(channel, "chain4_user", 1m);   await WaitAsync();
            Assert("S04 #5 = 0 (spend rejected)", await GetBalanceAsync("chain4_user", "Starbucks Card"), 0m);
        });

        // ══ FIX REGRESSION TESTS ═══════════════════════════════════════════
        AnsiConsole.WriteLine();
        AnsiConsole.Write(new Rule("[cyan bold]Fix Regression Tests[/]").RuleStyle("grey"));
        AnsiConsole.WriteLine();

        // ── RG01: Cumulative refund cap — two SEPARATE partial refund events must
        //          not exceed 100% of the original earn in total (RF03 tests the
        //          same event's idempotency; this test uses different event ids)
        await RunTest("RG01 — Two separate partial refunds (0.6 + 0.6) → total refund caps at 100%", async () =>
        {
            await ResetUserAsync("rg01_user");
            var orderId = Guid.NewGuid().ToString();
            PublishRaw(channel, "order.created", orderId, new
            {
                contact_key = "rg01_user", amount = "100.00",
                channel = "web", payment_method = "card",
                items = new[] { new { sku = "X", category = "sandwich", qty = 1, total = "100.00" } }
            });
            await WaitAsync();
            Assert("RG01 order: floor(100*0.30)=30 + 50 bonus", await GetBalanceAsync("rg01_user", "Stars"), 80m);

            // 1st refund 60%: 30*0.6=18 + 50*0.6=30 → -48
            PublishRaw(channel, "order.refunded", Guid.NewGuid().ToString(), new
            {
                contact_key = "rg01_user", original_event_id = orderId, refund_ratio = "0.60"
            });
            await WaitAsync();
            Assert("RG01 balance after the first 60% refund", await GetBalanceAsync("rg01_user", "Stars"), 32m);

            // 2nd refund 60% (DIFFERENT event id): must be clipped to remaining 40% → -32, balance 0
            PublishRaw(channel, "order.refunded", Guid.NewGuid().ToString(), new
            {
                contact_key = "rg01_user", original_event_id = orderId, refund_ratio = "0.60"
            });
            await WaitAsync();
            Assert("RG01 second refund clipped to the remaining 40%, balance 0 (not negative)",
                await GetBalanceAsync("rg01_user", "Stars"), 0m);
        });

        // ── RG02: First order after a period reset must not downgrade the tier.
        //          The event flow is upgrade-only; downgrade only in the nightly job.
        await RunTest("RG02 — The first order after a period reset does not downgrade the tier (upgrade-only)", async () =>
        {
            await ResetUserAsync("rg02_user");
            await ExecuteSqlAsync("DELETE FROM tier_upgrade_log WHERE tenant_id='starbucks' AND contact_key='rg02_user'");

            // Earn 1010★ → altin (threshold 1000)
            PublishRaw(channel, "order.created", Guid.NewGuid().ToString(), new
            {
                contact_key = "rg02_user", amount = "3200.00",
                channel = "web", payment_method = "card",
                items = new[] { new { sku = "X", category = "sandwich", qty = 1, total = "3200.00" } }
            });
            await WaitAsync(2000);
            Assert("RG02 earning: floor(3200*0.30)=960 + 50 bonus", await GetBalanceAsync("rg02_user", "Stars"), 1010m);
            Assert("RG02 precondition: tier upgraded to altin", await GetTierNameAsync("rg02_user"), "altin");

            // Move the earns into the past — keep them out of the new period window
            await ExecuteSqlAsync("""
                UPDATE ledger_entries SET created_at = created_at - interval '100 days'
                WHERE tenant_id='starbucks' AND contact_key='rg02_user'
                """);

            // Simulate the nightly job's period reset: tier stays altin, period starts today
            await ExecuteSqlAsync("""
                UPDATE customer_accounts ca
                SET tier_qualifying_pts = 0,
                    tier_period_start   = CURRENT_DATE,
                    tier_expires_at     = NULL
                FROM account_types at
                WHERE at.id = ca.account_type_id
                  AND ca.tenant_id = 'starbucks'
                  AND ca.contact_key = 'rg02_user'
                  AND at.name = 'Stars'
                """);

            // Small order in the new period: floor(10*0.30)=3★ — old code downgraded the tier to yesil
            PublishRaw(channel, "order.created", Guid.NewGuid().ToString(), new
            {
                contact_key = "rg02_user", amount = "10.00",
                channel = "web", payment_method = "card",
                items = new[] { new { sku = "X", category = "sandwich", qty = 1, total = "10.00" } }
            });
            await WaitAsync(2000);

            Assert("RG02 tier still altin after the small order", await GetTierNameAsync("rg02_user"), "altin");

            var downgradeLogs = await ScalarIntAsync("""
                SELECT COUNT(*) FROM tier_upgrade_log tul
                JOIN tier_definitions td ON td.id = tul.to_tier_id
                WHERE tul.tenant_id='starbucks' AND tul.contact_key='rg02_user' AND td.name='yesil'
                """);
            Assert("RG02 no downgrade log entry to yesil", downgradeLogs, 0);
        });

        // ── RG03: Poison message is not lost — nacked event lands in the DLQ
        await RunTest("RG03 — An unprocessable event is nacked and lands in the DLQ (not lost)", async () =>
        {
            var dlqBefore = (int)channel.QueueDeclarePassive("q.loyalty.dlq").MessageCount;

            var poisonEventId = Guid.NewGuid().ToString();
            PublishRaw(channel, "order.refunded", poisonEventId, new
            {
                contact_key       = "rg03_user",
                original_event_id = "no-such-order-" + Guid.NewGuid(),
                refund_ratio      = "1.0"
            });
            await WaitAsync(2500);

            var dlqAfter = (int)channel.QueueDeclarePassive("q.loyalty.dlq").MessageCount;
            Assert("RG03 DLQ message count +1", dlqAfter, dlqBefore + 1);

            var status = await ScalarStringAsync(
                $"SELECT status FROM event_inbox WHERE tenant_id='starbucks' AND event_id='{poisonEventId}'");
            Assert("RG03 event_inbox status", status, "failed");
        });

        // ══ RESULT ────────────────────────────────────────────────────────
        AnsiConsole.WriteLine();
        AnsiConsole.Write(new Rule("[bold]Result[/]").RuleStyle("grey"));
        AnsiConsole.MarkupLine($"[green]PASSED: {_passed}[/]  [red]FAILED: {_failed}[/]  TOTAL: {_passed + _failed}");

        if (_failed == 0)
            AnsiConsole.MarkupLine("[bold green]✓ All tests passed![/]");
        else
            AnsiConsole.MarkupLine("[bold red]✗ Some tests failed![/]");
    }

    // ── Helpers ──────────────────────────────────────────────────────────

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

    // On failure the counter is NOT incremented — RunTest's catch counts the
    // throwing assert once. Incrementing here too would double-count one failure.
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

    static async Task ResetAsync()
    {
        await using var db = new NpgsqlConnection(PG);
        await db.OpenAsync();
        foreach (var sql in new[]
        {
            "DELETE FROM reward_log WHERE tenant_id = 'starbucks'",
            "DELETE FROM ledger_entries WHERE tenant_id = 'starbucks'",
            "DELETE FROM customer_accounts WHERE tenant_id = 'starbucks'",
            "DELETE FROM event_inbox WHERE tenant_id = 'starbucks'"
        })
        {
            await using var cmd = new NpgsqlCommand(sql, db);
            await cmd.ExecuteNonQueryAsync();
        }
        AnsiConsole.MarkupLine("[grey]DB reset.[/]");

        await FlushRedisLimitsAsync(null); // all starbucks limit keys
        AnsiConsole.MarkupLine("[grey]Redis limit cache reset.[/]\n");
    }

    static async Task FlushRedisLimitsAsync(string? contactKey)
    {
        var pattern = contactKey is null
            ? "limit:starbucks:*"
            : $"limit:starbucks:*:{contactKey}:*";

        var lua = "local ks=redis.call('keys',ARGV[1]) for _,k in ipairs(ks) do redis.call('del',k) end return #ks";

        var psi = new ProcessStartInfo("/usr/local/bin/docker")
        {
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            UseShellExecute        = false
        };
        psi.ArgumentList.Add("exec");
        psi.ArgumentList.Add("loyalty-redis-1");
        psi.ArgumentList.Add("redis-cli");
        psi.ArgumentList.Add("eval");
        psi.ArgumentList.Add(lua);
        psi.ArgumentList.Add("0");
        psi.ArgumentList.Add(pattern);

        using var proc = Process.Start(psi)!;
        // Consume stdout and stderr concurrently — deadlock prevention
        var stdoutTask = proc.StandardOutput.ReadToEndAsync();
        var stderrTask = proc.StandardError.ReadToEndAsync();
        await Task.WhenAll(stdoutTask, stderrTask);
        await proc.WaitForExitAsync();
    }

    static async Task ResetUserAsync(string contactKey)
    {
        await using var db = new NpgsqlConnection(PG);
        await db.OpenAsync();
        foreach (var sql in new[]
        {
            $"DELETE FROM reward_log WHERE tenant_id='starbucks' AND contact_key='{contactKey}'",
            $"DELETE FROM ledger_entries WHERE tenant_id='starbucks' AND contact_key='{contactKey}'",
            $"DELETE FROM customer_accounts WHERE tenant_id='starbucks' AND contact_key='{contactKey}'",
        })
        {
            await using var cmd = new NpgsqlCommand(sql, db);
            await cmd.ExecuteNonQueryAsync();
        }

        // Clear the Redis limit cache for this user
        await FlushRedisLimitsAsync(contactKey);
    }

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

    static async Task<int> GetRewardCountAsync(string contactKey, string rewardType)
    {
        await using var db = new NpgsqlConnection(PG);
        await db.OpenAsync();
        const string sql = "SELECT COUNT(*) FROM reward_log WHERE tenant_id=@t AND contact_key=@c AND reward_type=@r";
        await using var cmd = new NpgsqlCommand(sql, db);
        cmd.Parameters.AddWithValue("t", TENANT);
        cmd.Parameters.AddWithValue("c", contactKey);
        cmd.Parameters.AddWithValue("r", rewardType);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync());
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

    static async Task<string?> GetTierNameAsync(string contactKey)
    {
        return await ScalarStringAsync($"""
            SELECT td.name FROM customer_accounts ca
            JOIN tier_definitions td ON td.id = ca.tier_id
            JOIN account_types at ON at.id = ca.account_type_id
            WHERE ca.tenant_id='starbucks' AND ca.contact_key='{contactKey}' AND at.name='Stars'
            """);
    }

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

    static async Task SendCashAddAsync(IModel ch, string contact, decimal amount)
    {
        PublishRaw(ch, "cash.added", Guid.NewGuid().ToString(), new
        {
            contact_key     = contact,
            amount          = amount.ToString("F2", System.Globalization.CultureInfo.InvariantCulture),
            account_type_id = "018fcd02-0000-7000-8000-000000000002"
        });
        await Task.CompletedTask;
    }

    static async Task SendCashSpendAsync(IModel ch, string contact, decimal amount)
    {
        PublishRaw(ch, "cash.spent", Guid.NewGuid().ToString(), new
        {
            contact_key     = contact,
            amount          = amount.ToString("F2", System.Globalization.CultureInfo.InvariantCulture),
            account_type_id = "018fcd02-0000-7000-8000-000000000002"
        });
        await Task.CompletedTask;
    }

    static async Task SendRedeemAsync(IModel ch, string contact, decimal points)
    {
        PublishRaw(ch, "points.redeem", Guid.NewGuid().ToString(), new
        {
            contact_key            = contact,
            points_amount          = points.ToString("F2", System.Globalization.CultureInfo.InvariantCulture),
            source_account_type_id = "018fcd02-0000-7000-8000-000000000001"
        });
        await Task.CompletedTask;
    }

    static async Task<int> GetLedgerEntryCountAsync(string contactKey)
    {
        await using var db = new NpgsqlConnection(PG);
        await db.OpenAsync();
        const string sql = "SELECT COUNT(*) FROM ledger_entries WHERE tenant_id=@t AND contact_key=@c";
        await using var cmd = new NpgsqlCommand(sql, db);
        cmd.Parameters.AddWithValue("t", TENANT);
        cmd.Parameters.AddWithValue("c", contactKey);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync());
    }

    static async Task<List<int>> GetRewardCompletionCountsAsync(string contactKey)
    {
        await using var db = new NpgsqlConnection(PG);
        await db.OpenAsync();
        const string sql = "SELECT completion_count FROM reward_log WHERE tenant_id=@t AND contact_key=@c ORDER BY completion_count";
        await using var cmd = new NpgsqlCommand(sql, db);
        cmd.Parameters.AddWithValue("t", TENANT);
        cmd.Parameters.AddWithValue("c", contactKey);
        var result = new List<int>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            result.Add(reader.GetInt32(0));
        return result;
    }

    static async Task FlushRuleCacheAsync()
    {
        // Delete the rules:{tenantId}:{programId} keys → so the consumer reloads its cache
        var lua = "local ks=redis.call('keys','rules:starbucks:*') for _,k in ipairs(ks) do redis.call('del',k) end return #ks";
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
}
