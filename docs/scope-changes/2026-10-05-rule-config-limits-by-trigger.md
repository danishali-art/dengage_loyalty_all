# Scope Change Impact Analysis: Rule Configuration & Limits by Trigger Event

**Status:** **All recommendations agreed by the stakeholders** (D2–D24, E1–E3; relayed by the developer, Moiz). No question is open. **Phase 0, Part 1a, Part 1b, Phase 2, Phase 4 and Phase 5 implemented and verified on 2026-10-08** (§7.3, §7.5, §7.7, §7.9, §7.11) on branch `1.8-rule-confg-limits-by-trigger`, pending the Architect's diff review. Every phase of this CR is now built; each still goes through its own Jira Scope Change Request (§7, D19).
**Date:** 2026-10-08 (revision 26; revisions 1–25 were 2026-10-05 to 2026-10-08)
**Author:** Claude Code, at the request of Moiz (acting as BA/PO)
**Type:** Mixed — scope changes, bug fixes and UI-only changes (table below).
**Basis:** code read on 2026-10-06, after commits `a176a30` (1.6), `fe101a5` (1.7) and `7bd368b` (1.6, birthday bonus removed) — Api, RuleEngine, Ledger, Consumer and the portal rule form. Nothing was run; findings marked "to verify" need a test.
**File name:** kept from revision 1 so existing links still work.

