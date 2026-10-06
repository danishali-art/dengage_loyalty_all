# Scope Change Impact Analysis: Rule-driven redeem and transfer, program live checks, overview tiles, optional conditions

**Status:** **Approved** 2026-10-05 (revision 7) by the Product Owner and the Architect, after the document was shared (confirmed by the developer, Moiz). **O14 skipped:** background jobs stay unchanged and are out of scope for this CR. **P0 and P1 implemented and verified 2026-10-05** (§12).
**Date:** 2026-10-05
**Author:** Claude Code, at the request of Moiz (acting as BA/PO)
**Client context:** Tiqmo (fintech).
**Type:** Mixed.
**File name:** kept from revision 1 so existing links still work. "Dynamic reward purchase" is no longer part of this CR (S1).

| # | Change | Type |
|---|---|---|
| 1 | `points.redeem` / `points.transfer` always run on a **rule definition**. The rule's calculation section gets the same fields as the wallet's redemption/transfer settings. The wallet settings only pre-fill new rules. | **Scope change.** It adopts and changes the deferred **P5** of CR 2026-09-30 (§3.9 steps 2–3). |
| 2 | ~~Dynamic reward purchase~~ | **Dropped** (S1). Reward purchase stays exactly as today. |
| 3 | Programs › Overview: add the Streak campaigns and Card buckets tiles | **UI only.** Not a scope change; the pages and routes already exist. |
| 4 | Rule form: the Conditions section is marked optional but blocks saving | **Bug fix.** Not a scope change. |
| 5 | **Program live check** (status Active **and** publication Published) on the event handlers that don't check it today: redeem, transfer, reward purchase of every type, `cash.added`, `cash.spent` | **Scope change** (revision 7). It changes the SOW §3 known limitation. Background jobs unchanged (O14 skipped). |

**Affects (docs):**
- `docs/SCOPE_BASELINE.md`: the Rules row (redeem and transfer rules now take effect, and are required).
- `docs/SOW.md`: the burn-events section, and the known limitation in §3 (program status not checked by some handlers and jobs), which item 5 narrows.
- `LoyaltySaaSApi.md`: rule calculation fields, the new failure reasons, and the new outbound events `loyalty.cash.add_failed` / `loyalty.cash.spend_failed` (item 5). **No inbound payload or endpoint changes.**
- `scripts/loyalty_schema_reference.md`: **no change** (the only migration is a data step, R-O11).
- `.claude/rules/01-workflow-and-debugging.md` and `backend-consumer.md`: the pitfall rows about redeem/transfer and about inactive programs ("Redeem / transfer / expiry still happens for an inactive program").

## 0. Decisions in force (revision 5)

### 0.1 Senior direction (2026-10-05, from the developer's discussion with the senior)

| # | Decision |
|---|---|
| S1 | **Dynamic reward purchase is dropped.** No "Use dynamic rule" checkbox, no new rule type on `reward.purchase`, no `points_amount` on the purchase event, no "Dynamic" badge. Reward purchase keeps using the reward's fixed points price and points wallet. |
| S2 | **Redeem and transfer always use a rule.** At event time the handler never reads the wallet's `redemption` / `transfer` settings. There is no fallback. |
| S3 | **The rule's calculation section gets the same fields as the wallet settings.** Redeem: cash per point, minimum redemption, redeem into (CASH wallet). Transfer: daily transfer limit. See §3.1, "Rule fields". |
| S4 | **The wallet settings are a value provider only.** When an admin creates a redeem or transfer rule, the form copies the selected wallet's settings into the rule's calculation fields **once**. The admin can change them before saving. From then on the rule owns its values: later changes to the wallet settings do **not** change existing rules, and events read only the rule. |
| S5 | **The wallet settings stay on the wallet screen as optional defaults.** They are relabelled as "defaults for new rules", and the help text says events don't use them. |
| S6 | **The rule is picked automatically by wallet.** No `rule_id` in the payload. The handler takes the active rules for the trigger whose target wallet equals the event's `source_account_type_id`, checks conditions and limits, and the highest priority wins. Payloads stay exactly as today. |
| S7 | **No usable rule → the event fails safely.** The system publishes `loyalty.points.redeem_failed` / `loyalty.points.transfer_failed` with reason `no_rule` or `rule_limit_reached`. Nothing is debited, and the inbox entry is marked `processed` (not DLQ), so the client is told. |
| S8 | **No automatic migration of existing settings into rules.** After deploy, admins create the rules. Until a usable rule exists, redeem and transfer fail with `no_rule`. Go-live must be coordinated (§7). |
| S9 | ~~Existing redeem/transfer rules are left as they are.~~ **Replaced by R-O11 (revision 6):** existing redeem/transfer rules are disabled by a migration. |
| R-O11 | **Existing redeem and transfer rules are deactivated by a migration** (decided 2026-10-05, resolves O11). Every RedemptionRule and TransferRule whose status is `active` or `pending_approval` is set to `disabled`, the only inactive rule status. Rows already `disabled` or `deleted` are left alone. Admins then re-enable (after completing the new fields) or recreate rules deliberately. Details in §3.1, "Existing rules". |
| R-O13 | **Accepted:** redeem and transfer only work in live programs (decided 2026-10-05, resolves O13). It becomes an explicit check in item 5 (P-1), with its own failure reason. |
| R-O12 | **One winner, no fall-through** (decided 2026-10-05, resolves O12). The highest-priority usable rule is picked. If its own minimum redemption or daily transfer limit check fails, the event fails (`below_minimum` / `daily_limit_exceeded`). Lower-priority rules are not tried. |

### 0.1b Program live checks (item 5, decided 2026-10-05)

"Live" means program status **Active** and publication status **Published**, the same check earn rules and cashback purchases already use (`RewardFulfilmentService.IsProgramLiveAsync`). The program is the one that owns the wallet or reward named in the event.

| # | Area | Decision |
|---|---|---|
| P-1 | `points.redeem`, `points.transfer` | Checked **first**, before the rule is picked. Not live → `loyalty.points.redeem_failed` / `transfer_failed` with reason **`program_not_live`**. Nothing posted, inbox `processed`. Same pattern as the existing cashback check. |
| P-2 | `reward.purchase`, **every reward type** | The existing cashback-only check is extended to all reward types. (Implementation note: a tier upgrade can only be acquired through a streak today, so cashback is the only purchasable type — the extension guards future purchasable types; see §12.3.) Not live → `loyalty.reward.purchase_failed`, reason `program_not_live`, before any debit. |
| P-3 | `cash.added`, `cash.spent` | Not live → **nothing is posted**, and a **new** outbound event `loyalty.cash.add_failed` / `loyalty.cash.spend_failed` with reason `program_not_live` tells the client. Inbox `processed`. Rules on these events stay gated as today. |
| P-4 | `order.refunded` | **Stays ungated.** A refund always reverses what the order earned, even in a paused program, so customers never keep points for refunded orders. |
| P-5 | Background jobs | **Unchanged** (O14 skipped). |

### 0.2 Earlier decisions that still apply

| # | Decision |
|---|---|
| D2 | **A redeem rule that pays cash needs second-admin approval** (CR-04), even though its target is a POINTS wallet. The creator can't approve it. Editing the cash per point or the cash wallet creates a new version, which goes back to Pending approval. Confirmed again on 2026-10-05: redeem is blocked until a second admin approves. |
| R-O1 | **Redeem rate unit: `rate`, cash per point** (cash = points × rate), the same as the wallet's `redemption.rate`. The legacy `ratio` is deprecated for RedemptionRule. |
| R-O2 | **Transfer rule: daily limit only** (`maxPerDay`). `fee` and `ratio` stay out of scope and are hidden in the portal until a fee-engine CR. |
| R-D2 | **Minimum redeem is optional on the rule** (blank = no minimum). Confirmed again on 2026-10-05. The pre-fill copies the wallet's minimum when the wallet has one. |
| D8 | **Item 3: same shape as the existing tiles.** Icon, title, one-line description and a link. No counts, no API change. |

### 0.3 Superseded decisions (kept for the record)

| # | Was | Superseded by |
|---|---|---|
| D1 | Rule first, then fall back to the wallet settings | S2 |
| R-D1 | Optional `rule_id` in the payload chooses rule mode or wallet mode | S2, S6 |
| R-O3 | Disable existing redeem/transfer rules in a migration | S9, then reinstated as R-O11 |
| S9 | Leave existing redeem/transfer rules as they are | R-O11 |
| R-O7 | Wallet-settings mode stays ungated by program status | S2, then R-O13 and P-1 |
| D3–D7, R-O4, R-O5, R-O6 | Dynamic reward purchase design | S1 |
| O8–O10 | Questions about `rule_id` and dynamic purchase | Moot after S1/S6 |

## 1. Why

- **Item 1:** Redeem and transfer limits are set once per POINTS wallet: `redemption {rate, min_points, target_account_type_id}` and `transfer.daily_limit`. These cannot vary by segment, time window or campaign. Rule definitions already offer all of that (conditions, priority, active window, limits, budget), but today RedemptionRule and TransferRule **save without ever acting** (CR 2026-09-30 §2.8, pending issue 12).
- **Item 3:** Programs › Overview shows tiles for Account types, Tiers, Rules, Rewards and History. Streak campaigns and Card buckets are reachable only from the tab bar.
- **Item 4:** Admins cannot save a rule without conditions, even though the section says it is optional.

## 2. Current state (as-built, verified)

### 2.1 Item 1: redeem and transfer
- [PointsRedeemHandler.cs](../../src/dEngage.Loyalty.Consumer/Handlers/PointsRedeemHandler.cs):
  - It reads the POINTS wallet's `config.redemption` and throws `redemption_not_configured` (DLQ) if it is missing.
  - It computes `cash = round(points × rate, 2)`. The 2 decimal places are hard-coded, which is wrong for 3-decimal currencies (KWD, BHD, OMR).
  - `below_minimum` and `insufficient_points` publish `loyalty.points.redeem_failed`; success publishes `loyalty.points.redeemed`.
- [PointsTransferHandler.cs](../../src/dEngage.Loyalty.Consumer/Handlers/PointsTransferHandler.cs):
  - It reads `config.transfer.daily_limit` and sums today's `TransferOut` ledger rows under the sender's row lock.
  - `daily_limit_exceeded` and `insufficient_points` publish `loyalty.points.transfer_failed`.