| # | Change | Type | Phase |
|---|---|---|---|
| 1 | Refund integration test for the possible double reversal (§2.8) and the refund-during-hold gap H1 (§2.4) | **Test only.** No behaviour change. **Done 2026-10-08 — both gaps confirmed** (§7.1) | 0 |
| 2 | A refund during the hold cancels the held posting; release sends notifications and runs the tier check (H1–H3); release without recheck documented (H4) | **Bug fix** (the feature doesn't do what it promises). **Done 2026-10-08** | 1a |
| 3 | The engine runs once per event for `signup`, `kyc.completed`, `card.transaction`, `remittance`, `points.adjusted` (D18) | **Bug fix** (known pitfall). **Done 2026-10-08** | 1a |
| 4 | ~~Test mode hidden and rejected on new redeem, transfer, reversal and adjustment rules (D9)~~ Superseded by row 11 (D20) | — | — |
| 5 | Automatic once-only for `signup` / `kyc.completed`, per customer per rule, on every rule (D4, D4a, D4b, D12) | **Scope change** — repeat events stop paying. **Done 2026-10-08** | 1b |
| 6 | Every per-customer cap follows On breach on both exclusive and stackable rules (D6, D13) | **Scope change** — changes payouts on capped exclusive rules. **Done 2026-10-08** | 1b |
| 7 | Rule form by trigger: Configuration/Limits fields shown per trigger group on new rules, with API create validation; Configuration section in en/tr; "Redemption terms" / "Transfer terms" titles; amount hint on tenant-defined triggers (D5, D8, D10, D11, D14, D17) | **UI only** + create-time validation. **Done 2026-10-08** | 2 |
| 8 | ~~Test mode on earn rules as a true shadow run, marked and reported, with a "Go live" action (D9, D16)~~ Dropped by D20 | — | ~~3~~ |
| 9 | Rounding: wallet decimals + rule/program direction; program `default_rounding` (D2) | **Scope change**. **Done 2026-10-08** | 4 |
| 10 | Expiry override: per-rule override of the POINTS wallet's `expiration_days`; soonest-expiring-first consumption (D2, E1–E3) | **Scope change**. **Done 2026-10-08** | 5 |
| 11 | **Test mode removed from every rule** (D20): checkbox gone for every trigger, `testMode: true` rejected by the API, the engine no longer has a test-mode path; existing earn rules with test mode on disabled at deploy (D21) | **Scope change** — removes a capability. **Done 2026-10-08** | 1b |
| 12 | `order.refunded` accepts no Reversal rule; existing ones disabled; the built-in refund and Reversal rules share one cap (D22) | **Bug fix + capability narrowed** (a Reversal rule there reversed twice). **Done 2026-10-08** | 1a |
| 13 | `cash.spent` above the balance reported with `loyalty.cash.spend_failed` `insufficient_balance` instead of dead-lettering; rules run only for a spend that happened (D23) | **Bug fix + outbound reason added**. **Done 2026-10-08** | 1a |
| 14 | A redelivered event doesn't reserve budgets, count limits or pay another exclusive rule again; a redelivered partial Reversal completes (R15, found after Phase 5) | **Bug fix** (no scope change). **Done 2026-10-08** (§7.12) | 1a follow-up |

**Affects (docs)** — updated in the same change as each phase (`00-project-guardrails.md` §5):
- `docs/SCOPE_BASELINE.md`: the Rules and `programs/rules` rows; the RuleEngine rows for limits, delayed posting and rule-fire audit; the Ledger row (refunds, expiry). **As-built drift today:** line 28 lists "hold/expiry override" and "test mode" as Done, but expiry override is never read and test mode is ignored on redeem/transfer rules (§2.2). Corrected in the Phase 1 docs update.
- `docs/SOW.md` §2.2 (line 63, same drift as above) and §3 known limitations.
- `LoyaltySaaSApi.md`: `configuration.testMode` documented as removed (D20; the field stays on the wire, only `false` accepted); new 400 codes on rule create (test mode / delayed posting / fields that don't apply to the trigger); `already_awarded_once` outcome in the Customer 360 event drawer; `defaultRounding` on Program (Phase 4).
- `scripts/loyalty_schema_reference.md`: Phase 1 (only if H1 adds a cancelled state to `held_postings`), ~~Phase 3 (`rule_fire_audits.is_test`)~~ dropped by D20, Phase 4 (`programs.default_rounding`), Phase 5 (`ledger_entries.expires_at`).
- `.claude/rules/`:
  - `backend-rule-engine.md` l.19–21 (dedupe keys — the once key, D12) and l.49–50 (test mode and delayed posting — D9, H1–H3).
  - `backend-ledger.md` l.35–39 (expiry job — Phase 5) and l.44–46 (refunds — H1).
  - `backend-consumer.md` l.34–36 (engine runs twice — D18).
  - `01-workflow-and-debugging.md` pitfall table: remove "limits used up twice as fast" (D18); "Min event amount on an event without `amount` never fires", "refund during a hold".

> **Cross-reference.**
> - [2026-10-05-remove-complaints-and-stamps.md](2026-10-05-remove-complaints-and-stamps.md): Complaints, the Stamp rule type (end to end), the Expiry rule type and the `points.expired` trigger were removed. Its addendum A (approved 2026-10-06) removed the birthday bonus end to end.
> - [2026-10-05-Rule-driven redeem and transfer, program live checks, overview tiles, optional conditions.md](<2026-10-05-Rule-driven redeem and transfer, program live checks, overview tiles, optional conditions.md>): redeem and transfer always run on a rule; conditions are optional on every trigger; Streak campaigns and Card buckets tiles added to the program overview.

---

## 0. Decisions in force (2026-10-06)

Answers came from the stakeholders, relayed by the developer.

### 0.1 Decisions

| # | Question | Decision | What it means |
|---|---|---|---|
| D2 (Q2) | Expiry override and Rounding: build them, or remove them from the form? | **Build properly.** | Both become real features. Design: §3.8 (Rounding), §3.9 (Expiry override). |
| D4 (Q4) | Should signup and KYC be once-only automatically? | **Yes, automatically.** | The engine enforces "once per customer" for `signup` and `kyc.completed`. Design §3.6. |
| D4a (F2a) | Once-only on existing rules too? | **Yes — every signup/KYC rule, existing and new.** | An engine guarantee, so D5 doesn't exempt existing rules. No clawback of past duplicate payments; a past payment counts as "already received". |
| D4b (F2b) | Per customer total on signup/KYC rules? | **Hidden on new rules; legacy values preserved but can't override the event's cardinality.** | At most one award per rule, whatever the saved value. |
| D5 (Q5) | Per-trigger field rules on new rules only, or also on edit? | **New rules only. Don't touch existing rules.** | Hiding and API validation apply on create. Existing rules keep their values and are edited as today. No migration. |
| D6 (Q7) | Per-customer caps on exclusive rules? | **Must apply on both exclusive and stackable rules.** | The cap is never exceeded on either kind. Reading confirmed by D13. |
| D7 (Q8) | Tenant-defined events: declare whether they carry an `amount`? | **No — amount is optional, like `kyc.completed`.** | Every field stays visible for tenant-defined triggers; see D14. |
| D8 (Q6b) | A minimum transfer amount? | **No — nothing to do.** | No new field. Min event amount hidden on new transfer rules. |
| D9 (Q1) | Test mode: build it for burn rules, or hide and reject it? | **Earn: a true shadow run. Burn, reversal, adjustment: hidden and rejected on create.** | Design §3.3. Existing rules unchanged; a read-only query lists burn rules with test mode on today. |
| D10 (Q3) | Delayed posting: keep for one-time bonuses? | **Amount-based earn only, after H1–H3 are fixed.** | Hidden for signup/KYC, redeem, transfer, reversal and adjustment on new rules. Design §3.4. |
| D11 (Q6, Q6a) | Where do burn limits live; one redeem minimum or two? | **Minimum redemption and Daily transfer limit stay in Calculation; only Minimum redemption is shown.** | Design §3.5. |
| D12 (F2) | What counts as "once"? | **Once per customer per rule (option A).** | Design §3.6. |
| D13 (F4) | Per-customer caps and On breach | **Every per-customer cap follows On breach on both kinds of rule; default Clamp.** | Design §3.7. |
| D14 (F3) | Warn admins when a rule needs an `amount` the event might not send? | **Yes.** | A one-line hint on Spend's rate and on Min event amount for tenant-defined triggers. Portal only, en/tr. |
| D15 (H4) | Release held points even if the rule was disabled or the program paused during the hold? | **Yes.** | The award was decided at event time; documented as deliberate. |
| D16 (O1) | "Go live" action for test mode? | **Yes.** | Switches test mode off as a new rule version. |
| D17 (O2) | Rename "Calculation" on redeem/transfer rules? | **Yes.** | "Redemption terms" / "Transfer terms". Label only, en/tr. |
| D18 (O3) | Fix the double evaluation in Phase 1? | **Yes.** | Budgets and counters stop double-counting. |
| D19 (O4) | Approve the phases, one Scope Change Request each? | **Yes.** | §7. |
| D20 (final verdict, 2026-10-08) | Test mode: shadow run on earn rules (D9) or remove it? | **"Let's execute with test mode removal."** | Test mode is removed from **every** rule type and trigger. **Supersedes** D9's earn shadow run and D16 ("Go live"); D9's hide-and-reject for burn, reversal and adjustment is now part of the general removal. Phase 3 is dropped. Design §3.10; existing rules with test mode on: Q10 (§8). |
| D21 (Q10, 2026-10-08) | Existing earn rules with test mode on: disable them, or let them start paying? | **Option A approved: disable them in the Phase 1 data migration.** | Active and pending-approval earn rules whose current configuration has `testMode: true` are set to `disabled` (no row deleted, `updated_at` bumped), deployed together with the engine change. Admins re-enable deliberately; that edit saves `testMode: false` as a new version. Redeem, transfer and reversal rules with test mode on are left as they are. |
| D22 (Q9, 2026-10-08) | Reversal rules on `order.refunded`? | **Recommendation approved: don't allow them.** | `order.refunded` accepts no rule type (`RuleTypeCatalog`); existing active / pending Reversal rules on it are disabled by migration and refused on edit / re-enable; the engine skips a not-yet-disabled one; `RefundService` and `ReversalRuleProcessor` count each other's reversals in their cumulative cap. Pre-deploy query lists refunds already reversed twice; the PO decides on compensating credits. |
| D23 (Q11, 2026-10-08) | `cash.spent` with insufficient balance: dead-letter or report? | **Recommendation approved: report it.** | `loyalty.cash.spend_failed` with reason `insufficient_balance`, nothing posted, inbox `processed`. The handler runs the rule engine only for a spend that happened. |
| D24 (2026-10-08) | Deliver Phase 1 as one change? | **Recommendation approved: split it.** | **Part 1a — money correctness:** D18, D22, H1–H3, D23. **Part 1b — rule behaviour:** D20/D21, D4/D12, D6/D13. One Scope Change Request and one Architect review each. |

### 0.2 Expiry override decisions

| # | Question | Decision |
|---|---|---|
| E1 | Can the override be longer than the wallet's expiry? | **Yes, longer or shorter.** Any whole number of days > 0. |
| E2 | Can it apply when the wallet has no expiry? | **Yes.** Points from the rule expire; the wallet's other points never do. |
| E3 | Soonest-expiring-first instead of oldest-first? | **Yes.** Redemptions, transfers out, reward purchases and expiry use the soonest-expiring points first; points with no expiry are used last. |

---

## 1. Why

The rule form shows every Configuration and Limits field for every trigger. Now that redeem and transfer run on rules, several fields an admin can set are silently ignored, and some combinations break the rule or move real money unexpectedly.

**Key findings (as-built, §2):**

- **Test mode is ignored on redeem and transfer rules** — a "test" redeem rule debits real points and pays real cash. Highest risk.
- **A test-mode earn rule can block a live rule**, and its fires are unmarked and partly count toward caps (§2.3).
- **Delayed posting doesn't protect against refunds** — a refund during the hold fails, and the points are posted anyway (§2.4).
- **Delayed posting, Notify on award, Rounding, Max per event, per-customer caps and On breach are ignored on redeem and transfer rules.**
- **Expiry override does nothing; Rounding has no practical effect.**
- **Min event amount on `signup`, `kyc.completed` or `points.adjusted` rules means the rule never fires** (no `amount`).
- **"Once per customer" (signup, KYC) is not enforced.**
- **Caps can be exceeded on exclusive rules.**
- **Limits are used up twice as fast for five events** (the engine runs twice).
- **Reversal rules ignore Configuration and Limits**, and reverse an order a second time on top of the built-in refund when the refund carries a `contact_key` (**confirmed by test 2026-10-08**, §2.8).

---

## 2. Current state (as-built)

### 2.1 Code changes since revision 1

| Area | Before (revision 1) | Now |
|---|---|---|
| Rule types | 8 (incl. Stamp, Expiry) | 6: Spend, Fixed bonus, Redemption, Transfer, Reversal, Manual adjustment |
| Triggers | 14 built-in, incl. `points.expired` and `birthdaybonus` | 12 built-in; `points.expired` and `birthdaybonus` are rejected by name (`trigger_retired`); existing rules on them can't be edited or re-activated |
| Targets | POINTS, CASH, STAMP | POINTS, CASH |
| Redeem / transfer | Wallet settings; rules configurable but never applied | Always a rule, picked by the debited POINTS wallet; wallet settings only pre-fill new rules |
| Redeem calculation | `ratio`, `minRedeem` | `rate` (cash per point, required), `minRedeem` (optional), `cashAccountTypeId` "Redeem into" (required) — needs second-admin approval |
| Transfer calculation | `ratio`, `fee`, `maxPerDay` | `maxPerDay` (daily transfer limit, required); `ratio` / `fee` hidden |
| Burn event amount | Read from `amount` (always 0) | Read from `points_amount` |
| Conditions | Shown as optional but save failed | Optional on every trigger |
| Program live check | Earn rules and cashback only | Also redeem, transfer, every reward purchase, `cash.added`, `cash.spent` |

### 2.2 Configuration section, field by field

The section has 7 fields, shown for every rule type and trigger, English only (hardcoded in [rule-form.page.ts](../../web/src/app/features/programs/rules/rule-form.page.ts)). Which code applies a rule depends on its type:

- **Earn pipeline** — Spend, Fixed bonus, Manual adjustment: [WinnerSelector.cs](../../src/dEngage.Loyalty.RuleEngine/Processing/WinnerSelector.cs) then [LedgerPoster.cs](../../src/dEngage.Loyalty.RuleEngine/Processing/LedgerPoster.cs).
- **Burn handlers** — Redemption, Transfer: [BurnRuleResolver.cs](../../src/dEngage.Loyalty.RuleEngine/Processing/BurnRuleResolver.cs) picks the rule; [PointsRedeemHandler.cs](../../src/dEngage.Loyalty.Consumer/Handlers/PointsRedeemHandler.cs) / [PointsTransferHandler.cs](../../src/dEngage.Loyalty.Consumer/Handlers/PointsTransferHandler.cs) post.
- **Reversal processor** — Reversal: [ReversalRuleProcessor.cs](../../src/dEngage.Loyalty.RuleEngine/Processing/ReversalRuleProcessor.cs).

| Field (UI label) | Options / default | What it does | Earn pipeline | Burn handlers | Reversal |
|---|---|---|---|---|---|
| Rounding | Inherit from program (default), Down, Nearest, Up | Applied after calculation. "Inherit" = no rounding (Program has no rounding setting). Spend already rounds down to the wallet's decimals, so Up/Nearest change nothing. Other types round to 2 places. | Effectively no effect | Ignored (cash always rounded down to the CASH wallet's decimals) | Ignored |
| Posting | Immediate (default), Delayed | Delayed writes a held posting; an hourly job posts it after the hold days. | Works, but a refund during the hold doesn't cancel it (§2.4) | **Ignored — always immediate** | Ignored |
| Hold days | Required, ≥ 1, when Delayed | When the held posting is released. | Works | Ignored | Ignored |
| Expiry override (days) | Optional | **Saved, never read anywhere.** | No effect | No effect | No effect |
| Reversible (checkbox) | On | When off, the built-in refund on `order.refunded` ([RefundService.cs](../../src/dEngage.Loyalty.Ledger/RefundService.cs)) skips this rule's earn postings. | Works (refundable events only) | Not applicable | Not applicable |
| Test mode (checkbox) | Off | Evaluate and audit, post nothing, reserve no budget, send nothing. | Works, with side effects (§2.3) | **Ignored — real points and cash move** | Ignored |
| Notify on award (checkbox) | Off | Extra `loyalty.rule.awarded` message per awarding rule. | Works | Ignored (`loyalty.points.redeemed` / `transferred` are always sent, with `rule_id`) | Ignored |

### 2.3 Test mode: storage, visibility, side effects (to verify by test)

- **Storage.** No separate table: a test fire is written to `rule_fire_audits`, the same table as a real fire, with `ledger_entry_id` empty and **no test flag**. Nothing goes to the ledger or the outbox.
- **Visibility.** Customer 360 → Rules & caps (`GET customers/{ref}/rule-fires`) and the event drawer show the fire with its amount exactly like a real award. Nothing marks a rule as in test mode, and there's no per-rule results view. A held (delayed) fire also has an empty `ledger_entry_id`, so test and held fires can't be told apart from the audit row.
- **Side effect 1 — a test rule can block a live rule.** Exclusive winner selection doesn't check test mode. A higher-priority test rule on the same wallet wins the slot, nothing is posted, and **the customer loses the live award**.
- **Side effect 2 — test fires partly count toward caps.** The Redis per-customer counters (total, per day) are increased for test fires too ([LimitCounterSync.cs](../../src/dEngage.Loyalty.RuleEngine/Processing/LimitCounterSync.cs)); cooldown, max customers and budgets are counted from the ledger and ignore them. Going live, customers may already have part of their cap "used"; during the test, cooldowns and budgets never cut in.

### 2.4 Delayed posting (H1 confirmed by test 2026-10-08)

**Purpose.** Award now, hold for N days (e.g. a 14-day return window); a refund in that window should cancel the award.

**How it works.** Posting = Delayed + Hold days → the rule is evaluated and the amount fixed → a row in **`held_postings`** with `hold_until`; any budget is reserved now → **every hour** [DelayedPostingPromotionJob.cs](../../src/dEngage.Loyalty.RuleEngine/Processing/DelayedPostingPromotionJob.cs) posts due rows with the same idempotency key → Customer 360 shows them as **pending**.

| # | Gap | Effect | Severity / decision |
|---|---|---|---|
| H1 | A refund during the hold doesn't cancel the held posting: `RefundService` finds no ledger entries, fails with `original_entries_not_found`, and the `order.refunded` event goes to the dead-letter queue. The points are posted when the hold ends. | Points kept for a refunded order; budget stays used. | **High** — fix in Phase 1 |
| H2 | No `points.earned` or `loyalty.rule.awarded` on release. | The customer is never told. | Medium — fix in Phase 1 |
| H3 | No tier check on release. | A tier upgrade waits for a later event. | Medium — fix in Phase 1 |
| H4 | No recheck of rule/program status on release. | Points posted even if the rule was disabled or the program paused. | Low — **kept, documented (D15)** |
| H5 | Hourly release. | Up to an hour late. | Low — unchanged |

### 2.5 Limits, enforcement by path

The portal shows all 9 limits plus Period, Reset window and On breach for every trigger. The API ([RulesValidators.cs](../../src/dEngage.Loyalty.Api/Rules/RulesValidators.cs)) and portal ([rule-limits.ts](../../web/src/app/features/programs/rules/rule-limits.ts)) check values identically (positive, ≤ 4 decimals, a period for per-period caps) but not whether a limit fits the trigger.

| Limit | Earn pipeline | Burn handlers | Reversal |
|---|---|---|---|
| Per customer total | Earn only. Exclusive: checked before, not trimmed. Stackable: always trimmed. | Ignored | Ignored |
| Per customer per day | Same as total | Ignored | Ignored |
| Per customer per period | Earn only. Exclusive: checked before. Stackable: follows On breach. | Ignored | Ignored |
| Max per event | Caps the calculated amount | **Ignored** | Ignored |
| Min event amount | Skips the rule; reads `amount` | Skips; next rule tried; reads `points_amount` | Ignored |
| Cooldown hours, Max customers | Skip the rule | Skip; next rule tried | Ignored |
| Rule budget total / per period | Reserved at posting; Clamp or Skip | All-or-nothing reservation in points after the pick; refusal fails the event (`rule_limit_reached`) | Ignored |
| On breach | Budgets and per-customer per period | **Ignored** | Ignored |

Days and weeks are UTC; a calendar week starts Sunday ([PeriodWindow.cs](../../src/dEngage.Loyalty.RuleEngine/Processing/PeriodWindow.cs)). A blocked burn fails with `rule_limit_reached`; no matching rule gives `no_rule`.

### 2.6 Where the event amount comes from

[EvaluationEvent.cs](../../src/dEngage.Loyalty.RuleEngine/Models/EvaluationEvent.cs) reads `points_amount` for burn events and `amount` for everything else; a missing field is 0. `signup`, `kyc.completed` and `points.adjusted` declare no `amount`, so **Min event amount on them means the rule never fires**, with no warning.

### 2.7 Other enforcement gaps

- **Cardinality not enforced.** `signup` / `kyc.completed` are "once per customer" in [EventTypes.cs](../../src/dEngage.Loyalty.Shared/Events/EventTypes.cs), but a repeat event with a new event id pays again. → D4.
- **Caps exceeded on exclusive rules** (cap 100, used 90, award 50 → total 140). → D6, D13.
- **Engine runs twice** for `signup`, `kyc.completed`, `card.transaction`, `remittance`, `points.adjusted`: budgets and counters double-count. → D18.

### 2.8 Double reversal on `order.refunded` (confirmed by test 2026-10-08)

The built-in refund (`OrderRefundedHandler` → `RefundService`, reason `refund`) always runs, and the worker also runs the engine, so an active Reversal rule on `order.refunded` posts its own entry (reason `rule_reversal`). Each path caps only against its own reason, so one refund could reverse the earn twice. Phase 0 confirms or rules it out; Q9 follows.

**Phase 0 result (2026-10-08).** Confirmed, with one precondition: `CampaignEvaluationService` runs the engine only when the event's `Data` carries `contact_key`, and `contact_key` is optional on `order.refunded` (`OrderRefundedRequest.ContactKey` is nullable). So the double reversal happens **when the client sends the refund with a contact key** and the program has an active Reversal rule on `order.refunded`. In the test, a 100-point earn on a 600-point balance was reversed **twice** (−200, balance 400 instead of 500). A customer whose balance holds only the order's points is protected by the Reversal rule's default clamp to zero, which is why it can stay unnoticed.

**Fixed in Part 1a (D22, 2026-10-08)** — §7.3.

---

### 2.9 Which code applies each rule (reference)

| Path | Rule types | How | Configuration / Limits honoured |
|---|---|---|---|
| **1. Shared rule engine** | Spend, Fixed bonus, Manual adjustment | `CampaignEvaluationService` (skips events without `contact_key`; live programs only) → `RuleEngine` → `RuleMatcher` → `WinnerSelector` → `LedgerPoster` → `LimitCounterSync` → tier evaluation | All |
| **2. Inside the event handler** | Redemption, Transfer | `PointsRedeemHandler` / `PointsTransferHandler` pick one rule through `BurnRuleResolver` and post it themselves; the engine skips these types | Conditions, Min event amount, Cooldown, Max customers, Rule budget, plus their own terms |
| **3. Dedicated processor** | Reversal | `ReversalRuleProcessor`, called by the engine; no winner selection, no `LedgerPoster` | None |

| Trigger | Rule types | Handler's own work | Path | Engine runs (since Part 1a) |
|---|---|---|---|---|
| `signup`, `kyc.completed` | Fixed bonus | none — calls the engine | 1 | Once, in the handler (was twice — D18) |
| `card.transaction`, `remittance` | Fixed bonus, Spend | none — calls the engine | 1 | Once, in the handler (was twice — D18) |
| `order.created` | Fixed bonus, Spend | none — calls the engine | 1 | Once, in the handler |
| `cash.added` | Fixed bonus, Spend | credits the CASH wallet; refuses a non-live program | 1 | Once, in the worker |
| `cash.spent` | Fixed bonus, Spend | debits the CASH wallet; refuses a non-live program or an insufficient balance (D23) | 1 | Once, in the handler, only for a spend that happened (D23) |
| `points.redeem` | Redemption | the whole redeem | 2 | Once, in the worker (streak campaigns only) |
| `points.transfer` | Transfer | the whole transfer | 2 | Once, in the worker (streak campaigns only) |
| `order.refunded` | none since D22 | the built-in refund (incl. held postings, H1) | — | Once, in the worker (only with `contact_key`) |
| `points.adjusted` | Manual adjustment | none — calls the engine | 1 | Once, in the handler (was twice — D18) |

## 3. Proposed design

### 3.1 Trigger event groups

| Group | Trigger events | Amount field | Rule types | Calculation fields |
|---|---|---|---|---|
| A. Amount-based earn | `order.created`, `card.transaction`, `remittance`, `cash.added`, `cash.spent` | `amount` | Spend, Fixed bonus | Spend: rate. Fixed bonus: amount |
| B. One-time earn | `signup`, `kyc.completed` (once per customer) | none | Fixed bonus | amount |
| C1. Redeem | `points.redeem` | `points_amount` | Redemption | Cash per point (required), Minimum redemption (optional), Redeem into CASH wallet (required) |
| C2. Transfer | `points.transfer` | `points_amount` | Transfer | Daily transfer limit (required) |
| D. Reverse | `order.refunded` | `amount` | Reversal | Reversal mode, allow negative / clamp to zero |
| E. Adjust | `points.adjusted` (operator) | none declared | Manual adjustment | Reason code, fallback amount (optional) |

`reward.purchase` allows no rule types. Tenant-defined events carry no field metadata: every field stays visible, the amount is optional (D7), and a hint warns that amount-based fields need it (D14).

### 3.2 Fields per trigger group (new rules — D5)

Key: **Show** · **Hide** (no meaning) · **Hide (!)** (ignored or harmful today). Existing rules keep their saved values and are edited as today.

**Configuration**

| Field | A. Amount earn | B. One-time earn | C1. Redeem | C2. Transfer | D. Reverse | E. Adjust |
|---|---|---|---|---|---|---|
| Rounding | Show — Phase 4 (§3.8) | Show — Phase 4 (Fixed bonus) | Hide (!) | Hide (!) | Hide (!) | Show — Phase 4 (Manual adjustment) |
| Posting + Hold days | Show, once H1 is fixed (D10) | Hide (D10) | Hide (!) | Hide (!) | Hide (!) | Hide |
| Expiry override | Show, POINTS targets only — Phase 5 (§3.9) | Show, POINTS targets only — Phase 5 | Hide | Hide | Hide | Hide |
| Reversible | Show | Hide (nothing refunds it) | Hide | Hide | Hide | Hide |
| Test mode | **Removed (D20)** | **Removed (D20)** | **Removed (D20)** | **Removed (D20)** | **Removed (D20)** | **Removed (D20)** |
| Notify on award | Show | Show | Hide (outcome events already sent) | Hide | Hide | Hide |

**Limits**

| Limit | A. Amount earn | B. One-time earn | C1. Redeem | C2. Transfer | D. Reverse | E. Adjust |
|---|---|---|---|---|---|---|
| Per customer total | Show | Hide — once-only is automatic (D4b) | Hide (!) | Hide (!) | Hide (!) | Hide |
| Per customer per day | Show | Hide | Hide (!) | Hide (!) — Daily transfer limit instead | Hide (!) | Hide |
| Per customer per period | Show | Hide | Hide (!) | Hide (!) | Hide (!) | Hide |
| Max per event | Show for Spend; Hide for Fixed bonus | Hide | Hide (!) | Hide (!) | Hide (!) | Hide |
| Min event amount | Show | **Hide (!) — never fires** | Hide — Minimum redemption instead (D11) | Hide (D8) | Hide (!) | Hide (!) |
| Cooldown hours | Show | Hide | Show (e.g. one redeem per 24 h) | Show (anti-abuse) | Hide (!) | Hide |
| Max customers | Show | Show (e.g. first 1,000 KYC) | Show | Show | Hide (!) | Hide |
| Rule budget total / per period | Show | Show | Show (in points) | Show (in points) | Hide (!) | Hide |
| Period / Reset window | With per-period caps | With budget per period | With budget per period | With budget per period | Hide | Hide |
| On breach | With clampable caps | With budget | Hide (!) — all-or-nothing | Hide (!) | Hide | Hide |

The API rejects, **on create only**, a value set on a field hidden for that trigger (new 400 codes). The portal and API checks mirror each other exactly (DSL-parity contract).

### 3.3 Test mode (D9, D16) — Phases 1 and 3

> **Superseded by D20 (2026-10-08) — see §3.10.** Kept for the record.

- **Earn rules (A, B): a true shadow run** (Phase 3): excluded from exclusive winner selection, result worked out separately as "what this rule would have given"; no limit counters updated; `is_test` on `rule_fire_audits`; a "Test" badge in Customer 360; a "Test results" panel on the rule (would-be fires, distinct customers, total would-be value, date range); a "Go live" action that switches test mode off as a new rule version.
- **Redeem, transfer (C1, C2): no dry run.** A redeem or transfer is the customer's own request. Trial burn behaviour in a staging tenant, the Event Simulator, or live with a condition, an active window or priority. Hidden and `testMode: true` rejected on create (Phase 1).
- **Reversal, adjustment (D, E): hidden and rejected** on create (Phase 1).
- **Existing rules:** unchanged; a read-only query lists burn rules with test mode on today (they move real money).

### 3.4 Delayed posting (D10, D15) — Phases 1 and 2

- Kept for amount-based earn (A) only; hidden on new B, C, D, E rules (Phase 2).
- **H1:** a refund during the hold cancels the held posting, fully or in proportion to the refund, and releases its budget reservation, instead of failing to the dead-letter queue (Phase 1).
- **H2, H3:** the release sends `points.earned` (and `loyalty.rule.awarded` when Notify on award is on) through the outbox and runs the tier check (Phase 1).
- **H4:** the release posts without rechecking rule or program status — deliberate (D15).

### 3.5 Redeem and transfer terms (D8, D11, D17) — Phase 2

| Setting | Section | When it blocks | Customer's app is told |
|---|---|---|---|
| Minimum redemption (`minRedeem`) | Calculation | The event fails; lower-priority rules not tried | `redeem_failed`, `below_minimum` |
| Daily transfer limit (`maxPerDay`) | Calculation | The event fails | `transfer_failed`, `daily_limit_exceeded` |
| Min event amount | Limits | Rule skipped, next rule tried | `rule_limit_reached` |
| Cooldown, Max customers, Rule budget | Limits | Same | `rule_limit_reached` |

- Minimum redemption and Daily transfer limit stay in Calculation — they're the customer-facing terms, mirror the wallet defaults (CR 2026-10-05, S3/S4) and give clear reasons. The section is titled "Redemption terms" / "Transfer terms" on these rules (D17).
- Only Minimum redemption is shown on new redeem rules; Min event amount is hidden on new redeem and transfer rules. No minimum transfer field (D8).
- Limits shown for burn rules: budget, cooldown, max customers.

### 3.6 Once-only for signup and KYC (D4, D4a, D4b, D12) — Phase 1

| | **A. Once per customer per rule (decided)** | B. Once per customer per event type in a program |
|---|---|---|
| Two rules on the same event | Both pay on the first event | Both pay on the first event |
| A new rule added later, event resent | The new rule pays | Nothing pays |
| First event blocked by an exhausted budget | A later event can pay | Bonus lost for good |

**Rules of once-only:**
1. "Once" spans all versions of a rule (same rule id).
2. Only a real award counts: a ledger posting or a held posting. Test fires and limit-skipped fires don't.
3. A reversal doesn't reset it.
4. Per program (rules belong to one program).
5. Every signup/KYC rule, existing and new (D4a); no clawback.
6. A legacy Per customer total never allows a second award (D4b); with D13, a legacy cap below the bonus still trims that one award.
7. Tenant-defined events are not affected (no cardinality).

**Guarantee.** For once-per-customer triggers the ledger idempotency key becomes **`once:{ruleId}:{contactKey}`** instead of `{eventId}:{ruleId}`. The existing unique index `uq_ledger_entries_idempotency_key` on `(tenant_id, idempotency_key)` ([LedgerEntryConfiguration.cs](../../src/dEngage.Loyalty.Schema/Configurations/LedgerEntryConfiguration.cs)) then makes a second payment impossible, even under a race. The engine checks first and skips cleanly (no audit row, no `points.earned`); held postings use the same key.

**Reporting.** No failure event to the client. The engine logs `already_awarded_once`; Customer 360's event drawer shows "No award: bonus already received", linking the earlier event.

### 3.7 Per-customer caps and On breach (D6, D13) — Phase 1

Worked example — POINTS wallet; each order is `order.created` for 1,000:

| Rule | Kind | Priority | Award | Per customer total |
|---|---|---|---|---|
| A — 10% back | Exclusive | 10 | 100 | 250 |
| B — flat thank-you | Exclusive | 5 | 20 | none |
| C — weekend bonus | Stackable | — | 50 | 120 |

| Order | Rule A | Rule B | Rule C | Customer gets |
|---|---|---|---|---|
| 1 | Wins, 100 (used 100) | Loses to A | 50 (used 50) | 150 |
| 2 | Wins, 100 (used 200) | Loses to A | 50 (used 100) | 150 |
| 3 | Used 200 < 250 → **full 100 (used 300 — 50 over the cap)** | Loses to A | Room 20 → **trimmed to 20** | 120 |
| 4 | Used 300 ≥ 250 → skipped | **Wins, 20** | Nothing | 20 |

**Decision.** Every per-customer cap (total, per day, per period), on both kinds of rule, follows On breach:

| On breach | Every per-customer cap | Exclusive rule extra |
|---|---|---|
| **Clamp** (default) | Trim the award to what fits; the cap is never exceeded | The trimmed rule still wins its slot |
| **Skip** | If the full award doesn't fit, this rule pays nothing | The next exclusive rule gets its chance |

Order 3 becomes: A pays **50** (total exactly 250). One shared cap check replaces `IsLimitExhaustedAsync` / `ClipToLimitAsync`. No migration, no API change (`on_breach` exists, default Clamp).

**Effect on existing rules:** stackable + Clamp — no change; stackable + Skip with a total/per-day cap — now skips when the award doesn't fit; exclusive + Clamp — trimmed (the fix); exclusive + Skip — skips and the next exclusive rule tries.

**Deliberately unchanged:** Per customer per day stays as a field; Calendar stays the default Reset window; boundaries stay UTC.

### 3.8 Rounding (D2) — Phase 4

- **Precision** from the target wallet's decimals; **direction** from the rule's Rounding, or "Inherit from program" → new `programs.default_rounding` (Down / Nearest / Up), default **Down**, so existing payouts don't change.
- Applies to Spend, Fixed bonus and Manual adjustment, once, after the calculation and the max-per-event cap; `SpendRuleHandler` stops rounding on its own (one rounding place — `backend-rule-engine.md` "never round in two places").
- Not applied to the redeem cash payout (stays rounded down to the CASH wallet's decimals), transfer or reversal.
- The form shows the inherited value ("Inherit from program (Down)"). "Nearest" = half away from zero.
- To check in code before this phase: whether a fixed bonus with more decimals than its wallet allows (e.g. 10.5 into a 0-decimal wallet) is rejected anywhere.

### 3.9 Expiry override (D2, E1–E3) — Phase 5

**What it is.** A per-rule override of the POINTS wallet's `expiration_days` for the points earned through that rule (e.g. promo points expiring in 30 days while normal points keep 365). It does nothing today because [PointsExpirationJob.cs](../../src/dEngage.Loyalty.Ledger/PointsExpirationJob.cs) computes one cutoff per wallet, and no posting stores its own expiry date.

1. A nullable `expires_at` on `ledger_entries`, set once on insert (the rule's override, else the wallet's days) — the ledger stays append-only.
2. No backfill: an empty `expires_at` means "earn date + current wallet days", exactly today's behaviour.
3. The expiry job expires by `expires_at`.
4. Soonest-expiring points are used first; points with no expiry last (E3).
5. The "expiring soon" job ([PointsExpiringDetectorJob.cs](../../src/dEngage.Loyalty.Ledger/PointsExpiringDetectorJob.cs)) and Customer 360 amounts use the same logic.
6. Held postings count expiry from when they actually post; points received by transfer get the wallet default; changing the wallet's days doesn't move stored dates.

Shown only on rules earning into a POINTS wallet (A, B). Label "Points expire after (days)", placeholder "Wallet default: N"; whole number > 0 (E1); allowed on a wallet without expiry (E2).

### 3.10 Test mode removal (D20) — Phase 1

**Decision.** Test mode is removed from every rule. It removes, in one step, every problem found in §2.3: the ignored flag on redeem/transfer rules (real money moving on "test" rules), a test rule blocking a live rule, test fires counting toward caps, and unmarked test fires in Customer 360.

**Design.**

1. **Portal:** the Test mode checkbox is removed from the rule form for every trigger; its en/tr strings are removed.
2. **API:** `configuration.testMode: true` is rejected (new 400 code `test_mode_removed`) on create and on any edit that sends it. The `testMode` field stays on `RuleSettings` and on the wire (existing fields are frozen), and responses always return `false`.
3. **Engine:** the test-mode branch in [LedgerPoster.cs](../../src/dEngage.Loyalty.RuleEngine/Processing/LedgerPoster.cs) (no posting, no budget reservation, excluded from `points.earned`) is removed, so every matching rule posts. This only happens together with the D21 data migration for existing rules, in the same deploy.
4. **History:** past test fires stay in `rule_fire_audits` as they are (no flag to mark them; they have an empty `ledger_entry_id`). No backfill.
5. **How to trial a rule instead:** a staging tenant, the Event Simulator, or live with a condition on a test segment or test customers, a short active window, or priority.

**Existing rules with test mode on (Q10 — decided as D21: option A).**

| Rule kind | Today | If nothing is done | Recommendation |
|---|---|---|---|
| Earn (Spend, Fixed bonus, Manual adjustment) with test mode on | Evaluates and audits, pays nothing | **Starts paying real points and cash the moment the engine change deploys** | **Decided (D21): disable them in the Phase 1 migration** (status → `disabled`, `updated_at` bumped so `RuleSyncService` picks it up; no row deleted) — the same pattern as R-O11 in CR 2026-10-05. Admins re-enable deliberately; that edit saves `testMode: false` as a new version. |
| Redeem / transfer with test mode on | Already moves real money (flag ignored) | No change | Leave as they are; listed by the pre-deploy query for the PO. |
| Reversal with test mode on | Flag ignored | No change | Leave as they are. |

**Impact.**

| Area | Change |
|---|---|
| Portal | `programs/rules/rule-form.page.ts`, `rule.model.ts`; en/tr |
| API | `Rules/RulesValidators.cs` (reject `testMode: true`), `RulesAppService.cs` (always return `false`) |
| RuleEngine | `LedgerPoster.cs` test-mode branch removed; `LimitCounterSync` needs no test-mode handling |
| Schema | **Data-only migration** (D21): disable active / pending-approval earn rules whose current configuration has `testMode: true`. No schema change. |
| Ops | Pre-deploy query: every rule with `testMode: true`, by tenant, program, type, status and version |
| Tests | API rejects `testMode: true` on create and edit; engine posts for a rule whose stored config still has `testMode: true` (after migration it is disabled); migration disables only the intended rows |

---

## 4. End-to-end impact (blast radius)

| Area | Phase | Files (planned) | Change |
|---|---|---|---|
| IntegrationTests | 0 | `IntegrationTests/E2E` (new refund test) | Refund of an earn with an active Reversal rule; refund during a hold |
| Ledger | 1 | `RefundService.cs` | Cancel held postings on refund (H1) |
| Schema | 1 | `Entities/HeldPosting.cs`, configuration, migration (only if a cancelled state is needed) | H1 |
| RuleEngine | 1 | `Processing/WinnerSelector.cs`, `LedgerPoster.cs`, `DelayedPostingPromotionJob.cs`, `LimitCounterSync.cs` | Shared cap check with On breach (D13); once key (D12); notifications and tier check on release (H2, H3) |
| Consumer | 1 | `EventConsumerWorker.cs` and/or `Handlers/SignupHandler.cs`, `KycCompletedHandler.cs`, `CardTransactionHandler.cs`, `RemittanceHandler.cs`, `PointsAdjustedHandler.cs` | Engine runs once per event (D18) |
| API: Rules | 1, 2 | `Rules/RulesValidators.cs`, `RulesAppService.cs`, `rules/metadata` | Create-only rejection of test mode / delayed posting / hidden fields per trigger (D5, D9, D10, D11) |
| API: Customers | 1, 5 | `Customers/CustomersAppService.cs`, `CustomersDtos.cs` | `already_awarded_once` in the event drawer; expiring-soon amounts (`isTest` dropped by D20) |
| Portal: rules | 2, 3, 4, 5 | `programs/rules/rule-form.page.ts`, `rule-limits.ts`, `rule.model.ts`, `rules-list.page.ts` | Fields per trigger group; en/tr Configuration section; terms titles; amount hint; test results and Go live; rounding display; expiry field |
| Portal: customers | 1, 3 | `features/customers/*` | Event drawer label; Test badge |
| Portal: programs | 4 | program form | `defaultRounding` |
| Portal: i18n | all | `public/i18n/en.json`, `tr.json` | New strings |
| Schema | 1, 4, 5 | migrations `…Cr1006` | Phase 1: data-only migration disabling earn rules with test mode on (D21); ~~`rule_fire_audits.is_test`~~ dropped by D20; `programs.default_rounding`; `ledger_entries.expires_at` (partitioned table) |
| Ledger | 5 | `PointsExpirationJob.cs`, `PointsExpiringDetectorJob.cs` | Expiry by `expires_at`; soonest-expiring-first |
| Ops | 1 | Pre-deploy SQL in the release notes (not `scripts/*.sql`) | (a) every rule with test mode on (D20, D21); (b) rules whose cap behaviour changes (§3.7); (c) held postings whose `order.refunded` is in the dead-letter queue (H1) |
| Docs | all | see "Affects (docs)" | |

**Breaking behaviour changes (release notes):**
- Repeat `signup` / `kyc.completed` events stop paying, on existing rules too (D4a).
- Capped exclusive rules pay less on the award that reaches the cap (D13).
- Stackable rules with On breach = Skip skip instead of trimming on total/per-day caps (D13).
- A refund during a hold cancels the held points (H1).
- Redemptions use soonest-expiring points first (E3, Phase 5).
- Rule create rejects fields that don't apply to the trigger (D5) — existing rules unaffected.

**Breaking (D20):** `configuration.testMode: true` is rejected on rule create and edit; earn rules that had test mode on are disabled at deploy (D21).

**API changes (additive):** new 400 codes on rule create; `already_awarded_once` outcome; `defaultRounding` (Phase 4). No event payload changes.

---

## 5. Contracts check (`00-project-guardrails.md` §2)

| Contract | Status |
|---|---|
| Tenancy | Kept. Once-only keys and all new queries are tenant-scoped; the unique index is per tenant. |
| Money and points | Kept. `decimal` on the server, strings on the wire, `DecimalString` in the portal. Rounding happens once (Phase 4). |
| Ledger | Kept. Append-only: `expires_at` is set on insert, no backfill; H1 cancels a *held* posting, never a ledger row. Idempotency stays deterministic — the once key `once:{ruleId}:{contactKey}` replaces `{eventId}:{ruleId}` for once-per-customer triggers only (D12). |
| Events | Kept. Release notifications go through the outbox (H2); no payload changes; inbox dedupe unchanged; event `Data` stays snake_case. |
| Rule versioning | Kept. Once-only spans all versions (D12). ("Go live", D16, dropped by D20.) Re-enabling a rule disabled under D21 saves `testMode: false` as a new version. |
| CASH rules | Unchanged. Redeem rules still need a second admin. |
| Time | Unchanged. UTC; program time zones out of scope. |
| DSL parity | Applies. The create-time field validations (D5, D9, D10, D11) are identical in the API and the portal. |

---

## 6. Risks

| # | Risk | Mitigation |
|---|---|---|
| R1 | Repeat signup/KYC events stop paying on existing rules — support tickets. | Intended (D4a). Customer 360 shows "bonus already received"; release notes. |
| R2 | Capped exclusive rules pay less; Skip rules change behaviour. | Pre-deploy query (§4 Ops b) for the PO; release notes. |
| R3 | Refunds already in the dead-letter queue for held postings: those points will still post. | Pre-deploy query (§4 Ops c). The PO decides; any replay is a **new** refund event, never deleting `EventInbox` rows. |
| R4 | Burn rules with test mode on are moving real money today. | Pre-deploy query (§4 Ops a); PO decides (existing rules untouched, D5). |
| R5 | The double-evaluation fix could stop evaluation entirely for an event type. | Tests per event type: one evaluation, one budget reservation, one counter increment. |
| R6 | Admins see two classes of rules: old rules with values now hidden for new ones. | Edit shows saved values as today (D5); hints on the form. |
| R7 | The expiry rewrite removes points wrongly. | Strong tests (mixed dates, rows without `expires_at`, reruns); compare results with the current job before go-live. |
| R8 | Soonest-expiring-first changes which points a redemption uses. | Release notes; E3 signed off. |
| R9 | Per-customer caps are checked from Redis before posting, not reserved — two simultaneous events could both pass. | **Accepted** (today's risk too); per-customer ordering in the Consumer makes it unlikely. |
| R10 | ~~The double reversal is unconfirmed.~~ **Fixed in Part 1a (D22).** **Confirmed by Phase 0 (2026-10-08):** a refund sent with a `contact_key` is reversed twice when an active Reversal rule exists on `order.refunded`. | Decide Q9; fix in Phase 1. Pre-deploy query: Reversal rules active on `order.refunded`, and refunds already reversed by both `refund` and `rule_reversal` entries. |
| R11 | **Removing test mode makes existing earn rules with test mode on start paying real points and cash** (D20). | **Done in Part 1b:** migration `DisableTestModeEarnRulesCr1006`, tested on Postgres (`RuleDataMigrationsCr1006E2ETests`). D21: disable them in the Phase 1 data migration, deployed together with the engine change; the pre-deploy query lists them for the PO. |
| R12 | Admins lose a way to trial a rule on live traffic. | Accepted (D20). Trial in a staging tenant, the Event Simulator, or live with a test-segment condition or a short active window (§3.10). |
| R13 | Customer 360 "pending" and the event drawer show held postings; a refund now cancels or reduces them. | The API returns `refundedDelta` / `cancelledAt` (additive) and the portal shows them; `pendingAmount` nets them out (Part 1a). |
| R14 | A refund and the hourly release acting on the same held row at the same moment. | Both take a `FOR UPDATE` lock on the row before reading it (Part 1a); one waits for the other. |
| R15 | **Found 2026-10-08, fixed (§7.12):** an event that failed after its rules posted, when replayed, ran winner selection again — budgets and Redis counters were recorded twice, and on signup / KYC or a capped rule the next exclusive rule paid a second award for the same event; a replayed partial Reversal always failed. | `ILedgerPoster.HasPostedAsync` guard before winner selection; `ReversalRuleProcessor` skips reversed entries; tests reproduce each case. |

---

## 7. Phasing, go-live and verification

Each phase: Jira Scope Change Request → PO + Architect approval of its scope → feature branch → docs updated in the same change → Architect reviews the diff before merge. The file list of each phase is appended to §11.

| Phase | Content | Decisions | Schema change |
|---|---|---|---|
| 0 | Refund integration test (§2.4, §2.8); settles Q9 — **done 2026-10-08** (§7.1) | — | No |
| 1a | **Money correctness (D24) — done 2026-10-08 (§7.3):** engine once per event (D18); no Reversal rule on `order.refunded` + shared cap (D22); refund cancels held points, release announces and re-evaluates the tier (H1–H3), no recheck (H4/D15); `cash.spent` reports insufficient balance (D23) | D15, D18, D22, D23 | `HeldPostingRefundsCr1006` (schema), `DisableReversalRulesOnOrderRefundedCr1006` (data) |
| 1b | **Rule behaviour (D24) — done 2026-10-08 (§7.5):** test mode removed from every rule, existing earn rules with test mode on disabled (D20, D21); once-only for signup/KYC (D4, D4a, D4b, D12); per-customer caps follow On breach on both kinds (D6, D13) | D4, D4a, D4b, D6, D12, D13, D20, D21 | Data-only migration (D21) |
| 2 | **Done 2026-10-08 (§7.7):** Rule form by trigger with create validation; en/tr Configuration section; terms titles; amount hint | D5, D8, D10, D11, D14, D17 | No |
| ~~3~~ | ~~Test mode shadow run, `is_test`, badge, Test results panel, Go live~~ **Dropped by D20** | ~~D9 (earn), D16~~ | — |
| 4 | **Done 2026-10-08 (§7.9):** Rounding | D2 | `programs.default_rounding` |
| 5 | **Done 2026-10-08 (§7.11):** Expiry override | D2, E1–E3 | `ledger_entries.expires_at` |

**Verification per phase** (`01-workflow-and-debugging.md`):
- `dotnet build dEngage.Loyalty.sln`; `dotnet test src/dEngage.Loyalty.Engine.Tests`; `dotnet test src/dEngage.Loyalty.IntegrationTests` (Docker running).
- Portal (from `web/`): `npx ng lint` · `npm test` · `npx ng build --configuration development` · `npx prettier --write <changed files>`.
- Engine and ledger changes assert idempotency (`backend-tests.md`): the same event twice → one posting, one budget reservation, one limit increment.
- Each fix gets a test that fails without it (Engine.Tests for engine logic, IntegrationTests for API/persistence).
- New migrations via `dotnet ef migrations add <Name>Cr1006 -p src/dEngage.Loyalty.Schema`; applied migrations and the model snapshot are never hand-edited.

---

### 7.1 Phase 0 record (2026-10-08)

**Branch:** `1.8-rule-confg-limits-by-trigger`. **Baseline before the change:** `dotnet build dEngage.Loyalty.sln` clean (0 errors, 1 existing warning); Engine.Tests 95/95 passed; IntegrationTests 251/251 passed.

**New test:** [RefundPathsCr1006E2ETests.cs](../../src/dEngage.Loyalty.IntegrationTests/E2E/RefundPathsCr1006E2ETests.cs) (`Category=E2E`, real Postgres via Testcontainers). The tests assert the **correct** behaviour, so a failure confirms the gap; they become the regression tests for the Phase 1 fixes.

| Test | Result | Finding |
|---|---|---|
| `Order_refunded_reaches_the_rule_engine_only_when_it_carries_a_contact_key` | Passed | The engine runs for `order.refunded` only when `Data` carries `contact_key` |
| `A_full_refund_reverses_the_order_earn_only_once_when_a_reversal_rule_is_active` | **Failed — gap confirmed** | Balance 400 instead of 500; −200 reversed for a 100-point earn |
| `A_refund_during_the_hold_cancels_the_held_points` | **Failed — H1 confirmed** | The refund threw `original_entries_not_found` (dead-letter queue in production); the promotion job then posted the 100 held points |

The two failing tests are expected to fail until the Phase 1 fixes land; they stay as they are (never skipped or loosened — `backend-tests.md`).

### 7.2 Part 1a — Scope Change Request (text for Jira)

**Title:** Rule configuration & limits by trigger — Part 1a: money correctness (CR 2026-10-06)

**Why:** four defects move balances wrongly or hide outcomes today; two are proven by failing tests (`RefundPathsCr1006E2ETests`).

**Scope:**
1. **D18** — the rule engine runs exactly once per event; it ran twice for `signup`, `kyc.completed`, `card.transaction`, `remittance`, `points.adjusted`, double-counting budgets and limit counters.
2. **D22** — `order.refunded` accepts no Reversal rule (create 400; existing ones disabled by migration, 409 on edit / re-enable); the engine skips them; the built-in refund and Reversal rules share one cumulative cap.
3. **H1–H3, H4** — a refund during a hold takes the held points back (proportionally, once per refund event) instead of dead-lettering, and cancels a fully refunded hold; the release posts what remains under a shared row lock, sends `loyalty.points.earned` (and `loyalty.rule.awarded` when the rule notifies) and re-evaluates the tier; rule/program status not rechecked on release (D15).
4. **D23** — `cash.spent` above the balance → `loyalty.cash.spend_failed` `insufficient_balance`, processed, no rules run.

**Breaking / visible changes:** a Reversal rule can't be created on `order.refunded` (400) and existing ones stop running; refunds during a hold no longer fail; a refund-cancelled hold is never posted; new reason `insufficient_balance` on `loyalty.cash.spend_failed`; an extra `loyalty.points.earned` per released held posting; signup / KYC / card / remittance / adjustment rules use their limits and budgets at half the previous rate (the correct rate).

**Migrations:** `HeldPostingRefundsCr1006` (additive: `held_postings.cancelled_at`, `held_posting_refunds`, an index), `DisableReversalRulesOnOrderRefundedCr1006` (data only).

**Pre-deploy queries (run on each environment, results to the PO):**

```sql
-- (a) Reversal rules the D22 migration will disable
SELECT t.slug AS tenant, p.name AS program, r.id, r.name, r.status, r.current_version
FROM rules r JOIN programs p ON p.id = r.program_id JOIN tenants t ON t.id = r.tenant_id
WHERE r.type = 'ReversalRule' AND r.trigger = 'order.refunded'
  AND r.status IN ('active', 'pending_approval');

-- (b) Order earns already reversed by both paths (candidates for a compensating credit, D22)
SELECT le.tenant_id AS tenant, le.contact_key, le.id AS earn_entry_id, le.delta AS earned,
       SUM(ABS(r.delta)) AS reversed_total
FROM ledger_entries le
JOIN ledger_entries r ON r.tenant_id = le.tenant_id
 AND ((r.reason = 'refund' AND r.metadata->>'refund_of_entry_id' = le.id::text)
   OR (r.reason = 'rule_reversal' AND r.metadata->>'reversal_of_entry_id' = le.id::text))
WHERE le.reason = 'earn'
GROUP BY le.tenant_id, le.contact_key, le.id, le.delta
HAVING SUM(ABS(r.delta)) > le.delta;

-- (c) Held postings whose order.refunded is in the dead-letter queue (H1, R3)
SELECT h.tenant_id, h.contact_key, h.id AS held_posting_id, h.delta, h.hold_until, h.posted_at,
       ei.event_id AS refund_event_id, ei.status
FROM held_postings h
JOIN event_inbox ei ON ei.tenant_id = (SELECT slug FROM tenants WHERE id = h.tenant_id)
 AND ei.event_type = 'order.refunded' AND ei.status = 'failed'
 AND ei.payload::jsonb -> 'Data' ->> 'original_event_id' = h.source_event_id;
```

Query (c) assumes the inbox payload stores the envelope with `Data`; check one row before relying on it. Any replay of a failed refund is a **new** refund event, never a deleted `EventInbox` row.

### 7.3 Part 1a implementation record (2026-10-08)

**Branch:** `1.8-rule-confg-limits-by-trigger`. Build and tests run with `--artifacts-path` to a scratch folder, because the developer's running Api and Consumer locked their `bin` folders.

**Verification:**

| Check | Result |
|---|---|
| `dotnet build dEngage.Loyalty.sln` | 0 errors, 1 warning (existing, `RuleVersioningServiceTests.cs:57`) |
| `dotnet test src/dEngage.Loyalty.Engine.Tests` | **110/110 passed** (baseline 95) |
| `dotnet test src/dEngage.Loyalty.IntegrationTests` (Docker) | **261/261 passed** (baseline 251) — incl. the two Phase 0 tests that failed before |
| Portal `npm test` | 145/145 passed |
| Portal `npx ng build --configuration development` | Succeeded |
| Portal `npx ng lint` | 3 errors, **all in files not touched by this change** (`core/ui/confirm.service.ts` ×2 `no-autofocus`, `features/dashboard/dashboard.page.ts` `label-has-associated-control`) — existing, left as they are |
| `npx prettier --write` | Run on the two changed portal files |
| Not run | The full local stack (`.\run.ps1`) and a manual pass through the portal |

**New tests:** `HandlerEvaluatedEventsTests` (D18 guard), `RuleTypeCatalogTests.Order_refunded_has_no_compatible_rule_type` (D22), `BurnTriggerRuleTypesApiTests` (D22: create 400, re-enable 409, metadata), `CashEventsProgramLiveE2ETests` (D23: refused + no rules; evaluated per delivery, posted once), `RefundPathsCr1006E2ETests` (H1 partial refund + redelivery, full-refund redelivery, H2/H3 release).

**For the Architect's review:**
- D18 keeps the existing pattern (handler evaluates + worker skips) and turns the single `order.created` exception into the `HandlerEvaluatedEvents` set, guarded by a reflection test.
- `cash.spent` moved its engine call into the handler so a refused spend earns nothing. Side effect: a refused spend no longer triggers `cash.spent` rules of **other** live programs in the tenant (it used to, through the worker).
- H1 adds an append-only child table rather than mutating `held_postings.delta`, so every partial take-back is auditable and idempotent per refund event. Held take-backs change no balance, so they don't appear in `loyalty.points.reversed`.
- The release job now commits per row (it used to save once per batch) so a failure rolls back only that row.
- `RulesAppService.RequireNotRetiredAsync` now also refuses any rule whose type its trigger no longer accepts (catalog check). This covers D22; it would equally cover any older incompatible combination.

**Files (Part 1a):**

| File | Action | Reason |
|---|---|---|
| `src/dEngage.Loyalty.Consumer/HandlerEvaluatedEvents.cs` | Created | D18/D23: the event types whose handler runs the engine |
| `src/dEngage.Loyalty.Consumer/EventConsumerWorker.cs` | Changed | D18: skip evaluation for those types |
| `src/dEngage.Loyalty.Consumer/Handlers/CashSpentHandler.cs` | Changed | D23: report insufficient balance; evaluate only after a spend |
| `src/dEngage.Loyalty.Shared/Events/OutcomeReasons.cs` | Changed | D23: `InsufficientBalance` |
| `src/dEngage.Loyalty.RuleEngine/Metadata/RuleTypeCatalog.cs` | Changed | D22: `order.refunded` accepts no rule type |
| `src/dEngage.Loyalty.RuleEngine/RuleEngine.cs` | Changed | D22: skip incompatible Reversal rules |
| `src/dEngage.Loyalty.RuleEngine/Processing/ReversalRuleProcessor.cs` | Changed | D22: shared cumulative cap |
| `src/dEngage.Loyalty.RuleEngine/Processing/DelayedPostingPromotionJob.cs` | Changed | H1–H3: remainder, lock, per-row transaction, announcements, tier |
| `src/dEngage.Loyalty.Ledger/RefundService.cs` | Changed | H1 + D22: held take-back, row lock, shared cap |
| `src/dEngage.Loyalty.Api/Rules/RulesAppService.cs` | Changed | D22: refuse edit / re-enable of incompatible rules |
| `src/dEngage.Loyalty.Api/Customers/CustomersAppService.cs`, `CustomersDtos.cs` | Changed | H1: pending nets refunds; `refundedDelta` / `cancelledAt` |
| `src/dEngage.Loyalty.Schema/Entities/HeldPosting.cs`, `Configurations/HeldPostingConfiguration.cs` | Changed | H1: `cancelled_at`, source-event index |
| `src/dEngage.Loyalty.Schema/Entities/HeldPostingRefund.cs`, `Configurations/HeldPostingRefundConfiguration.cs` | Created | H1: append-only take-back record |
| `src/dEngage.Loyalty.Schema/LoyaltyDbContext.cs` | Changed | H1: `HeldPostingRefunds` |
| `src/dEngage.Loyalty.Schema/Migrations/20261008101744_HeldPostingRefundsCr1006.cs` (+ Designer), `LoyaltyDbContextModelSnapshot.cs` | Created / generated | H1 schema migration (generated by `dotnet ef`) |
| `src/dEngage.Loyalty.Schema/Migrations/20261008101801_DisableReversalRulesOnOrderRefundedCr1006.cs` (+ Designer) | Created | D22 data migration |
| `src/dEngage.Loyalty.Engine.Tests/HandlerEvaluatedEventsTests.cs` | Created | D18 guard |
| `src/dEngage.Loyalty.Engine.Tests/RuleTypeCatalogTests.cs` | Changed | D22 |
| `src/dEngage.Loyalty.IntegrationTests/Api/BurnTriggerRuleTypesApiTests.cs` | Changed | D22 API |
| `src/dEngage.Loyalty.IntegrationTests/E2E/CashEventsProgramLiveE2ETests.cs` | Changed | D23 tests; handler constructor |
| `src/dEngage.Loyalty.IntegrationTests/E2E/RefundPathsCr1006E2ETests.cs` | Changed | H1–H3 tests; job constructor |
| `src/dEngage.Loyalty.IntegrationTests/Engine/RuleConfigurationTests.cs` | Changed | Job constructor only (no assertion changed) |
| `web/src/app/features/customers/customer.model.ts`, `customer-event.drawer.ts` | Changed | H1: show refunded / cancelled holds |
| `web/public/i18n/en.json`, `tr.json` | Changed | Two new drawer strings |
| `scripts/loyalty_schema.sql` | Regenerated | `dotnet ef migrations script --idempotent` (appends the two migrations) |
| `scripts/loyalty_schema_reference.md` | Changed | `held_postings.cancelled_at`, `held_posting_refunds`, D22 note |
| `docs/SCOPE_BASELINE.md`, `docs/SOW.md`, `LoyaltySaaSApi.md` | Changed | Part 1a behaviour and contract |
| `.claude/rules/backend-consumer.md`, `backend-ledger.md`, `backend-rule-engine.md`, `01-workflow-and-debugging.md` | Changed | Conventions and pitfalls for Part 1a |

### 7.4 Part 1b — Scope Change Request (text for Jira)

**Title:** Rule configuration & limits by trigger — Part 1b: rule behaviour (CR 2026-10-06)

**Scope:**
1. **D20/D21 — test mode removed.** The checkbox is gone; `configuration.testMode: true` is 400 `test_mode_removed` on create and edit; responses always return `false`; the engine ignores a stored flag. Active / pending earn rules (Spend, Fixed bonus incl. card buckets, Manual adjustment) that had it on are disabled by migration `DisableTestModeEarnRulesCr1006` so they don't start paying unnoticed.
2. **D4/D4a/D4b/D12 — once per customer per rule** for `signup` and `kyc.completed`, on every rule, past awards included, whatever the rule's limits. Guaranteed by the posting key `once:{ruleId}:{contactKey}`. The Customer 360 event drawer shows "No award: bonus already received" with the earlier event (`onceOnlySkips`, additive).
3. **D6/D13 — caps follow On breach on both kinds of rule.** One shared cap check: Clamp trims to what's left, Skip pays nothing and lets the next exclusive rule try; a cap is never exceeded.

**Breaking / visible changes:** `testMode: true` refused; earn rules that were in test mode are disabled at deploy; repeat signup / KYC events stop paying (existing rules too, no clawback); a capped exclusive rule pays only what's left of its cap (it used to overshoot); a stackable rule with On breach = Skip skips instead of trimming its total / per-day cap.

**Migration:** `DisableTestModeEarnRulesCr1006` (data only).

**Pre-deploy queries (run on each environment, results to the PO):**

```sql
-- (a) Every rule with test mode on; the earn ones (active / pending) will be disabled (D21)
SELECT t.slug AS tenant, p.name AS program, r.id, r.name, r.type, r.status
FROM rules r JOIN programs p ON p.id = r.program_id JOIN tenants t ON t.id = r.tenant_id
WHERE r.configuration->>'testMode' = 'true' AND r.status <> 'deleted'
ORDER BY t.slug, p.name, r.type;

-- (b) Rules whose cap behaviour changes (D13): exclusive with a per-customer cap,
--     or any rule with On breach = Skip and a per-customer total / per-day cap
SELECT t.slug AS tenant, p.name AS program, r.id, r.name, r.type, r.stackable,
       r.limits->>'on_breach' AS on_breach, r.limits->>'per_customer_total' AS per_customer_total,
       r.limits->>'per_customer_per_day' AS per_customer_per_day, r.limits->>'per_customer_per_period' AS per_customer_per_period
FROM rules r JOIN programs p ON p.id = r.program_id JOIN tenants t ON t.id = r.tenant_id
WHERE r.status IN ('active', 'pending_approval')
  AND (r.limits ? 'per_customer_total' OR r.limits ? 'per_customer_per_day' OR r.limits ? 'per_customer_per_period')
  AND (r.stackable = false OR r.limits->>'on_breach' = 'Skip');

-- (c) Customers already paid more than once by a signup / KYC rule (D4a: no clawback, for information)
SELECT le.tenant_id AS tenant, r.name AS rule, le.contact_key, COUNT(*) AS awards, SUM(le.delta) AS total
FROM ledger_entries le JOIN rules r ON r.id = le.rule_id
WHERE r.trigger IN ('signup', 'kyc.completed') AND le.reason = 'earn'
GROUP BY le.tenant_id, r.name, le.contact_key
HAVING COUNT(*) > 1;
```

### 7.5 Part 1b implementation record (2026-10-08)

**Verification** (built and tested with `--artifacts-path` to a scratch folder, as in §7.3):

| Check | Result |
|---|---|
| `dotnet build dEngage.Loyalty.sln` | 0 errors, 1 warning (existing) |
| `dotnet test src/dEngage.Loyalty.Engine.Tests` | **110/110 passed** |
| `dotnet test src/dEngage.Loyalty.IntegrationTests` (Docker) | **277/277 passed** (251 at baseline; 26 new across Phase 0, 1a and 1b) |
| Portal `npm test` / `npx ng build --configuration development` | All passed / succeeded |
| Portal `npx ng lint` | The same 3 existing errors in untouched files (§7.3); none in this change |
| Not run | The full local stack and a manual pass through the portal (the migration was applied to the developer's local database afterwards — §12, revision 19) |

**New / changed tests:** `OnceOnlyAndCapsCr1006Tests` (D12: repeat pays once, two rules on one event, next exclusive rule, award before the change, legacy per-customer total, other events unaffected; D13: the §3.7 worked example 150 → 300 → 370 → 390 → 410, exclusive Skip, stackable Skip); `RuleTestModeRemovedCr1006ApiTests` (D20 API); `RuleDataMigrationsCr1006E2ETests` (D21 and D22 migrations on Postgres: exactly the intended rows disabled, nothing deleted, program flagged); `CustomerViewCr1002E2ETests.A_repeated_signup_shows_which_rule_already_paid_and_when` (drawer). **Changed expectation:** `RuleConfigurationTests.Test_mode_evaluates_and_audits_but_posts_nothing` asserted the behaviour D20 removed; it now asserts that a stored flag is ignored and the rule posts (renamed `A_stored_test_mode_flag_is_ignored_and_the_rule_posts`).

**For the Architect's review:**
- D13 removes the exclusive-only gate (`IsLimitExhaustedAsync`); exclusive and stackable rules now share `ClipToLimitAsync`, which also pre-clips budgets for exclusive rules (the in-transaction reservation is unchanged).
- D12 checks twice: `WinnerSelector` (before the transaction, so the next exclusive rule can win) and `LedgerPoster` (inside it, before any budget reservation). The posting key makes a race harmless.
- `LedgerPoster` now leaves skipped rules (once-only repeat, budget exhausted at reservation) out of the `points.earned` summary — the budget case was previously included with its pre-reservation delta.
- `LimitCounterSync` still increments Redis per-customer counters for a once-only repeat that was skipped inside the transaction (rare: only when the two checks disagree under a race). Harmless for once-only rules; noted.

**Files (Part 1b):**

| File | Action | Reason |
|---|---|---|
| `src/dEngage.Loyalty.RuleEngine/Processing/OnceOnlyAward.cs` | Created | D12: what counts as "already awarded", the once key |
| `src/dEngage.Loyalty.RuleEngine/Processing/IRuleLimitEvaluator.cs`, `RuleLimitEvaluator.cs` | Changed | D12: `HasOnceOnlyAwardAsync` |
| `src/dEngage.Loyalty.RuleEngine/Processing/WinnerSelector.cs` | Changed | D12 gate; D13 one cap check with On breach on every cap |
| `src/dEngage.Loyalty.RuleEngine/Processing/LedgerPoster.cs` | Changed | D20 test mode removed; D12 key and in-transaction re-check |
| `src/dEngage.Loyalty.Api/Rules/RulesValidators.cs`, `RulesAppService.cs` | Changed | D20: refuse `testMode: true`; report `false` |
| `src/dEngage.Loyalty.Api/Customers/CustomersAppService.cs`, `CustomersDtos.cs` | Changed | D12: `onceOnlySkips` in the event drawer |
| `src/dEngage.Loyalty.Schema/Migrations/20261008120415_DisableTestModeEarnRulesCr1006.cs` (+ Designer), `LoyaltyDbContextModelSnapshot.cs` | Created / generated | D21 data migration |
| `src/dEngage.Loyalty.IntegrationTests/Engine/OnceOnlyAndCapsCr1006Tests.cs` | Created | D12, D13 |
| `src/dEngage.Loyalty.IntegrationTests/Api/RuleTestModeRemovedCr1006ApiTests.cs` | Created | D20 |
| `src/dEngage.Loyalty.IntegrationTests/E2E/RuleDataMigrationsCr1006E2ETests.cs` | Created | D21, D22 migrations |
| `src/dEngage.Loyalty.IntegrationTests/E2E/CustomerViewCr1002E2ETests.cs` | Changed | D12 drawer test |
| `src/dEngage.Loyalty.IntegrationTests/Engine/RuleConfigurationTests.cs` | Changed | D20 expectation (explained above) |
| `web/src/app/features/programs/rules/rule-form.page.ts`, `rule.model.ts` | Changed | D20: checkbox removed |
| `web/src/app/features/customers/customer.model.ts`, `customer-event.drawer.ts`, `web/public/i18n/en.json`, `tr.json` | Changed | D12: drawer line |
| `scripts/loyalty_schema.sql` | Regenerated | Appends the D21 migration |
| `scripts/loyalty_schema_reference.md` | Changed | Last migration; `testMode` note |
| `docs/SCOPE_BASELINE.md`, `docs/SOW.md`, `LoyaltySaaSApi.md` | Changed | Part 1b behaviour and contract; as-built correction: expiry override stored but not applied |
| `.claude/rules/backend-rule-engine.md`, `01-workflow-and-debugging.md` | Changed | Conventions and pitfalls for Part 1b |

### 7.6 Phase 2 — Scope Change Request (text for Jira)

**Title:** Rule configuration & limits by trigger — Phase 2: rule form by trigger (CR 2026-10-06)

**Scope (D5, D8, D10, D11, D14, D17):** a NEW rule shows and may set only the Configuration and Limits fields that apply to its trigger and rule type, per the tables in §3.2 — e.g. no Min event amount or Per customer total on signup / KYC, no Delayed posting outside amount-based earn, no Configuration on redeem / transfer / reversal rules, no On breach on burn rules, no expiry override on a CASH target. The list lives once, in `RuleFieldCatalog` (RuleEngine, next to `RuleTypeCatalog`): the API rejects other non-default values on create (400 `field_not_applicable`) and serves the list as `applicableFields` in `GET rules/metadata`, which the portal reads. Existing rules are unchecked and show every field on edit (D5). Also: the Configuration section translated (en/tr); "Redemption terms" / "Transfer terms" as the calculation title on redeem / transfer rules (D17); a hint on Spend's rate and Min event amount for tenant-defined triggers (D14).

**Breaking / visible changes:** API clients creating rules with a non-default value in a field that doesn't apply get 400 (defaults are still accepted). No migration, no change to existing rules or to payouts.

**Pre-deploy query (information for the PO):** none needed — existing rules are untouched.

### 7.7 Phase 2 implementation record (2026-10-08)

**Verification** (built and tested with `--artifacts-path`, as in §7.3):

| Check | Result |
|---|---|
| `dotnet build dEngage.Loyalty.sln` | 0 errors, 1 warning (existing) |
| `dotnet test src/dEngage.Loyalty.Engine.Tests` | **126/126 passed** (16 new: `RuleFieldCatalogTests`, the §3.2 tables) |
| `dotnet test src/dEngage.Loyalty.IntegrationTests` (Docker) | **285/285 passed** (8 new: `RuleFieldsByTriggerCr1006ApiTests`) |
| Portal `npm test` | **149/149 passed** (4 new: `rule-field-visibility.spec.ts`) |
| Portal `npx ng build --configuration development` | Succeeded |
| Portal `npx ng lint` | The same 3 existing errors in untouched files; none in this change |
| Not run | The full local stack and a manual pass through the rule form |

**For the Architect's review:**
- The applicability list is defined once (`RuleFieldCatalog`) and served to the portal; the portal holds no copy (`rule-field-visibility.ts` only reads it).
- Create validation rejects only **non-default** values in fields that don't apply, so clients that always send the defaults keep working.
- On a new rule, a field that stops applying when the admin changes trigger, type or target is reset by an `effect`, so a hidden value is never validated or sent; `buildLimits` / `buildConfiguration` also skip fields that don't apply.
- `ValidationApiException`'s message is not in the response body (framework behaviour); the expiry-on-CASH check therefore surfaces as a generic 400 to API clients (the portal hides the field, so admins don't hit it).

**Files (Phase 2):**

| File | Action | Reason |
|---|---|---|
| `src/dEngage.Loyalty.RuleEngine/Metadata/RuleFieldCatalog.cs` | Created | The fields per trigger and type; non-default check |
| `src/dEngage.Loyalty.Api/Rules/RulesValidators.cs` | Changed | Create validation against the catalog |
| `src/dEngage.Loyalty.Api/Rules/RulesAppService.cs` | Changed | Expiry override not on a CASH target; `applicableFields` in metadata |
| `src/dEngage.Loyalty.Api/Rules/RulesDtos.cs` | Changed | `ApplicableFieldsResponse` (additive) |
| `src/dEngage.Loyalty.Engine.Tests/RuleFieldCatalogTests.cs` | Created | The §3.2 tables |
| `src/dEngage.Loyalty.IntegrationTests/Api/RuleFieldsByTriggerCr1006ApiTests.cs` | Created | API behaviour (create, edit, CASH, generic, metadata) |
| `web/src/app/features/programs/rules/rule-field-visibility.ts` (+ `.spec.ts`) | Created | Reads `applicableFields` |
| `web/src/app/features/programs/rules/rule-form.page.ts`, `rule.model.ts` | Changed | Fields per trigger, translated Configuration, terms titles, hints |
| `web/public/i18n/en.json`, `tr.json` | Changed | 25 strings each |
| `docs/SCOPE_BASELINE.md`, `docs/SOW.md`, `LoyaltySaaSApi.md` | Changed | Phase 2 behaviour and contract |
| `.claude/rules/backend-rule-engine.md`, `backend-api.md`, `01-workflow-and-debugging.md` | Changed | Keep `RuleFieldCatalog` the one list; new pitfall |

### 7.8 Phase 4 — Scope Change Request (text for Jira)

**Title:** Rule configuration & limits by trigger — Phase 4: rounding (CR 2026-10-06)

**Scope (D2, §3.8):** a program gets a **default rounding** (`down` / `nearest` / `up`, default `down`) that rules inherit when their own Rounding is "Inherit from program". Every Spend, Fixed bonus and Manual adjustment award is rounded **once**, in winner selection, to the target wallet's decimals in the rule's or program's direction, after the calculation and Max per event and before the limits (so rounding up can't push past a cap). Spend stops rounding inside its handler. Redeem cash (always down to the cash wallet's decimals), transfer and reversal are unchanged. The rule form shows "Inherit from program (Down)" with the program's actual value; the program overview has the setting.

**Behaviour changes:**
- Default `down` reproduces the Spend payouts exactly (asserted by test for wallet decimals 0–4).
- Fixed bonuses and manual adjustments are now rounded to their wallet's decimals: one with more decimals than its wallet (e.g. 25.555 points into a whole-point wallet) used to post the fraction and now posts 25 on `down`.
- A rule with an explicit Rounding on a Fixed bonus / Manual adjustment now rounds to the wallet's decimals instead of a fixed 2 places.
- Directions apply to the award's size: a negative adjustment rounds toward zero on `down` (it rounded away from zero before).
- Picking `nearest` or `up` for a program changes the payouts of every rule that inherits.

**Migration:** `ProgramDefaultRoundingCr1006` (additive: `programs.default_rounding varchar(10) NOT NULL DEFAULT 'down'`).

**Pre-deploy queries (results to the PO):**

```sql
-- (a) Fixed bonus rules whose amount has more decimals than their wallet — their payout changes
SELECT t.slug AS tenant, p.name AS program, r.name, (r.calculation->>'amount') AS amount,
       COALESCE((a.config->>'decimals')::int, 0) AS wallet_decimals
FROM rules r JOIN programs p ON p.id = r.program_id JOIN tenants t ON t.id = r.tenant_id
JOIN account_types a ON a.id = r.target_account_type_id
WHERE r.type = 'FixedBonusRule' AND r.status IN ('active', 'pending_approval')
  AND scale((r.calculation->>'amount')::numeric) > COALESCE((a.config->>'decimals')::int, 0);

-- (b) Fixed bonus / manual adjustment rules with an explicit rounding — precision changes from 2 places to the wallet's
SELECT t.slug AS tenant, p.name AS program, r.name, r.type, r.configuration->>'rounding' AS rounding
FROM rules r JOIN programs p ON p.id = r.program_id JOIN tenants t ON t.id = r.tenant_id
WHERE r.type IN ('FixedBonusRule', 'ManualAdjustmentRule') AND r.status IN ('active', 'pending_approval')
  AND r.configuration->>'rounding' IS NOT NULL;
```

The pre-build check in §3.8 is answered: nothing rejected a fixed bonus with more decimals than its wallet allows (only "> 0" was validated), so such rules exist wherever query (a) returns rows.

### 7.9 Phase 4 implementation record (2026-10-08)

**Verification** (built and tested with `--artifacts-path`, as in §7.3):

| Check | Result |
|---|---|
| `dotnet build dEngage.Loyalty.sln` | 0 errors, 1 warning (existing) |
| `dotnet test src/dEngage.Loyalty.Engine.Tests` | **123/123 passed** |
| `dotnet test src/dEngage.Loyalty.IntegrationTests` (Docker) | **303/303 passed** (18 new: `RoundingCr1006Tests` 15, `ProgramDefaultRoundingCr1006ApiTests` 3) |
| Portal `npm test` / `npx ng build --configuration development` | 149/149 passed / succeeded |
| Portal `npx ng lint` | The same 3 existing errors in untouched files; none in this change |
| Not run | The full local stack and a manual pass (the migration was applied to the developer's local database afterwards — all 7 programs got `down`; §12, revision 22) |

**Changed expectations (explained):**
- `RuleTypeHandlerRegistryTests`: three `SpendRuleHandler` tests asserted that the handler rounds down (1.3.CL item 2). The agreed design moves rounding to one place, so they now assert the unrounded rate × amount; the same payouts (12, 12.8, 12.89, 12.897 for wallet decimals 0–4) are asserted end to end in `RoundingCr1006Tests`.
- `RuleDataMigrationsCr1006E2ETests`: seeds its program with raw SQL, because the current EF model maps `default_rounding`, which doesn't exist yet at the migration it starts from. Assertions unchanged.
- `program-overview.page.spec.ts`: the `Program` fixture gains `defaultRounding: 'down'` (the type requires it). Assertions unchanged.

**For the Architect's review:**
- The program's direction reaches the engine per event through the already-loaded `RuleEvaluationContext.Program` (`IWinnerSelector.SelectAsync` gains an optional parameter), so there's no rule-cache invalidation to manage when a program's default changes.
- Rounding is applied before `ClipToLimitAsync` (a cap trims an already-rounded award); the existing final rounding calls are idempotent and stay for the legacy multiplier path.
- Directions now apply to the award's size (a negative adjustment rounds toward zero on `down`) — a choice made here, as the design didn't say; flagged for confirmation.

**Files (Phase 4):**

| File | Action | Reason |
|---|---|---|
| `src/dEngage.Loyalty.Shared/Constants/RoundingDirection.cs` | Created | The three directions |
| `src/dEngage.Loyalty.Schema/Entities/Program.cs`, `Configurations/ProgramConfiguration.cs` | Changed | `DefaultRounding` |
| `src/dEngage.Loyalty.Schema/Migrations/20261008130056_ProgramDefaultRoundingCr1006.cs` (+ Designer), `LoyaltyDbContextModelSnapshot.cs` | Created / generated | The column |
| `src/dEngage.Loyalty.Api/Programs/ProgramsDtos.cs`, `ProgramsValidators.cs`, `ProgramsAppService.cs` | Changed | `defaultRounding` on create / update / response / publish snapshot; validation; unpublished-changes flag |
| `src/dEngage.Loyalty.RuleEngine/Processing/IWinnerSelector.cs`, `WinnerSelector.cs` | Changed | One rounding step: wallet decimals + rule / program direction, before the limits |
| `src/dEngage.Loyalty.RuleEngine/RuleEngine.cs` | Changed | Passes the program's default |
| `src/dEngage.Loyalty.RuleEngine/Calculation/SpendRuleHandler.cs` | Changed | No longer rounds |
| `src/dEngage.Loyalty.Engine.Tests/RuleTypeHandlerRegistryTests.cs` | Changed | Spend handler expectations (explained above) |
| `src/dEngage.Loyalty.IntegrationTests/Engine/RoundingCr1006Tests.cs` | Created | Rounding end to end |
| `src/dEngage.Loyalty.IntegrationTests/Api/ProgramDefaultRoundingCr1006ApiTests.cs` | Created | Program API |
| `src/dEngage.Loyalty.IntegrationTests/E2E/RuleDataMigrationsCr1006E2ETests.cs` | Changed | Raw-SQL program seed |
| `web/src/app/features/programs/program.model.ts`, `program-overview.page.ts`, `program-overview.page.spec.ts` | Changed | Default rounding setting; fixture |
| `web/src/app/features/programs/rules/rule-form.page.ts` | Changed | "Inherit from program (…)" shows the program's direction |
| `web/public/i18n/en.json`, `tr.json` | Changed | 3 strings each |
| `scripts/loyalty_schema.sql` | Regenerated | Appends the migration |
| `scripts/loyalty_schema_reference.md` | Changed | `programs.default_rounding`; last migration |
| `docs/SCOPE_BASELINE.md`, `docs/SOW.md`, `LoyaltySaaSApi.md` | Changed | Phase 4 behaviour and contract |
| `.claude/rules/backend-rule-engine.md` | Changed | Where rounding happens |

### 7.10 Phase 5 — Scope Change Request (text for Jira)

**Title:** Rule configuration & limits by trigger — Phase 5: expiry override (CR 2026-10-06)

**Scope (D2, E1–E3, §3.9):** a rule's **Points expire after (days)** now works. Points posted by a rule with an override expire that many days after the posting (after the release for a delayed posting) instead of on the wallet's expiry — longer or shorter (E1), and also in a wallet that has no expiry (E2). Every new earn / transfer-in posting into a POINTS wallet stores its own expiry date (`ledger_entries.expires_at`): the rule's override, else the wallet's `expiration_days`, else none. Expiry, redemptions, transfers out and reward purchases use the **soonest-expiring points first**, points with no expiry last (E3). The expiring-soon warning and the Customer 360 "expiring" figure use the same dates.

**Behaviour changes:**
- Without any override the result is the same as before: an entry's date is earn date + wallet days, so soonest-expiring first is oldest first (asserted by test).
- Rules that already have `expiryOverrideDays` saved start applying it to points they post **after** the deploy; points they posted before keep the wallet's expiry (no backfill).
- When a lot has an override, spending uses it before older lots that expire later, so the older lot lives longer than under oldest-first.
- A wallet's later `expiration_days` change no longer moves the expiry of points posted after this deploy (their date is stored); older points still follow the wallet's current setting.
- The API rejects `expiryOverrideDays` ≤ 0 (400) on create and edit.

**Migration:** `LedgerExpiresAtCr1006` (additive: `ledger_entries.expires_at timestamptz NULL`; no backfill; the ledger stays append-only).

**Pre-deploy queries (results to the PO):**

```sql
-- (a) Rules with an expiry override saved — it starts applying at deploy (new postings only)
SELECT t.slug AS tenant, p.name AS program, r.name, r.type, r.status,
       r.configuration->>'expiryOverrideDays' AS override_days,
       a.config->>'expiration_days' AS wallet_expiration_days   -- null = the wallet has no expiry (E2)
FROM rules r JOIN programs p ON p.id = r.program_id JOIN tenants t ON t.id = r.tenant_id
JOIN account_types a ON a.id = r.target_account_type_id
WHERE r.status IN ('active', 'pending_approval')
  AND r.configuration->>'expiryOverrideDays' IS NOT NULL;

-- (b) Saved overrides the engine ignores (≤ 0) — the rule can't be saved again until fixed
SELECT t.slug AS tenant, r.name, r.configuration->>'expiryOverrideDays' AS override_days
FROM rules r JOIN tenants t ON t.id = r.tenant_id
WHERE r.configuration->>'expiryOverrideDays' IS NOT NULL
  AND (r.configuration->>'expiryOverrideDays')::numeric <= 0;
```

### 7.11 Phase 5 implementation record (2026-10-08)

**Verification** (built and tested with `--artifacts-path`, as in §7.3):

| Check | Result |
|---|---|
| `dotnet build dEngage.Loyalty.sln` | 0 errors, 1 warning (existing, `RuleVersioningServiceTests.cs:57`) |
| `dotnet test src/dEngage.Loyalty.Engine.Tests` | **123/123 passed** |
| `dotnet test src/dEngage.Loyalty.IntegrationTests` (Docker) | **314/314 passed** (11 new: `ExpiryOverrideCr1006E2ETests` 7, `ExpiryOverridePostingCr1006Tests` 3, `CustomerViewCr1002E2ETests` 1). One run had a transient Testcontainers timeout in `ProgramPublicationCl13MigrationE2ETests`; it passed alone and in the next full run |
| Portal `npm test` / `npx ng build --configuration development` | 149/149 passed / succeeded |
| Portal `npx ng lint` / `npx prettier --check` | The same 3 existing errors in untouched files; none in this change / formatted |
| `scripts/loyalty_schema.sql` | Regenerated; append-only (no existing line changed) |
| Not run | The full local stack and a manual pass (the migration was applied to the developer's local database afterwards — 39 existing ledger rows left undated, no rule has an override saved; §12, revision 24) |

**Changed tests (explained):** `StreakCampaignModuleTests` (3 setups) and `StreakMonthlySumTests` (2 setups) mock `ILedgerService.AddEntryAsync`, which gained an optional `expiresAt` parameter; Moq expression trees can't omit optional arguments, so the setups add `It.IsAny<DateTime?>()`. Assertions unchanged. No existing expiry test changed — `PointsExpirationJobE2ETests` passes as it was, which is the equivalence check.

**For the Architect's review:**
- `LedgerService.AddEntryAsync` sets `expires_at` only for `earn` / `transfer_in` into a POINTS wallet: the caller's date (`LedgerPoster`, `DelayedPostingPromotionJob`), else the wallet's `expiration_days` read at posting; a malformed wallet config gives no date (falls back to the wallet rule at expiry time, as before).
- The delayed-posting release reads the rule's override **as it stands at release** (D15 already releases without rechecking the rule; this is the one rule setting it reads).
- Consumption is still allocated as a total over the lots (like the old FIFO), not replayed in time order: a redemption can be counted against a lot earned after it. Unchanged in kind; only the order is new.
- `PointsExpirationJob` now also runs for POINTS wallets without `expiration_days`, limited to accounts that have a dated lot (`EXISTS ... expires_at IS NOT NULL`). There is no index on `expires_at`; the existing `(tenant_id, customer_account_id, created_at)` index serves the per-account lookups. Worth watching on large tenants.
- Wallets without `warning_days` (so every wallet without expiry, E2) get no expiring-soon warning for override lots — the warning settings live on the wallet.
- `cutoff_date` in the `points.expired` payload keeps its meaning (today − wallet days; today for a wallet without expiry).

**Self-review fixes (2026-10-08, after the first record):** a technical review of the Phase 5 code against the existing patterns found three gaps, now fixed and re-verified (build 0 errors / 1 existing warning, Engine 123/123, Integration 314/314, portal 149/149, build OK, lint unchanged):
- `LedgerService` stored a rule's override on any earn, including into a CASH wallet (possible only for a rule saved before Phase 2's create check). It now stores a date only for a POINTS wallet; `Postings_record_their_expiry_date_from_the_wallet_or_the_override` asserts it (fails without the fix).
- `DelayedPostingPromotionJob` read the rule row twice per release (override, then announcements); it now reads it once.
- The portal's expiry field had only the HTML `min`; it now has `Validators.min(1)` with an inline error, like Hold days (new key `rules.config.expiryOverride.min`, en/tr).
- One full Integration run had two 1 ms failures at container start (`CashEventsProgramLiveE2ETests`, `CustomerViewCr1002E2ETests`); both classes passed alone (12/12) and the next full run passed 314/314.

**Files (Phase 5):**

| File | Action | Reason |
|---|---|---|
| `src/dEngage.Loyalty.Schema/Entities/LedgerEntry.cs`, `Configurations/LedgerEntryConfiguration.cs` | Changed | `ExpiresAt` |
| `src/dEngage.Loyalty.Schema/Migrations/20261008162920_LedgerExpiresAtCr1006.cs` (+ Designer), `LoyaltyDbContextModelSnapshot.cs` | Created / generated | The column |
| `src/dEngage.Loyalty.Ledger/ILedgerService.cs`, `LedgerService.cs` | Changed | `expiresAt` parameter; date stored on earn / transfer_in postings |
| `src/dEngage.Loyalty.Ledger/PointsExpirationJob.cs` | Changed | Expire by each lot's date, soonest first; wallets without expiry |
| `src/dEngage.Loyalty.Ledger/PointsExpiringDetectorJob.cs` | Changed | Same ordering and dates for the warning |
| `src/dEngage.Loyalty.RuleEngine/Processing/LedgerPoster.cs` | Changed | Passes the rule's override |
| `src/dEngage.Loyalty.RuleEngine/Processing/DelayedPostingPromotionJob.cs` | Changed | Override dated from the release |
| `src/dEngage.Loyalty.Api/Customers/CustomerViewRules.cs`, `CustomersAppService.cs`, `CustomersDtos.cs` | Changed | Customer 360 expiring figure uses the same dates; comment |
| `src/dEngage.Loyalty.Api/Rules/RulesValidators.cs` | Changed | `expiryOverrideDays` > 0 on create and edit |
| `src/dEngage.Loyalty.Engine.Tests/StreakCampaignModuleTests.cs`, `src/dEngage.Loyalty.IntegrationTests/Engine/StreakMonthlySumTests.cs` | Changed | Moq setups (explained above) |
| `src/dEngage.Loyalty.IntegrationTests/E2E/ExpiryOverrideCr1006E2ETests.cs` | Created | Expiry, ordering, warning and stored dates (Postgres), incl. no date on cash |
| `src/dEngage.Loyalty.IntegrationTests/Engine/ExpiryOverridePostingCr1006Tests.cs` | Created | Engine and release postings store the right date |
| `src/dEngage.Loyalty.IntegrationTests/E2E/CustomerViewCr1002E2ETests.cs` | Changed | Customer 360 expiring figure with an override lot |
| `web/src/app/features/programs/rules/rule-form.page.ts` | Changed | Label, `min="1"`, "Wallet default: N" / "never" placeholder, hint |
| `web/public/i18n/en.json`, `tr.json` | Changed | Expiry override strings (old placeholder key removed) |
| `scripts/loyalty_schema.sql` | Regenerated | Appends the migration |
| `scripts/loyalty_schema_reference.md` | Changed | `ledger_entries.expires_at`; `expiryOverrideDays`; last migration |
| `docs/SCOPE_BASELINE.md`, `docs/SOW.md`, `LoyaltySaaSApi.md` | Changed | Phase 5 behaviour and contract |
| `.claude/rules/backend-ledger.md` | Changed | Expiry by each lot's date, one ordering everywhere |
| `docs/scope-changes/2026-10-05-rule-config-limits-by-trigger.md` | Changed | Revision 23 (§7.10, §7.11) |

### 7.12 R15 — redelivered events: implementation record (2026-10-08)

**Type:** bug fix, no scope change (`00-project-guardrails.md` §1). Found while checking D18 after Phase 5.

**The bug.** The worker marks an event `Failed` and dead-letters it when any step throws. When it is replayed from the dead-letter queue (or redelivered after a crash before the ack), the whole event runs again, including the rule engine. The postings were deduped by their keys, but:
- the rule budget (`rule_limit_counters`) was reserved again and the Redis per-customer counters were incremented again;
- on `signup` / `kyc.completed`, the first delivery's award made the rule "already paid" (D12), so the **next exclusive rule paid a second award for the same event** (reproduced: 130 instead of 100);
- the same with a per-customer cap reached by the first delivery's award (On breach Skip, D13);
- a replayed **partial** Reversal redid the reversal: the ledger deduped it, but the budget was given back again and `points.reversed` was enqueued again, which the outbox dedupe index refuses — the replay always failed.

D18 (Part 1a) fixed the engine running twice **within** one delivery; this is the same symptom **across** deliveries.

**The fix:**
- `ILedgerPoster.HasPostedAsync`: before winner selection, `RuleEngine` asks whether any of the event's candidate rules already posted or held under its own posting key for **this** event id (unique-index lookups on `ledger_entries` and `held_postings`). `PostAsync` writes all of an event's rules in one transaction, so one hit means the earlier delivery decided the event. Then nothing is selected, posted, reserved or counted; campaigns (their own idempotency) and the tier check still run, since the first delivery may have failed in either. The key computation is shared with `PostAsync`.
- `ReversalRuleProcessor`: skips an original entry whose reversal key is already written, before the cap, the budget release and the announcement — the pattern the redeem / transfer handlers already use.
- Redeem / transfer (`PointsRedeemHandler`, `PointsTransferHandler`) already check their posting key before reserving; unchanged.

**Verification:**

| Check | Result |
|---|---|
| New tests on the unfixed code | 7 of 8 failed as expected (balances 130 instead of 100, budget 80 instead of 40, counters incremented twice, reversal replay threw `ux_outbox_dedup`); the control (a new event id pays again) passed |
| `dotnet build dEngage.Loyalty.sln` | 0 errors, 1 warning (existing) |
| `dotnet test src/dEngage.Loyalty.Engine.Tests` | **132/132 passed** (9 new: `ConsumerEvaluatesOnceCr1006Tests`) |
| `dotnet test src/dEngage.Loyalty.IntegrationTests` (Docker) | **322/322 passed** (8 new: `RedeliveryCr1006Tests` 7, `RefundPathsCr1006E2ETests` 1) |
| Not run | The full local stack and a real dead-letter replay; portal untouched |

**New tests:**
- `RedeliveryCr1006Tests` (engine harness): a replayed signup / KYC doesn't pay the next exclusive rule; a replayed event doesn't let the next exclusive rule pay after the first reached its cap; budget reserved once; Redis counters incremented once; a delayed posting held once with its budget once; control — a new event id pays, reserves and counts again.
- `RefundPathsCr1006E2ETests.A_redelivered_partial_reversal_gives_the_budget_back_once` (Postgres).
- `ConsumerEvaluatesOnceCr1006Tests` (through `EventConsumerWorker.ProcessAsync`): each of `order.created`, `signup`, `kyc.completed`, `card.transaction`, `remittance`, `points.adjusted` and a tenant event runs the engine once per delivery (R5's per-event-type check for D18); a processed duplicate isn't evaluated; a failed delivery is evaluated again when replayed and then marked processed.

**For the Architect's review:**
- The guard is per event, not per rule: if the first delivery posted any rule, none is re-evaluated. Correct because `PostAsync` is one transaction; a first delivery that posted nothing (every rule skipped) is evaluated afresh, which can't double anything.
- One extra indexed query per event that matched rules (two when nothing was posted: ledger, then held postings).
- `EventConsumerWorker.ProcessAsync` became `internal` for the worker test (Engine.Tests already sees the Consumer's internals). Making the Consumer visible to IntegrationTests was tried and reverted: its generated `Program` class clashes with the Api's.
- Not covered by a test: that the tier check really re-runs on a replay — the harness can't make the first tier check fail. The code path is the existing "nothing applied" branch.
- Still open, unchanged (Part 1b note): within one delivery, `LimitCounterSync` increments Redis counters with the pre-reservation amount, and also for rules skipped inside the posting transaction. Separate from R15; not changed here.

**Files (R15):**

| File | Action | Reason |
|---|---|---|
| `src/dEngage.Loyalty.RuleEngine/Processing/ILedgerPoster.cs`, `LedgerPoster.cs` | Changed | `HasPostedAsync`; posting key computed in one place |
| `src/dEngage.Loyalty.RuleEngine/RuleEngine.cs` | Changed | Redelivery guard before winner selection; tier check still runs |
| `src/dEngage.Loyalty.RuleEngine/Processing/ReversalRuleProcessor.cs` | Changed | Skips an entry already reversed |
| `src/dEngage.Loyalty.Consumer/EventConsumerWorker.cs` | Changed | `ProcessAsync` internal (test access only) |
| `src/dEngage.Loyalty.IntegrationTests/Engine/RedeliveryCr1006Tests.cs` | Created | Reproduces each redelivery case |
| `src/dEngage.Loyalty.IntegrationTests/E2E/RefundPathsCr1006E2ETests.cs` | Changed | Replayed partial reversal |
| `src/dEngage.Loyalty.Engine.Tests/ConsumerEvaluatesOnceCr1006Tests.cs` | Created | One engine run per delivery, per event type |
| `.claude/rules/backend-rule-engine.md`, `backend-consumer.md`, `01-workflow-and-debugging.md` | Changed | Redelivery behaviour; pitfall row |
| `docs/SCOPE_BASELINE.md` | Changed | Revision row (bug fix) |
| `docs/scope-changes/2026-10-05-rule-config-limits-by-trigger.md` | Changed | Revision 26 (item 14, R15, §7.12) |

## 8. Open questions

1. ~~**Q9 — Reversal rules.**~~ **Decided 2026-10-08 as D22** (don't allow them on `order.refunded`). Original question: If Phase 0 confirms the double reversal: keep Reversal rules on `order.refunded` alongside the built-in refund, or allow only one of them?
2. ~~**Q10 — Existing earn rules with test mode on (from D20).**~~ **Decided 2026-10-08 as D21 — option A (disable in the Phase 1 data migration).** Original question: Removing test mode would make them start paying. Disable them in the Phase 1 data migration, so admins re-enable them deliberately (recommended, §3.10)? Or leave them active and let them start paying at deploy?
3. ~~**Q11 — `cash.spent` with insufficient balance.**~~ **Decided 2026-10-08 as D23** (report it).

---

## 9. Explicitly out of scope

- A dry run for redeem/transfer rules (D9).
- Test mode in any form, including the earn shadow run, `is_test`, the Test results panel and "Go live" (D20).
- Clawback of past duplicate signup/KYC payments (D4a).
- Changing or migrating existing rules' saved values (D5).
- A minimum transfer amount (D8).
- Specific failure reasons for blocked burns (`cooldown_active`, `budget_exhausted`) — later, its own decision.
- Program time zones for day/week boundaries — a separate request.
- Moving per-customer caps into the transactional, reserved path (R9).
- Backfilling `expires_at` on existing ledger rows.
- Changes to Reversal rules beyond hiding fields, until Q9 is decided.
- Any change to `reward.purchase`.

---

## 10. Next step

1. ~~Stakeholder decisions~~ Done 2026-10-06 (§0).
2. ~~**Phase 0:** write the refund integration test (needs Docker for Testcontainers). No behaviour change.~~ Done 2026-10-08 (§7.1).
3. ~~Decide Q9 on the Phase 0 result.~~ Done 2026-10-08 (D22).
4. ~~Decide Q10 (existing rules with test mode on) before Phase 1.~~ Done 2026-10-08 (D21).
5. ~~Part 1a~~ implemented 2026-10-08 (§7.3): raise its Jira Scope Change Request with the §7.2 text, run the pre-deploy queries, and get the Architect's diff review.
6. ~~Part 1b~~ implemented 2026-10-08 (§7.5): raise its Jira Scope Change Request with the §7.4 text, run its pre-deploy queries, and get the Architect's diff review.
7. ~~Phase 2~~ implemented 2026-10-08 (§7.7): raise its Jira Scope Change Request with the §7.6 text and get the Architect's diff review.
8. ~~Phase 4~~ implemented 2026-10-08 (§7.9): raise its Jira Scope Change Request with the §7.8 text, run its pre-deploy queries, and get the Architect's diff review.
9. ~~Phase 5~~ implemented 2026-10-08 (§7.11): raise its Jira Scope Change Request with the §7.10 text, run its pre-deploy queries, and get the Architect's diff review.
10. ~~R15 redelivery fix~~ implemented 2026-10-08 (§7.12): a bug fix — include it in the Part 1a release.
11. Next: the Architect's review of the whole branch, then deploy in phase order (1a, 1b, 2, 4, 5) or as one release, as the Architect decides.

---

## 11. Document record (review record per `00-project-guardrails.md` §6)

| File | Action | Reason |
|---|---|---|
| `docs/scope-changes/2026-10-05-rule-config-limits-by-trigger.md` | Created (revision 1, 2026-10-05) | Analysis of rule Configuration and Limits per trigger event, for PO + Architect review |
| same | Changed (cross-reference, by Moiz) | Link to the complaints/stamps removal CR |
| same | Changed (revisions 2–3) | Re-read against 1.6/1.7 code and the birthday removal |
| same | Changed (revisions 4–12) | Stakeholder decisions D2–D19 and E1–E3; test mode, delayed posting, once-only, caps and D2 designs |
| same | Changed (revision 13) | Restructured to the project's scope-change format: decisions first (§0), "Affects (docs)", contracts check, consolidated risks, verification, out of scope, next step, this record; two consistency fixes (see §12) |
| same | Changed (revision 14, 2026-10-08) | Final verdict D20: test mode removed from every rule; D9 shadow run and D16 superseded; Phase 3 dropped; §3.10, Q10, R11–R12 added |
| same | Changed (revision 15, 2026-10-08) | Q10 decided as D21 (option A: disable existing earn rules with test mode on in the Phase 1 data migration) |

| `src/dEngage.Loyalty.IntegrationTests/E2E/RefundPathsCr1006E2ETests.cs` | Created (Phase 0, 2026-10-08) | Refund integration tests confirming the double reversal and H1; regression tests for Phase 1 |
| `docs/scope-changes/2026-10-05-rule-config-limits-by-trigger.md` | Changed (revision 16, 2026-10-08) | Phase 0 results recorded (§2.4, §2.8, §6 R10, §7.1, §8, §10) |

Part 1a files are listed in §7.3.

---

## 12. Revision history

| Date | Change | By |
|---|---|---|
| 2026-10-05 | Initial draft for PO + Architect review | Claude Code, at the request of Moiz |
| 2026-10-05 | Cross-reference to the complaints/stamps removal added | Moiz |
| 2026-10-06 | Revision 2: re-read against 1.6/1.7 code — stamp/expiry/`points.expired` removed; redeem/transfer rule-driven; findings on Test mode, Max per event and On breach for burn rules; possible double reversal flagged | Claude Code, at the request of Moiz |
| 2026-10-06 | Revision 3: birthday bonus removed (CR 2026-10-05 addendum A) | Claude Code, at the request of Moiz |
| 2026-10-06 | Revision 4: stakeholder answers D2, D4, D5, D6, D7; follow-ups F1–F4 | Claude Code, at the request of Moiz |
| 2026-10-06 | Revision 5: test mode analysed in depth; recommendation for Q1 | Claude Code, at the request of Moiz |
| 2026-10-06 | Revision 6: design for D2 (expiry override, rounding) | Claude Code, at the request of Moiz |
| 2026-10-06 | Revision 7: expiry decisions E1–E3 | Claude Code, at the request of Moiz |
| 2026-10-06 | Revision 8: delayed posting analysed (H1–H5); recommendation for Q3 | Claude Code, at the request of Moiz |
| 2026-10-06 | Revision 9: D4a, D4b; design for D4 | Claude Code, at the request of Moiz |
| 2026-10-06 | Revision 10: Q6 analysed; D8 | Claude Code, at the request of Moiz |
| 2026-10-06 | Revision 11: D9–D13; caps design, open items, phases | Claude Code, at the request of Moiz |
| 2026-10-06 | Revision 12: D14–D19 (F3, H4, O1–O4) | Claude Code, at the request of Moiz |
| 2026-10-06 | Revision 13: restructured to the scope-change format required by `.claude/rules/00-project-guardrails.md` §1, §2, §5, §6 and the approved CR 2026-10-05 layout — added header with type per item, "Affects (docs)" (incl. the as-built drift in `SCOPE_BASELINE.md` l.28 / `SOW.md` l.63 and the rule files that will change), contracts check, consolidated risks R1–R10, verification commands, out of scope, next step, document record. Consistency fixes: (1) Rounding in §3.2 now shows for groups B and E, matching the agreed design (it applies to Fixed bonus and Manual adjustment); (2) the old impact row "Reversal rules respect Limits / Test mode" is replaced by D9's hide/reject and Q9. No decision changed. | Claude Code, at the request of Moiz |
| 2026-10-08 | Revision 14: final stakeholder verdict recorded as D20 — "execute with test mode removal": test mode removed from every rule (item 11, §3.10); D9's earn shadow run and D16 superseded (kept, struck through); Phase 3 dropped; Q10 added (existing earn rules with test mode on — recommended: disable in the Phase 1 data migration); R11–R12 added; Affects (docs), §4, §5, §7, §9 and §10 updated. No other decision changed. | Claude Code, at the request of Moiz |
| 2026-10-08 | Revision 15: Q10 decided as D21 — option A approved: existing earn rules with test mode on are disabled in the Phase 1 data migration, deployed with the engine change; references in §3.10, §4, §5, §6, §7, §8 and §10 point to D21. No other decision changed. | Claude Code, at the request of Moiz |
| 2026-10-08 | Revision 16: Phase 0 done — `RefundPathsCr1006E2ETests` confirms the double reversal (when the refund carries a `contact_key`) and H1; baseline recorded (build clean, 95/95 and 251/251); §7.1 added; R10, Q9 and next steps updated. No decision changed. | Claude Code, at the request of Moiz |
| 2026-10-08 | Revision 17: D22 (Q9: no Reversal rule on `order.refunded`), D23 (Q11: `cash.spent` reports insufficient balance), D24 (Phase 1 split into 1a / 1b) approved; §2.9 (which code applies each rule) added; §7.2 Part 1a SCR text and pre-deploy queries; **Part 1a implemented and verified** (§7.3); R13–R14; open questions closed. | Claude Code, at the request of Moiz |
| 2026-10-08 | Revision 18: **Part 1b implemented and verified** — test mode removed (D20) with the D21 migration, once per customer per rule for signup / KYC (D12) with the drawer reason, caps follow On breach on both kinds (D13); §7.4 SCR text and pre-deploy queries, §7.5 implementation record. | Claude Code, at the request of Moiz |
| 2026-10-08 | Revision 19: D21 migration applied to the developer's local database (no rule had test mode on — no data changed); the guardrails git note dropped at the developer's request. | Claude Code, at the request of Moiz |
| 2026-10-08 | Revision 20: **Phase 2 implemented and verified** — fields per trigger on new rules (`RuleFieldCatalog`, create validation, `applicableFields` in metadata, portal reads it), en/tr Configuration section, terms titles, amount hint; §7.6 SCR text, §7.7 record. | Claude Code, at the request of Moiz |
| 2026-10-08 | Revision 21: **Phase 4 implemented and verified** — program default rounding, one rounding step at the wallet's decimals in the rule's or program's direction, portal setting; §3.8 pre-check answered (nothing rejected excess decimals); §7.8 SCR text and pre-deploy queries, §7.9 record. | Claude Code, at the request of Moiz |
| 2026-10-08 | Revision 22: `ProgramDefaultRoundingCr1006` applied to the developer's local database (7 programs, all `down` — no payout change). | Claude Code, at the request of Moiz |
| 2026-10-08 | Revision 23: **Phase 5 implemented and verified** — expiry override applied: `ledger_entries.expires_at` stored on earn / transfer_in postings (rule override or wallet days), expiry and warnings by each lot's date, soonest-expiring first (E1–E3); §7.10 (SCR text, pre-deploy queries) and §7.11 (record) added; every phase of this CR is built. | Claude Code, at the request of Moiz |
| 2026-10-08 | Revision 24: `LedgerExpiresAtCr1006` applied to the developer's local database (39 ledger rows, none backfilled; no active or pending rule has `expiryOverrideDays` — no expiry change). | Claude Code, at the request of Moiz |
| 2026-10-08 | Revision 25: Phase 5 self-review — no expiry date on cash even with an override, one rule read per release, client-side minimum on the portal field; re-verified (§7.11). | Claude Code, at the request of Moiz |
| 2026-10-08 | Revision 26: R15 found and fixed — a redelivered event no longer reserves budgets, counts limits or pays another exclusive rule again, and a redelivered partial Reversal completes; D18 now also tested per event type through the worker; item 14, R15 and §7.12 added. | Claude Code, at the request of Moiz |