- Neither handler checks program status (SOW §3 known limitation).
- **Program-status checks today (verified for O13).** "Live" means status Active **and** publication Published.

  | Area | Checks that the program is live today? | After this CR |
  |---|---|---|
  | Earn rules and streak campaigns for incoming events ([CampaignEvaluationService.cs:23-31](../../src/dEngage.Loyalty.Consumer/CampaignEvaluationService.cs#L23-L31)) | **Yes.** Only live programs are evaluated. | Unchanged |
  | Birthday bonus job ([BirthdayBonusJob.cs:56-57](../../src/dEngage.Loyalty.RuleEngine/Processing/BirthdayBonusJob.cs#L56-L57)) | **Yes.** | Unchanged |
  | Rule sync into the Consumer cache ([RuleSyncService.cs:140](../../src/dEngage.Loyalty.Consumer/RuleSyncService.cs#L140)) | **Yes.** Only live programs are synced. The rule cache itself loads Active rules for whichever program it is asked for; the callers above decide which programs to ask for. | Unchanged |
  | Reward purchase, **cashback** ([RewardPurchaseHandler.cs:71-77](../../src/dEngage.Loyalty.Consumer/Handlers/RewardPurchaseHandler.cs#L71-L77), [RewardFulfilmentService.cs:18-21](../../src/dEngage.Loyalty.RuleEngine/Processing/RewardFulfilmentService.cs#L18-L21)) | **Yes.** Refused with `program_not_live` before any debit. | Unchanged |
  | Reward purchase, **tier upgrade** | **No.** | **Yes** (P-2) |
  | `points.redeem`, `points.transfer` handlers | **No.** | **Yes** (P-1) |
  | `cash.added` / `cash.spent` direct credit and debit | **No.** (Rules on those events are gated, as above.) | **Yes** (P-3) |
  | `order.refunded` reversal | **No.** | **No**, on purpose (P-4) |
  | Ledger and tier jobs: points expiration, expiring-soon detector, tier downgrade, streak maintenance, delayed-posting promotion | **No.** | Unchanged (O14 skipped) |

- Wallet settings ([AccountTypeConfigValidators.cs:49-72](../../src/dEngage.Loyalty.Api/AccountTypes/AccountTypeConfigValidators.cs#L49-L72), [account-type-form.dialog.ts](../../web/src/app/features/programs/account-types/account-type-form.dialog.ts)):
  - both are already optional ("Allow redemption to another wallet", "Allow transfer");
  - when enabled, redemption requires `rate`, `min_points` and `target_account_type_id`, and transfer requires `daily_limit` > 0.
- [RuleTypeCatalog.cs](../../src/dEngage.Loyalty.RuleEngine/Metadata/RuleTypeCatalog.cs): `points.redeem` allows only RedemptionRule, `points.transfer` only TransferRule.
- [RuleCalculation.cs](../../src/dEngage.Loyalty.RuleEngine/Models/RuleCalculation.cs) has `rate`, `ratio` ("points per currency unit"), `minRedeem`, `fee` and `maxPerDay`. There is no cash wallet field.
- **Why the rules never act today:** the RedemptionRule handler and `TransferRuleProcessor` read the event's `amount` field, but these events carry `points_amount`, so they compute 0 and are skipped.
- **Latent risk:** RedemptionRule still goes through the generic engine (`RuleTypes.UsesWinnerSelectorPipeline`). If a client sent `amount` as well, an active redeem or transfer rule would debit **in addition to** the handler.
- [RulesAppService.cs](../../src/dEngage.Loyalty.Api/Rules/RulesAppService.cs) (`:99-123`, `:187-196`) requires CASH approval only when the rule's **target** is CASH.

### 2.2 Item 3: overview tiles
- [program-overview.page.ts:245-255](../../web/src/app/features/programs/program-overview.page.ts#L245-L255): the `cards` array holds Account types, Tiers, Rules, Rewards and History.
- `programs.routes.ts` already has the `streak-campaigns` and `card-buckets` routes.

### 2.3 Item 4: conditions (root cause)
- [rule-form.page.ts:746](../../web/src/app/features/programs/rules/rule-form.page.ts#L746) initialises `conditions` with `emptyTree()`. This is **one group containing one blank condition**, not "no conditions".
- `submit()` runs `validateConditionTree` on it. `validateConditionLeaf` rejects the blank leaf with "field is required", so Save is blocked.
- The backend ([GroupedConditionDsl.cs](../../src/dEngage.Loyalty.RuleEngine/GroupedConditionDsl.cs) `Validate`) accepts `conditions: null` but would also reject a blank field path. So the only real "no conditions" value is `null`, and the portal never sends it.
- This affects **every trigger**.

## 3. Proposed design

### 3.1 Item 1: redeem and transfer always run on rules

**Rule fields (S3).** These are the same fields as the wallet settings, in the rule's calculation section.

| Rule | Field label | Calculation key | Required | Copied from the wallet setting (S4) | Notes |
|---|---|---|---|---|---|
| Redeem (RedemptionRule) | Cash per point | `rate` | Yes, > 0 | `redemption.rate` | R-O1. It reuses the existing `rate` key, so no new key. |
| Redeem | Minimum redemption | `minRedeem` | No (R-D2) | `redemption.min_points` | Blank = no minimum. |
| Redeem | Redeem into (CASH wallet) | **`cashAccountTypeId`** (new) | Yes | `redemption.target_account_type_id` | A CASH wallet in the same tenant and program. |
| Transfer (TransferRule) | Daily transfer limit | `maxPerDay` | Yes, > 0 | `transfer.daily_limit` | R-O2. Same daily sum under the row lock as today. |
| Transfer | Ratio, Fee | `ratio`, `fee` | — | — | Hidden; ignored if present on old rules (R-O2). |

- The rule's **Target account** is the POINTS wallet being debited (existing field). Only POINTS wallets are offered.
- Everything else a rule already has applies: conditions, priority, active from/to, limits, budget and cooldown.
- The calculation is stored as JSON on the rule, so **no migration** is needed.

**Value provider: pre-fill once (S4, S5).** This is a portal behaviour only; the backend has no link between a rule and the wallet settings.
- On **create**, when the trigger is `points.redeem` or `points.transfer` and the admin picks the target POINTS wallet, the form copies that wallet's settings into the empty calculation fields:
  - redeem: `rate` ← `redemption.rate`, `minRedeem` ← `redemption.min_points`, `cashAccountTypeId` ← `redemption.target_account_type_id`;
  - transfer: `maxPerDay` ← `transfer.daily_limit`.
- If the admin picks a different wallet before saving, the fields are refilled from the new wallet only if the admin hasn't changed them. A field the admin edited is never overwritten.
- If the wallet has no settings, the fields stay empty and the admin fills them.
- On **edit**, nothing is pre-filled: the rule keeps its own values.
- A hint under the fields reads: "Pre-filled from the wallet's defaults. The rule keeps these values; changing the wallet later won't change this rule."
- **Wallet screen:** the redemption and transfer sections stay as they are (optional, same validation), relabelled "Defaults for new redeem rules" / "Defaults for new transfer rules". The help text says: "Redeem and transfer events use rules, not these values." Values that already exist are kept.

**Payloads: unchanged (S6).** No new field on any event or endpoint.

| Event | Event fields | Dedicated endpoint body |
|---|---|---|
| `points.redeem` | `contact_key`, `points_amount`, `source_account_type_id` | `contactKey`, `pointsAmount`, `sourceAccountTypeId` |
| `points.transfer` | `contact_key`, `target_contact_key`, `points_amount`, `source_account_type_id` | `contactKey`, `targetContactKey`, `amount`, `accountTypeId` |

**How the rule is picked (S6).** A new RuleEngine seam, **`Processing/IBurnRuleResolver`**, runs inside the handler. It only picks a rule and never posts.
1. **Candidates:** rules for this program and trigger with status **Active**, inside their active window, of the right type (RedemptionRule / TransferRule), and whose **target wallet equals `source_account_type_id`**. They come from the rule cache, which only holds Active rules of published and active programs.
2. They are tried in **priority order, highest first** (rule id breaks ties). A candidate is skipped if its **conditions** fail, or if a **limit, budget or cooldown** is used up.
3. **The first candidate that passes wins.** The stackable/exclusive flag plays no part: exactly one rule applies per event.
4. If none wins, the result is the failure reason (S7):
   - **`rule_limit_reached`** if at least one candidate passed its conditions but was blocked by a limit;
   - **`no_rule`** otherwise (no rule for that wallet, or no rule whose conditions pass).

**Program check first (P-1).** Before picking a rule, the handler checks that the program owning `source_account_type_id` is live. If not, the event fails with `program_not_live`.

**Handler flow after the pick.** `PointsRedeemHandler` / `PointsTransferHandler` remain the **only** code that posts for these events.
- They keep their transaction, row locks, redelivery guard, idempotency keys (`{eventId}:points_redeemed`, `{eventId}:transfer_out`) and failure events.
- Redeem: checks `points_amount` ≥ `minRedeem` (if set) → `below_minimum`; checks the balance → `insufficient_points`. Then it debits the points and credits `points_amount × rate` to the rule's cash wallet.
- Transfer: checks today's total + `points_amount` ≤ `maxPerDay` → `daily_limit_exceeded`; checks the balance → `insufficient_points`. Then it moves `points_amount`.
- **No fall-through (R-O12):** if the winning rule's minimum or daily-limit check fails, the event fails. Lower-priority rules are not tried.
- **Every failure** is an explicit check under the row lock (never a caught exception). The failure event goes through the outbox, and the transaction commits, so the inbox entry becomes `processed`. Nothing is posted and no budget is used.
- If the rule has a budget, the handler reserves it inside its own transaction. After commit it calls `ILimitCounterSync` and writes a `RuleFireAudit` row, in the same order as the engine.
- Ledger entries record `ruleId + ruleVersion`, and `loyalty.points.redeemed` / `loyalty.points.transferred` carry `rule_id`.
- **The handlers never read the wallet's `redemption` / `transfer` settings** (S2). The `redemption_not_configured` / `transfer_not_configured` errors are replaced by `no_rule`.
- An unknown or non-POINTS `source_account_type_id` stays a configuration error (DLQ), as today.

**New failure reasons** on `loyalty.points.redeem_failed` / `loyalty.points.transfer_failed`: `program_not_live`, `no_rule`, `rule_limit_reached`. The existing reasons stay: `below_minimum`, `daily_limit_exceeded`, `insufficient_points`.

**The generic engine never posts these rules.** RedemptionRule joins TransferRule in the `UsesWinnerSelectorPipeline` bypass, and the burn dispatch is removed from `RuleEngine.ProcessEventAsync`. **This closes the double-debit risk** in §2.1. Streak campaigns on these events keep running as today.

**Cash rounding:** the cash credit is rounded **down to the CASH wallet's decimal places**, in one place. Today it is fixed at 2. This changes amounts for 3-decimal currencies only.

**Burn amount for conditions:** `EvaluationEvent` reads `points_amount` for burn events (falling back to `amount`), so conditions such as `points_amount >= 500` work.

**Rate unit (R-O1).**
- The API requires `rate` on a RedemptionRule and rejects `ratio` when a rule is created or a new version is saved ([RulesValidators.cs:69-71](../../src/dEngage.Loyalty.Api/Rules/RulesValidators.cs#L69-L71)).
- The portal labels the field "Cash per point".
- Legacy `ratio` values are never converted automatically, because converting a reciprocal could silently change payouts.

**Transfer validation (R-O2).** The current "`ratio` is required for TransferRule" check ([RulesValidators.cs:73-75](../../src/dEngage.Loyalty.Api/Rules/RulesValidators.cs#L73-L75)) is replaced by "`maxPerDay` is required and > 0".

**Approval (D2).**
- The CASH check in `RulesAppService` changes from "target is CASH" to "**target is CASH, or `calculation.cashAccountTypeId` is set**". So every new redeem rule starts as **Pending approval**.
- Approval stays on `PATCH .../rules/{ruleId}/approve`, and the creator can never approve.
- Editing `rate` or `cashAccountTypeId` creates a new version (`IRuleVersioningService`), which goes back to Pending approval.
- Transfer rules need no approval (no cash).

**Existing rules (R-O11).**
- The P1 migration includes a **data step** (no schema change). Every RedemptionRule and TransferRule whose status is `active` or `pending_approval` is set to **`disabled`**. Rows already `disabled` or `deleted` are left alone. Other rule types are not touched.
- It is a soft status change: no row is deleted, so any `ruleId + ruleVersion` reference stays valid. These rules have never posted anything (§2.1), so no balance changes.
- The migration also bumps `updated_at`, so `RuleSyncService`'s delta sync picks up the change without a Consumer restart.
- A **pre-deploy query** lists the affected rules per tenant (tenant, program, rule name, type, version, status before) for the PO.
- After deploy, an admin re-enables a rule only after completing the new fields (Cash per point and Redeem into for redeem, Daily transfer limit for transfer). That save creates a new version, and a redeem rule goes to Pending approval (D2). Or the admin creates a new rule with the wallet pre-fill.
- The migration is generated with `dotnet ef migrations add` and carries the data step as SQL. Applied migrations are not edited.

### 3.2 Item 3: overview tiles
- Add two entries to the `cards` array in `program-overview.page.ts`, in the same shape as the others: an icon, title and description i18n keys, and a link to `streak-campaigns` / `card-buckets`.
- New keys go in `en.json` and `tr.json`.

### 3.3 Item 4: optional conditions (bug fix)
- On submit, a condition tree whose leaves are **all blank** (no field chosen) is sent as `conditions: null` and skips validation. This uses the existing `hasConditions(tree)` helper in `condition-tree-dsl.ts`.
- A tree with at least one non-blank leaf is validated exactly as today. Partially filled conditions still block saving with the current messages.
- When editing a rule saved with `null`, the form shows the empty tree as today.
- **No backend change.** `GroupedConditionDsl.Validate(null)` already accepts it. The DSL parity contract is untouched.
- Applies to every trigger.

### 3.4 Item 5: program live checks

One shared check, `IsProgramLiveAsync` (it already exists on `IRewardFulfilmentService`), is used by every gated handler, so there is one definition of "live". It could move to a small shared service if the Architect prefers; that is an implementation detail for the diff review.

| Handler | Program checked | Where in the flow | Failure |
|---|---|---|---|
| `PointsRedeemHandler`, `PointsTransferHandler` | The program of `source_account_type_id` | Before the rule pick and before any lock-sensitive work | `redeem_failed` / `transfer_failed`, reason `program_not_live` |
| `RewardPurchaseHandler` | The reward's program | Where the cashback check is today, now for every reward type, before the debit | `purchase_failed`, reason `program_not_live` (existing reason) |
| `CashAddedHandler`, `CashSpentHandler` | The program of `account_type_id` | Before the posting | **New** `loyalty.cash.add_failed` / `loyalty.cash.spend_failed`, reason `program_not_live` |

How every failure is reported:
- an explicit check, never a caught exception;
- the failure event goes through the **outbox**, with dedupe key `{kind}_failed:{eventId}`;
- the transaction commits, so the inbox entry becomes `processed`;
- nothing is posted.

**New outbound events (`Shared/Events/OutboundEventTypes.cs`).** These carry the same fields as the inbound event, plus `reason`.

| Event | Payload (snake_case) |
|---|---|
| `loyalty.cash.add_failed` | `contact_key`, `amount`, `account_type_id`, `reason` |
| `loyalty.cash.spend_failed` | `contact_key`, `amount`, `account_type_id`, `reason` |

**Unchanged on purpose:**
- `order.refunded` (P-4);
- earn rules, streaks and the birthday bonus, which are already gated;
- background jobs (O14).

**Redelivery:** each handler's existing redelivery guard runs before the program check. An event that was already posted before a program was paused is never reported as failed on redelivery.

## 4. End-to-end impact (blast radius)

| Area | Item | Files (planned) | Change |
|---|---|---|---|
| RuleEngine | 1 | new `Processing/IBurnRuleResolver.cs` + implementation; `RuleEngine.cs`; `Models/EvaluationEvent.cs`; `Models/RuleCalculation.cs` (`cashAccountTypeId`) | Pick the rule by wallet; drop the burn dispatch; `points_amount` for conditions |
| Shared | 1, 5 | `Constants/RuleTypes.cs` (bypass set); `Events/OutboundEventTypes.cs` | Bypass; new failure reasons; `CashAddFailed` / `CashSpendFailed` |
| Consumer | 1, 5 | `Handlers/PointsRedeemHandler.cs`, `PointsTransferHandler.cs`, `Program.cs` (DI) | Rule-only path; no reading of wallet settings; rounding; budget/audit; program check (P-1) |
| Consumer | 5 | `Handlers/RewardPurchaseHandler.cs`, `CashAddedHandler.cs`, `CashSpentHandler.cs` | Program check for every reward type (P-2); program check + failure events for cash (P-3) |
| API: Rules | 1 | `Rules/RulesValidators.cs`, `Rules/RulesAppService.cs` | RedemptionRule: `rate` and `cashAccountTypeId` required, `ratio` rejected, `minRedeem` ≥ 0 optional. TransferRule: `maxPerDay` required, `ratio` no longer required. Approval extension for the cash wallet. |
| API: Events | — | none | Payloads unchanged |
| API: Account types | 1 | none | Settings and their validation unchanged; only their meaning changes |
| Schema | 1 | **New migration** (P1) `DisableLegacyBurnRulesCrNN` | **Data step only** (R-O11): RedemptionRule/TransferRule rows that are `active` or `pending_approval` → `disabled`, `updated_at` bumped. No schema change, so `loyalty_schema_reference.md` is unchanged. |
| Ops | 1 | Pre-deploy SQL queries (in the release notes, not in `scripts/*.sql`) | (a) wallets with redemption/transfer settings; (b) the rules R-O11 will disable |
| Portal: rules | 1, 4 | `programs/rules/rule-form.page.ts`, `rule.model.ts` | Redeem: Cash per point, Minimum redemption, **Redeem into** CASH wallet picker, approval state. Transfer: Daily transfer limit, Ratio/Fee hidden. **Pre-fill from the wallet on create** (S4). Target limited to POINTS. Null-conditions fix. |
| Portal: account types | 1 | `programs/account-types/account-type-form.dialog.ts` | Relabel the sections as defaults for new rules, plus help text (S5) |
| Portal: overview | 3 | `programs/program-overview.page.ts` | Two tiles |
| Portal: event simulator | 1, 5 | `features/events/event-simulator.page.ts` | Help texts: redeem/transfer need an active rule for the wallet; redeem, transfer, purchase and cash events need a live program |
| Portal: i18n | all | `public/i18n/en.json`, `tr.json` | New and changed strings |
| Tests | all | `Engine.Tests` (resolver, bypass, rounding); `IntegrationTests` (redeem/transfer paths, approval, validators); portal specs (`rule-form` pre-fill, condition null) | See §7 |
| Docs | all | `SCOPE_BASELINE.md`, `SOW.md`, `LoyaltySaaSApi.md`, `01-workflow-and-debugging.md`, `backend-consumer.md`, `backend-rule-engine.md` | |

**Breaking behaviour changes (call out in the release notes):**
- **Redeem and transfer stop working until rules exist** (S8). Every event fails with `no_rule` until an admin creates a rule for that wallet, and redeem also needs a second admin's approval (D2).
- **The wallet's redemption/transfer settings no longer affect events.** Changing them changes only what new rules are pre-filled with.
- **Program status now matters** (item 5): redeem, transfer, every reward purchase, `cash.added` and `cash.spent` are refused with `program_not_live` while the program is draft or inactive. Integrators must handle the new failure events, including the two new cash events.
- **Validation tightens** on saving a redeem rule (`rate` and cash wallet required, `ratio` rejected) or a transfer rule (`maxPerDay` required). Stored rules are not changed.

**API changes (additive):** `cashAccountTypeId` in the rule calculation; failure reasons `program_not_live`, `no_rule` and `rule_limit_reached`; outbound events `loyalty.cash.add_failed` and `loyalty.cash.spend_failed`.

## 5. Contracts check (`00-project-guardrails.md` §2)

| Contract | Status |
|---|---|
| Tenancy | Kept. The resolver uses tenant-scoped cached rules. The cash wallet is validated as same tenant and program. |
| Money | Kept. Decimal on the server, strings on the wire; cash rounded once, down, to the wallet's decimals. |
| Ledger | Kept. Append-only, same deterministic idempotency keys, one posting path per event. |
| Events | Kept. No payload change; failure events through the outbox; inbox dedupe unchanged. |
| Rule versioning | Kept. Postings reference `ruleId + ruleVersion`; editing the cash leg creates a new version. |
| CASH rules | **Strengthened** (D2): a redeem rule paying cash needs a second admin. |
| DSL parity | Kept. Item 4 changes only when the portal sends `null`. |

## 6. Risks

| # | Risk | Mitigation |
|---|---|---|
| R1 | **Redeem and transfer outage at go-live** (S8): every event fails with `no_rule` until rules exist, and redeem rules also wait for approval (D2). | Coordinated go-live (§7): rules are created and approved before integrations depend on them. A pre-deploy query lists every wallet with redemption/transfer settings, so admins know which rules to create. Clients receive `redeem_failed` / `transfer_failed` with `no_rule`, not silence. |
| R2 | Old redeem/transfer rules with an incomplete calculation would become candidates. | **Resolved by R-O11:** the migration disables them; the pre-deploy query lists them for the PO. |
| R3 | Double posting if the generic engine still dispatches a burn rule. | RedemptionRule joins the bypass set; Engine tests assert that no generic posting happens for redeem or transfer rules. |
| R4 | The rounding change alters cash for 3-decimal wallets. | Release notes; it is a correction. |
| R5 | **Admins expect wallet settings to still apply** (S4, S5). | Relabelled sections and help text on the wallet screen; hint on the rule form. |
| R6 | Budget/limit counters double-count (the known engine-twice pitfall). | Handlers use the resolver only, never `ICampaignEvaluationService`. |
| R7 | **Cash movements are refused while a program is paused** (P-3). The integrator's own system may already have moved the customer's real money. | The client is told through `cash.add_failed` / `cash.spend_failed`, and must reconcile or resend after the program is live again. Called out in the release notes and `LoyaltySaaSApi.md`. |
| R8 | **Pausing a program now stops redeem, transfer and purchases at once** (P-1, P-2). | Intended. The program page should warn the admin before deactivating; this warning is UI-only and can be added in P1. |

## 7. Phasing, go-live and verification

| Phase | Content | Balance impact | Verification |
|---|---|---|---|
| **P0** | Item 4 (bug fix) + item 3 (UI) | None | Portal lint/test/build; manual save of a rule with no conditions for each trigger |
| **P1** | Item 1: resolver, bypass, rule-only handlers, cash wallet + approval, rounding, rule-form pre-fill, wallet-screen relabel, **legacy-rule migration (R-O11)**. Item 5: program live checks and the new cash failure events. | Yes | See the test list below |

**P1 tests:**
- A rule wins, and the wallet settings are not read: change the wallet settings, and the payout doesn't change.
- No rule for the wallet → `no_rule`, with no ledger row, inbox `processed` and an outbox event.
- A rule exists but its conditions fail → `no_rule`.
- A rule passes its conditions but its limit is reached → `rule_limit_reached`.
- A pending redeem rule is not a candidate.
- Priority order; a rule for another wallet is never used.
- The winner's minimum or daily limit fails → the event fails, and the next rule is **not** tried (R-O12).
- Migration: active and pending redeem/transfer rules → `disabled`; other rule types and statuses untouched; `updated_at` bumped.
- Item 5, per gated handler and per state (draft, inactive): `program_not_live`, nothing posted, outbox event written, inbox `processed`.
- Item 5: redelivery of an already-posted event after the program was paused → no failure event, no second posting.
- Item 5: `order.refunded` still reverses in an inactive program.
- Item 5: a tier-upgrade purchase in an inactive program → `program_not_live`, points kept.
- `below_minimum`, `daily_limit_exceeded` and `insufficient_points` still work.
- Redelivery posts only once.
- Rounding for 2 and 3 decimal places.
- Approval flow, including the creator not being able to approve and a cash-leg edit going back to pending.
- Validator cases.
- The generic engine posts nothing for redeem or transfer rules.
- Portal specs: pre-fill on create, no pre-fill on edit, an edited field is never overwritten.

**Go-live checklist (S8):**
1. Before deploy: run the queries listing (a) wallets with redemption/transfer settings and (b) the redeem/transfer rules the migration will disable (R-O11). Share both with the PO.
2. Deploy P1 in a window agreed with the client. From this moment, redeem and transfer need rules.
3. Admins create the rules (the form pre-fills them from the wallet settings), or complete and re-enable the disabled ones.
4. A second admin approves the redeem rules.
5. Smoke test one redeem and one transfer per tenant through the simulator.

P0 can ship independently, before the review of P1 concludes.

## 8. Open questions for the review

| # | Question | Recommendation |
|---|---|---|
| O11 | Old redeem/transfer rules with an incomplete calculation: skip them, or fail the event? | **Resolved → R-O11:** disable them all in a migration. |
| O12 | When the winning rule's minimum or daily limit fails: fail, or try the next rule? | **Resolved → R-O12:** fail; no fall-through. |
| O13 | Program status for redeem and transfer: accept that they stop working in draft or inactive programs? | **Resolved → R-O13:** accepted, as an explicit check (P-1). |
| O14 | Background jobs: gate them on program status? | **Skipped** (2026-10-05): the jobs stay unchanged; out of scope for this CR. |

## 9. Explicitly out of scope
- Dynamic reward purchase, and any change to reward purchase (S1).
- Automatically creating rules from the wallet settings (S8).
- Keeping rules in sync with the wallet settings (S4).
- Removing the wallet's redemption/transfer settings (S5).
- A `rule_id` field in the event payload (S6).
- Transfer `ratio` and `fee` (R-O2).
- Automatic conversion of legacy `ratio` values to `rate` (R-O1).
- Counts on the overview tiles (D8).
- Program-status gating of `order.refunded` (P-4).
- Any change to background jobs (O14 skipped), including pause-aware jobs.

## 10. Next step
1. ~~Approval~~ Done 2026-10-05.
2. On approval, P0 and P1 are implemented on a feature branch, with the docs in §4 updated in the same change.
3. An implementation record and file list are appended here per phase.

## 11. Document record (review record per `00-project-guardrails.md` §6)
| File | Action | Reason |
|---|---|---|
| `docs/scope-changes/2026-10-05-burn-rules-dynamic-reward-purchase.md` | Created | This impact analysis (draft for Architectural review). |
| same | Changed (revision 2) | Reviewer's decisions R-O3 and R-O4. |
| same | Changed (revision 3) | R-O1, R-O2, R-O5, R-O6, R-O7; open questions resolved. |
| same | Changed (revision 4) | R-D1 (no fallback, optional `rule_id`), R-D2, O8–O10. |
| same | Changed (revision 5) | Senior direction S1–S9: dynamic reward purchase dropped; redeem and transfer always use a rule picked by wallet and priority; wallet settings only pre-fill new rules; no payload change; no migration; existing rules left active; open questions O11–O13. Document rewritten; superseded decisions kept in §0.3. |
| same | Changed (revision 6) | R-O11 (migration disables existing redeem/transfer rules; replaces S9), R-O12 (no fall-through); verified program-status behaviour documented in §2.1 for O13. |
| same | Changed (revision 7) | Item 5: program live checks P-1 to P-4 (redeem, transfer, all reward purchases, cash.added/spent with new failure events; refund ungated); R-O13 accepted; O14 (background jobs) with the PO recommendation; risks R7/R8; tests; blast radius. |
| same | Changed (approval) | Approved by PO + Architect; O14 skipped (jobs unchanged). |

## 12. Implementation record — P0 + P1 (2026-10-05)

### 12.1 What was built

**P0 (items 3 and 4):**
- **Overview tiles:** Programs › Overview links to Streak Campaigns and Card Buckets. All tile labels moved to i18n keys (en/tr), as the frontend rules require for touched strings.
- **Optional conditions:** an untouched rule form (only blank conditions) saves `conditions: null`. A partly filled tree still blocks saving with the existing messages. This is the shared helper `conditionsForSave` in `condition-tree-dsl.ts`. No backend change.

**P1 (items 1 and 5):**
- **Rule picking:** `IBurnRuleResolver` / `BurnRuleResolver` (RuleEngine) picks the highest-priority Active redeem/transfer rule targeting the debited wallet. It checks conditions, active window, min event amount, cooldown, max customers and rule budget. It also reserves the budget inside the handler's transaction (all budgets checked before any is recorded) and writes the `RuleFireAudit` row.
- **Handlers:** `PointsRedeemHandler` / `PointsTransferHandler` never read the wallet settings. The order is: redelivery guard → program live check → rule pick → the winner's minimum / daily limit (no fall-through) → balance → budget → post.
  - Failure reasons: `program_not_live`, `no_rule`, `rule_limit_reached`, plus the existing ones.
  - `rule_id` is on the outcome events.
  - Redeem cash is rounded down to the CASH wallet's `decimals`.
- **Engine bypass:** RedemptionRule joins the `UsesWinnerSelectorPipeline` bypass, and the TransferRule dispatch is removed from `RuleEngine`. The generic engine never posts redeem/transfer rules.
- **Burn amount:** `EvaluationEvent` reads `points_amount` for burn events, so conditions and min event amount see it.
- **API:** `BurnRuleCalculationRules` is the one list used on create, on edit and before switching a rule on.
  - Redeem: `rate` > 0 and a CASH `cashAccountTypeId` of the same program are required; `ratio` is rejected; `minRedeem` ≥ 0 is optional.
  - Transfer: `maxPerDay` > 0 is required.
  - Approval: a redeem rule always starts `pending_approval`. A change to `rate` or `cashAccountTypeId` clears the approval. Re-enabling a never-approved one goes to `pending_approval`. Retargeting a pending one can't auto-activate it. An incomplete rule can't be switched on.
- **Program live check (item 5):** `ProgramLiveness.IsProgramLiveAsync` is the one definition of live (Active + Published), and `RewardFulfilmentService` now delegates to it.
  - Applied to redeem, transfer, reward purchase (every type), `cash.added` and `cash.spent`.
  - New outbound events `loyalty.cash.add_failed` / `loyalty.cash.spend_failed`.
  - The cash handlers gained an explicit redelivery guard, so a load posted before a pause is never reported as failed.
- **Migration:** `20261005180559_DisableLegacyBurnRulesCr1005` is a data step only. Active and pending redeem/transfer rules are set to `disabled`, `updated_at` is bumped, and published programs are marked as having unpublished changes. `Down` is a no-op. The model snapshot is unchanged.
- **Portal:**
  - Rule form: redeem asks for "Cash per point", "Minimum redemption" and "Redeem into" (CASH picker), with an approval hint. Transfer asks for "Daily transfer limit" only; Ratio and Fee are removed. A new rule is pre-filled once from the chosen wallet's defaults, never over a field the admin has edited (`burn-rule-defaults.ts`).
  - Account-type form: the redemption and transfer sections are relabelled as defaults for new rules (strings moved to i18n).
  - Rules list: RedemptionRule is grouped with Transfer/Reversal, outside winner selection.
  - Event simulator: help texts updated, and `reward_id` listed as optional.

### 12.2 Verification

| Check | Result |
|---|---|
| `dotnet build dEngage.Loyalty.sln` (scratch output, the running Consumer locks `bin`) | 0 errors |
| `dotnet test src/dEngage.Loyalty.Engine.Tests` | **95/95 passed** (incl. 12 new `BurnRulePipelineTests`) |
| `dotnet test src/dEngage.Loyalty.IntegrationTests` (Docker, Testcontainers) | **249/249 passed** (full suite). New: `BurnRuleCalculationApiTests`, `CashEventsProgramLiveE2ETests`; rewritten: `PointsRedeemOutcomeE2ETests`, `PointsTransferLimitE2ETests` |
| Portal `npm test` (vitest) | **145/145 passed** (new: `condition-tree-dsl.spec.ts`, `burn-rule-defaults.spec.ts`; updated: `rule-grouping.spec.ts`) |
| Portal `npx ng lint` | Only the 3 errors that existed before, in `confirm.service.ts` and `dashboard.page.ts`; none in changed files |
| Portal `npx ng build --configuration development` | Succeeds |
| Migration against a real DB | Applied by every Testcontainers E2E run (`MigrateAsync`), and **applied to the dev DB (`loyalty_dev`) on 2026-10-05** with `dotnet ef database update`. The pre-deploy query listed one rule (fintech › FinPay Rewards › "point redemption", RedemptionRule v2, active); it is now `disabled`, and FinPay Rewards shows unpublished changes. |
| Manual run in the portal / simulator | **Not done** — to do with the go-live checklist (§7) |

**Tests changed because the approved behaviour changed:**
- The redeem/transfer E2E tests asserted the old wallet-settings behaviour (e.g. "a wallet without a transfer setting throws `transfer_not_configured`"). They now assert the rule-driven behaviour, with the wallet settings deliberately different from the rules to prove they're never read.
- `BurnTriggerRuleTypesApiTests.Burn()` sent `{ratio: 1}` for every burn rule, which the approved CR makes invalid. It now sends a valid calculation per type, so those tests still only exercise the trigger allowlist. No assertion was loosened.

### 12.3 Notes for the Architect's diff review

- **`TransferRuleProcessor` / `ITransferRuleProcessor` deleted** (2026-10-05, confirmed by the developer). They were unwired from `RuleEngine` and DI by this CR; the solution builds without them.
- **Only the debit carries `rule_id`** (redeem: the points debit; transfer: the sender's debit). The other entry records `rule_id` / `rule_version` in its metadata. Rule budgets, cooldown and max customers count ledger rows by rule, so tagging both entries would count one burn twice. `RuleFireAudit` holds `ruleId + ruleVersion` for the posting.
- **Limits on burn rules** are the same as the engine's for non-earn rules: min event amount, cooldown, max customers, and rule budget (total / per period). The earn-only caps (per customer total / day / period) don't apply.
- **A budget that can't take the whole amount refuses the burn** (`rule_limit_reached`), whatever `on_breach` says. A redeem or transfer is never partly applied.
- **A redeem with `points_amount` ≤ 0 is now a hard failure** (`invalid_amount` → DLQ), like transfer. The API already rejected it; only a hand-built event gets there.
- **P-2 has no observable effect today.** A tier upgrade can only be acquired through a streak, and the purchase handler only loads points-purchase rewards. So cashback, already checked, is the only purchasable type. No test can buy a tier upgrade.
- **`LedgerPoster.ReserveBudgetAsync` (engine path, unchanged)** records the total budget before checking the period budget. If the period budget then refuses, the total's usage stays recorded for a rule that was skipped. This was pre-existing and is outside this CR; flagged for a follow-up. The new resolver checks all budgets first.

### 12.4 Pre-deploy queries (go-live checklist §7, step 1)

(a) Wallets whose settings admins will turn into rules:
```sql
SELECT t.slug AS tenant, p.name AS program, a.name AS wallet,
       a.config -> 'redemption' AS redemption, a.config -> 'transfer' AS transfer
FROM account_types a
JOIN programs p ON p.id = a.program_id
JOIN tenants t ON t.id = a.tenant_id
WHERE a.type = 'POINTS' AND (a.config ? 'redemption' OR a.config ? 'transfer')
ORDER BY 1, 2, 3;
```
(b) Rules the migration will disable (R-O11):
```sql
SELECT t.slug AS tenant, p.name AS program, r.name AS rule, r.type, r.current_version, r.status AS status_before
FROM rules r
JOIN programs p ON p.id = r.program_id
JOIN tenants t ON t.id = r.tenant_id
WHERE r.status IN ('active', 'pending_approval') AND r.type IN ('RedemptionRule', 'TransferRule')
ORDER BY 1, 2, 3;
```

### 12.5 Files (P0 + P1)

| File | Action | Reason |
|---|---|---|
| `src/dEngage.Loyalty.Shared/Constants/RuleTypes.cs` | Changed | RedemptionRule bypasses the winner-selector pipeline |
| `src/dEngage.Loyalty.Shared/Events/OutboundEventTypes.cs` | Changed | `CashAddFailed` / `CashSpendFailed` |
| `src/dEngage.Loyalty.Shared/Events/OutcomeReasons.cs` | Created | `no_rule`, `rule_limit_reached`, `program_not_live` |
| `src/dEngage.Loyalty.RuleEngine/Models/RuleCalculation.cs` | Changed | `cashAccountTypeId` |
| `src/dEngage.Loyalty.RuleEngine/Models/EvaluationEvent.cs` | Changed | Burn events read `points_amount` |
| `src/dEngage.Loyalty.RuleEngine/RuleEngine.cs` | Changed | TransferRule dispatch removed |
| `src/dEngage.Loyalty.RuleEngine/Processing/TransferRuleProcessor.cs`, `ITransferRuleProcessor.cs` | Deleted | Unused after this CR (developer confirmed) |
| `src/dEngage.Loyalty.RuleEngine/Processing/IBurnRuleResolver.cs` | Created | Rule-pick seam for the redeem/transfer handlers |
| `src/dEngage.Loyalty.RuleEngine/Processing/BurnRuleResolver.cs` | Created | Pick, budget reservation, audit |
| `src/dEngage.Loyalty.RuleEngine/Processing/ProgramLiveness.cs` | Created | One "program is live" check |
| `src/dEngage.Loyalty.RuleEngine/Processing/RewardFulfilmentService.cs` | Changed | Delegates to `ProgramLiveness` |
| `src/dEngage.Loyalty.Consumer/Handlers/PointsRedeemHandler.cs` | Changed | Rule-driven redeem, program check, rounding |
| `src/dEngage.Loyalty.Consumer/Handlers/PointsTransferHandler.cs` | Changed | Rule-driven transfer, program check |
| `src/dEngage.Loyalty.Consumer/Handlers/RewardPurchaseHandler.cs` | Changed | Program check for every reward type |
| `src/dEngage.Loyalty.Consumer/Handlers/CashAddedHandler.cs` | Changed | Program check, failure event, redelivery guard |
| `src/dEngage.Loyalty.Consumer/Handlers/CashSpentHandler.cs` | Changed | Program check, failure event, redelivery guard |
| `src/dEngage.Loyalty.Consumer/Program.cs` | Changed | DI: `IBurnRuleResolver`; transfer processor unregistered |
| `src/dEngage.Loyalty.Api/Rules/BurnRuleCalculationRules.cs` | Created | Redeem/transfer calculation rules + approval predicate |
| `src/dEngage.Loyalty.Api/Rules/RulesValidators.cs` | Changed | Uses the shared rules; old `ratio` requirements removed |
| `src/dEngage.Loyalty.Api/Rules/RulesAppService.cs` | Changed | Cash wallet check, approval on create/edit/retarget/re-enable, edit validation |
| `src/dEngage.Loyalty.Schema/Migrations/20261005180559_DisableLegacyBurnRulesCr1005.cs` (+ `.Designer.cs`) | Created | Data migration (R-O11), generated with `dotnet ef` |
| `src/dEngage.Loyalty.Engine.Tests/BurnRulePipelineTests.cs` | Created | Bypass and `points_amount` tests |
| `src/dEngage.Loyalty.IntegrationTests/Fixtures/BurnRuleFixture.cs` | Created | Real resolver over an in-memory rule list |
| `src/dEngage.Loyalty.IntegrationTests/Fixtures/RuleEngineTestHarness.cs` | Changed | Transfer processor removed; exposes `BurnRules` |
| `src/dEngage.Loyalty.IntegrationTests/E2E/PointsRedeemOutcomeE2ETests.cs` | Changed | Rewritten for rule-driven redeem |
| `src/dEngage.Loyalty.IntegrationTests/E2E/PointsTransferLimitE2ETests.cs` | Changed | Rewritten for rule-driven transfer |
| `src/dEngage.Loyalty.IntegrationTests/E2E/CashEventsProgramLiveE2ETests.cs` | Created | Item 5 for cash events |
| `src/dEngage.Loyalty.IntegrationTests/Api/BurnRuleCalculationApiTests.cs` | Created | Validation, tenancy and approval through the API |
| `src/dEngage.Loyalty.IntegrationTests/Api/BurnTriggerRuleTypesApiTests.cs` | Changed | Fixture sends a valid calculation per type |
| `web/src/app/shared/forms/condition-tree-dsl.ts` | Changed | `conditionsForSave` (item 4) |
| `web/src/app/shared/forms/condition-tree-dsl.spec.ts` | Created | Item 4 tests |
| `web/src/app/features/programs/program-overview.page.ts` | Changed | Two tiles; tile strings to i18n (item 3) |
| `web/src/app/features/programs/rules/rule-form.page.ts` | Changed | Item 4 save; redeem/transfer fields; wallet pre-fill |
| `web/src/app/features/programs/rules/rule.model.ts` | Changed | `cashAccountTypeId`; field notes |
| `web/src/app/features/programs/rules/burn-rule-defaults.ts` | Created | Wallet → rule pre-fill |
| `web/src/app/features/programs/rules/burn-rule-defaults.spec.ts` | Created | Pre-fill tests |
| `web/src/app/features/programs/rules/rule-grouping.ts` | Changed | RedemptionRule outside winner selection |
| `web/src/app/features/programs/rules/rule-grouping.spec.ts` | Changed | Covers RedemptionRule |
| `web/src/app/features/programs/account-types/account-type-form.dialog.ts` | Changed | Sections relabelled as defaults; strings to i18n |
| `web/src/app/features/programs/account-types/account-type.model.ts` | Changed | Field notes (defaults only) |
| `web/src/app/features/events/event-simulator.page.ts` | Changed | `reward_id` optional for `reward.purchase` |
| `web/public/i18n/en.json`, `web/public/i18n/tr.json` | Changed | New and updated strings |
| `LoyaltySaaSApi.md` | Changed | Rule calculation, approval, outcomes, cash failure events |
| `docs/SCOPE_BASELINE.md` | Changed | AccountTypes, Rules, gating, Consumer, portal rows; changelog |
| `docs/SOW.md` | Changed | §2.2, §2.4, §3; version 1.7 |
| `scripts/loyalty_schema_reference.md` | Changed | Latest migration; rule calculation; wallet settings are defaults |
| `.claude/rules/01-workflow-and-debugging.md` | Changed | Pitfall rows for redeem/transfer and program gating |
| `.claude/rules/backend-consumer.md`, `.claude/rules/backend-rule-engine.md` | Changed | Burn rules no longer go through the engine |
| `docs/scope-changes/2026-10-05-burn-rules-dynamic-reward-purchase.md` | Changed | Approval, this record |
