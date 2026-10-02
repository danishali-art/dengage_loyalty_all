# Scope Change Impact Analysis: Reward acquisition, cashback / tier-upgrade fulfilment, event simulator

**Status:** **Approved** 2026-10-01 by Product Owner + Architect (confirmed by the developer, Moiz). **P1–P4 implemented and verified** (§11). P5 stays deferred (A7). See the approval record below.
**Date:** 2026-09-30
**Author:** Claude Code, at the request of Moiz
**Source:** Change log items 1–7 (Add Reward popup, reward/acquisition taxonomy, configuration field, rule binding, event simulator), plus item 8 (burn events and burn rules), added 2026-09-30 after the burn-event review.
**Affects:**
- `docs/SCOPE_BASELINE.md`: the Programs (new program slug, A5), Rewards, Rules, StreakCampaigns, Tiers and Events backend rows, the RuleEngine tier-evaluation and rule-matching rows, and the `programs/rewards`, `programs/rules`, `programs/streak-campaigns` and `events` frontend rows.
- `docs/SOW.md` §2.2 (Programs, Rules, Streak Campaigns, Rewards), §2.3 (Event Simulator), §2.4 (redemption outcome events, `reward_id`) and §3 (burn-rule and burn-side money-control limitations). Drafted as **proposed** text in SOW v1.4 (draft) on 2026-10-01; it becomes contractual only on approval.
- `scripts/loyalty_schema_reference.md`, which must be regenerated after the migration.
- `LoyaltySaaSApi.md`, which is **still missing**. The reward DTO and reward-event changes below can't be recorded until it is regenerated.

This document is step 2 of the scope-change process in `CLAUDE.md`. It analyses the change log against the `docs/SCOPE_BASELINE.md` baseline and the as-built code. The developer has already made the decisions in the "Pre-agreed direction" table below. Everything in §5 not marked "Decided" is still open.

## Pre-agreed direction (developer, 2026-09-30, pending PO + Architect sign-off)

| # | Decision |
|---|---|
| A1 | **Full removal** of `stamp_completion` (acquisition) and of `points_bonus`, `discount`, `free_product` and `gift_card` (reward types). Existing rows that use a removed value are **deactivated by migration**. The rows are kept so that `RewardLog` history stays intact. |
| A2 | **The engine fulfils both remaining reward types.** Cashback posts a real CASH ledger credit. Tier upgrade really changes the customer's tier. |
| A3 | **Cashback uses a currency dropdown plus a CASH wallet picker.** The currency comes from the `SupportedCurrencies` list, exactly like the CASH account type. The wallet picker is filtered to CASH wallets in that currency. Both values are stored. |
| A4 | **Cashback rewards need a second admin's approval**, mirroring CASH rules (CR-04). The creator can never approve their own reward. |
| A5 | (Resolves O6.) **Reward names carry the program slug as a prefix** (for example `fintech_cashback_50`). Programs get a new **slug**: unique per tenant, locked after creation, no `_`. See §3.10. |
| A6 | (Resolves O15.) **A RedemptionRule's cash leg is entered as cash per point** (the existing `rate` key), the same meaning as the POINTS account type's redemption rate. |
| A7 | (Resolves O11.) **Item 8: P1 now, P5 later.** This CR ships only the burn-event bug fixes. Making burn rules post (§3.9 steps 2–3) is documented here but goes to a separate decision. |

## Approval record

Approved 2026-10-01. A1–A7 are approved as written. The remaining open decisions were resolved with their **recommended defaults**:

| # | Resolution |
|---|---|
| O1 | Stamp completions keep publishing `loyalty.reward.earned`, using the stamp rule's reward name, with `reward_type` null. No breaking event change. |
| O2 | Existing wallet-less `cashback` rewards are deactivated. |
| O3 | `ux_reward_definitions_active_stamp` is kept. |
| O4 | A tier upgrade with no `duration_days` follows the normal tier lifecycle. The UI label is "Until next tier review". |
| O5 | A reward-driven upgrade does not reset `tier_period_start`. |
| O7 | No budget or limit caps on rewards in v1. |
| O8 | Not in this CR; it stays a separate CR. |
| O9 | Scheduled event types are filtered at the API types endpoint, with the portal filter as a fallback. |
| O10 | The cashback credit is gated on the program being published and active. |
| O12–O14 | Not applicable until P5 is taken up. |

## 0. Change-log items mapped to the baseline

| Item | Summary | Baseline row | Type |
|---|---|---|---|
| 1 | Add Reward popup: Acquisition field first, then Reward type | `programs/rewards` (frontend) | UX change. Only meaningful together with item 3. |
| 2 | Acquisition limited to *Purchase with points* and *Streak completion* | Rewards (backend) + `programs/rewards` | **Scope change (reduction)** |
| 3 | Reward type limited to Cashback and Tier upgrade. Cashback works with either acquisition; Tier upgrade only with Streak completion. | Rewards + `programs/rewards` | **Scope change (reduction + new rule)** |
| 4 | Cashback currency uses the same dropdown as the CASH account type | Rewards + `programs/rewards` | Scope change (validation tightened) |
| 5 | Check whether the configuration field (`TypeConfig`) is used → it is **not** used at fulfilment (§2.3). The agreed fix is engine fulfilment (A2). | Rewards, StreakCampaigns, Tiers, RuleEngine | **Scope change (new capability)** |
| 6 | Rule binding with reward definitions, and how `reward.purchase` uses them | Events (`reward.purchase`), StreakCampaigns, Programs | Hardening (§3.7), plus a **scope change**: program slug and prefixed reward names (A5, §3.10) |
| 7 | Event simulator: hide the scheduled types and add per-event field help | `events` (frontend) | **Not a scope change.** UX/bug fix, portal only. |
| 8 | Burn events: each trigger allows only its own rule type (`points.transfer` → TransferRule, `points.redeem` → RedemptionRule). Burn rules actually take effect. `reward.purchase` stays rule-free. | Rules (backend), RuleEngine, Events, `programs/rules` (frontend) | Mixed. **Bug fixes (P1):** the trigger → rule-type mapping, the portal fix and the redeem outcome events. **Scope change (P5):** making burn rules post, including the cash leg and the rounding fix. See §3.9. |

## 1. Why this change is being proposed

- The reward catalog offers six reward types and three acquisitions. However, the configured details of a reward (amount, currency, target tier) are **never acted on**. Every completion path only publishes a `loyalty.reward.earned` notification that carries `reward_name` and `reward_type`. An admin who configures "50 SAR cashback" or "upgrade to Gold" gets neither.
- The business wants a narrower catalog that it can trust: **cashback** and **tier upgrade**, earned by **buying with points** or by **completing a streak**.
- Admins can't use the event simulator for the scheduled event types, and it gives no field guidance.

## 2. Current state (as-built)

### 2.1 Reward model

- **Entity:** `RewardDefinition` in `src/dEngage.Loyalty.Schema/Entities/RewardDefinition.cs`.
  - `Acquisition` (string) and `RewardType` (string), both immutable after creation.
  - Nullable `StampAccountTypeId`, `PointsPrice` and `PointsAccountTypeId`.
  - `TypeConfig` (jsonb) and `IsActive`.
- **No approval workflow:** the entity has no status, `CreatedBy` or `ApprovedBy` fields.
- **Acquisition whitelist:** in `RewardsValidators.cs`. The values are `points_purchase`, `stamp_completion` and `streak_completion`.
- **RewardType registry:** in `RewardTypeRegistry.cs`. It holds six types and validates the *shape* of `TypeConfig` only:
  - `cashback` requires `amount` (a decimal string) and `currency`, which can be any non-empty string. Currency isn't checked against `SupportedCurrencies` and no wallet is linked.
  - `tier_upgrade` requires `target_tier_id`, and only checks that it is a well-formed Guid. **It doesn't check that the tier exists, belongs to this program or belongs to this tenant.**
- **No compatibility rule** between Acquisition and RewardType. Any pair is accepted.
- **Pre-taxonomy rows:** rewards created before 2026-09-17 were backfilled with `reward_type = 'points_bonus'` (column default in migration `RewardTypeTaxonomy`). **All of them fall under A1.**

### 2.2 Portal form (`web/src/app/features/programs/rewards/reward-form.dialog.ts`)

- **Field order:** Reward type comes first, then Acquisition. The select options are hardcoded, and so are the English labels (no i18n keys).
- **Cashback currency:** a free-text `<input maxlength="3">`.
- **Reference implementation for item 4:** `account-type-form.dialog.ts:201-220` uses `SUPPORTED_CURRENCIES` and `DEFAULT_CURRENCY` from `account-type.model.ts`, which mirror `SupportedCurrencies.cs`, with i18n keys.

### 2.3 How rewards are fulfilled today (item 5)

| Path | Code | What actually happens | `TypeConfig` used? |
|---|---|---|---|
| Points purchase | `Consumer/Handlers/RewardPurchaseHandler.cs` | Finds an active `points_purchase` reward **by `reward_name`**. Debits `PointsPrice` from the POINTS wallet (idempotent). Writes a `RewardLog` and publishes `loyalty.reward.earned` with `reward_name` and `reward_type`. | **No** |
| Streak completion | `RuleEngine/Campaigns/Streak/StreakCampaignModule.cs:175-209` | Looks up `reward.reward_definition_id` from the streak config and publishes `loyalty.reward.earned`. **No `RewardLog`** is written, only a `StreakLog`. If the reward is missing or inactive, the streak still completes and only a warning is logged. | **No** |
| Stamp completion | `RuleEngine/Processing/StampCompletionHandler.cs:60-104` | Resets the stamps. Finds the active `stamp_completion` reward for that stamp account. If one exists, the `RewardLog` is marked `notified` and `loyalty.reward.earned` is published. Otherwise the `RewardLog` is marked `pending`. | **No** |

**Conclusion for item 5:** `TypeConfig` is validated, stored, returned by the API and included in the publish snapshot. **Nothing reads it at runtime.**
- **Tier upgrade via streak:** doesn't work. `customer_accounts.tier_id` isn't changed, no `TierUpgradeLog` row is written and no `loyalty.tier.changed` event is sent.
- **Cashback via streak or purchase:** doesn't work. No CASH ledger entry is posted. The amount and currency aren't even on the outbound event, so a downstream system can't fulfil the reward either.

### 2.4 How cashback works on the rule side, for comparison

- **Rules already pay real cashback.** A `FixedBonusRule` or `SpendRule` can target a CASH account type (`RuleTypeCatalog.cs`). It posts a real ledger credit with rule limits, budget, cooldown and a deterministic idempotency key.
- **CASH approval (CR-04):** a rule that targets CASH is created `PendingApproval` and needs a different admin to approve it (`RulesAppService.cs:98-123, 137-150, 187-196`).
- **Streak fixed bonus has no approval gate.** A streak campaign's `fixed_bonus` posts to `TargetAccountTypeId`. `StreakCampaignsAppService.cs:77-82` creates the campaign **`Active` with no CASH check**, so a streak that targets a CASH wallet pays money without second-admin approval. **This looks like an existing CR-04 gap** (§5, O8). This change doesn't fix it silently.

### 2.5 Tier engine (relevant to tier-upgrade fulfilment)

- **Points-driven.**
  - `TierEvaluationService` only **upgrades** during event processing. It picks the highest tier whose `min_points` is at or below the qualifying points of the program's tier-qualifying POINTS account.
  - The nightly `TierDowngradeJob` starts grace (`tier_expires_at`) and then downgrades from points at period end.
- **No manual or override concept.** A forced tier would be reverted by the nightly job at the next period end. `TierUpgradeConfig.duration_days` exists in the form and the model but has **no mechanism** behind it.
- **Where the tier lives:** on the customer's tier-qualifying `CustomerAccount` (`TierId`, `TierPeriodStart`, `TierExpiresAt`).

### 2.6 `reward.purchase` and reward binding (item 6)

- **Event contract:** `contact_key`, `reward_name` and an optional `channel` (`EventsDtos.cs:12`, `EventTypes.cs:124`). The event is a **Burn** handler and isn't evaluated by rules.
- **The name alone is ambiguous.** Reward names are unique per **(tenant, program, name)** (`uq_reward_definitions_tenant_program_name`). The handler matches on (tenant, name) with `FirstOrDefault`. If two programs in the same tenant use the same reward name, the purchase resolves to **an arbitrary one** and may debit the other program's wallet. With cashback paying real money, this becomes a correctness risk (§5, O6).
- **No program-status check.** The handler doesn't check whether the program is published and active (a known limitation, `docs/SOW.md` §3).
- **Rule ↔ reward bindings that exist today:**
  - Streak campaign → reward: a soft jsonb reference (`reward.reward_definition_id`). A foreign key was **deliberately declined** on 2026-09-17. The streak form lists all rewards, of any acquisition.
  - Stamp rule → reward: implicit, through the stamp account type.
  - Earn rules (for example `order.created`) have **no way** to grant a reward definition.

### 2.7 Event simulator (item 7)

The simulator is `web/src/app/features/events/event-simulator.page.ts`. The claims in the change log were verified against the handlers:

| Claim | Verified |
|---|---|
| `birthdaybonus` and `points.expired` are always rejected | Yes. `EventsAppService.cs:42-47` (`IsExternallyPublishable`, CR-01) |
| The "dedicated form" on the Customers/Programs screens doesn't exist | Yes. The helper text at `:71-75` refers to it |
| `cash.added`, `cash.spent`, `points.redeem` and `points.transfer` need string amounts | Yes. They use `GetString()` and then `decimal.Parse` |
| **Not in the change log:** `order.refunded` also needs strings | Yes, and the change log misses it. `refund_ratio`, `amount` and `original_amount` use `GetString()` (`OrderRefundedHandler.cs:18-23`). The proposed help text must say so. |
| `order.created` accepts a number or a string for `amount` | Yes. `EvaluationEvent.cs:22-26`. The "must be strings" line is a safe convention here, not a hard requirement. |

### 2.8 Burn events and burn rules (item 8)

**How every event is processed.** `EventConsumerWorker.cs:147-157` runs two steps:
1. The event's own **handler** runs.
2. Then the **generic rule engine** (`ICampaignEvaluationService`) runs, for every event type except `order.created`.

**One-line status of each burn event today:**

| Event | Status |
|---|---|
| `points.redeem` | A hardcoded handler converts points to cash using the POINTS account type's `config.redemption`. A RedemptionRule saves but never fires, because the engine reads `amount` and the event sends `points_amount`. |
| `points.transfer` | A hardcoded handler moves the points using the account type's `config.transfer.daily_limit` and ignores TransferRules (`ITransferRuleProcessor.cs:6`: "NOT wired into the live points.transfer path"). The trigger also wrongly allows RedemptionRule. |
| `reward.purchase` | Driven by the reward definition (§2.6), and no rule type fits it. The portal hides that with an empty dropdown and a hidden SpendRule fallback, so CASH/POINTS targets appear and Save then fails with 400. |

**Details:**

| | `points.redeem` (`PointsRedeemHandler.cs`) | `points.transfer` (`PointsTransferHandler.cs`) | `reward.purchase` (`RewardPurchaseHandler.cs`) |
|---|---|---|---|
| Configured by | POINTS account type `config.redemption` (`rate`, `min_points`, `target_account_type_id`) | POINTS account type `config.transfer.daily_limit` | Reward definition (`PointsPrice`, `PointsAccountTypeId`) |
| Ledger effect | Points debit + cash credit (`points × rate`, always rounded to 2 dp) in one transaction | Sender debit + receiver credit in one transaction | Points debit |
| Insufficient balance | **Throws** → inbox `failed` → DLQ. No failure event. | `loyalty.points.transfer_failed`; inbox `processed` | `loyalty.reward.purchase_failed`; inbox `processed` |
| Rule types allowed at the trigger (`RuleTypeCatalog.IsCompatible`) | RedemptionRule (correct) | **RedemptionRule + TransferRule** (wrong: RedemptionRule shouldn't be allowed) | **None**. The portal falls back to a hidden SpendRule (`rule-form.page.ts:926-930`) and the API rejects it on save (`RulesValidators.cs:40-44`). |
| Do those rules post? | No. `EvaluationEvent.Amount` reads only `amount` (`EvaluationEvent.cs:21-26`), so RedemptionRule computes 0. | No. `TransferRuleProcessor` returns early when the amount is 0 (`:40`). | n/a |

**Why the trigger → rule-type mapping is loose.** `RuleTypeCatalog.IsCompatible` matches on the event category and the kinds of field present. RedemptionRule only needs "a Number field", and `points.transfer` has one (`points_amount`). No rule type is tied to a specific event.

**Latent double-debit risk.** Suppose a caller sends both `points_amount` and `amount`:
1. The handler posts, with key `{eventId}:points_redeemed` or `:transfer_out`.
2. The generic engine then posts **again** for any matching burn rule, with key `{eventId}:{ruleId}:…`.

The two keys differ, so nothing stops the second posting.

**RedemptionRule is debit-only by design** (`RedemptionRuleHandler.cs:7-12`). It has no cash leg, so on its own it can't replace `points.redeem`.

## 3. Proposed direction

### 3.1 Items 2 and 3: Acquisition × RewardType taxonomy and compatibility

- **Acquisition:** `points_purchase` or `streak_completion`.
- **RewardType:** `cashback` or `tier_upgrade`.
- **Compatibility matrix.** It is enforced in two places: server-side in `CreateRewardRequestValidator`, and client-side by filtering the Reward type options on the chosen Acquisition.

  | | cashback | tier_upgrade |
  |---|---|---|
  | points_purchase | Allowed | Rejected (`tier_upgrade_requires_streak_completion`) |
  | streak_completion | Allowed | Allowed |

- **Registry:**
  - `RewardTypeRegistry` keeps only `cashback` and `tier_upgrade` as **creatable** types.
  - The four removed types move to a *retired* set, so that existing (deactivated) rows still read, list and display correctly.
  - Re-activating a reward with a retired type or acquisition (`PATCH /{rewardId}/active`) is rejected with `reward_type_retired`.
  - Editing `TypeConfig` on a retired row is rejected with the same code. Today it would fail anyway with "Unknown RewardType".
- **Constants and frozen fields:**
  - `RewardAcquisition.StampCompletion` and the retired `RewardType` constants stay, for reading history only.
  - The `stamp_account_type_id` column and the DTO field stay, per the frozen-fields rule. New rewards must send `null`.
- **Migration** (`RewardCatalogCashbackTierCrNN`):
  - `UPDATE reward_definitions SET is_active = false WHERE acquisition = 'stamp_completion' OR reward_type NOT IN ('cashback','tier_upgrade')`.
  - Existing `cashback` rows have no `cash_account_type_id` and can't be fulfilled. The proposal is to deactivate them too, subject to O2.
  - The filtered unique index `ux_reward_definitions_active_stamp` becomes dead. Keep it or drop it (O3).
  - This migration changes reward data only. It never touches ledger rows (ledger append-only rule).
  - Deactivating a reward is a change to a program's nested config. The migration also sets `has_unpublished_changes = true` on affected **published** programs, matching what `IProgramChangeTracker.MarkChangedAsync` does for an API edit (`backend-api.md`, "Mutations with side effects").
- **Stamp cards after removal:**
  - `StampCompletionHandler` no longer finds a definition, so every stamp completion writes a `pending` `RewardLog` named after the stamp rule's `config.RewardType`.
  - **Unless O1 decides otherwise, `loyalty.reward.earned` with `source = stamp_completion` stops being published.** This is a **breaking outbound-event change** for any tenant integration that listens for stamp rewards.
  - Stamp rules themselves (earning stamps, the reset, the `RewardLog`) are unchanged.

### 3.2 Item 1: popup field order and dependent options

- **Order:** Name / Display name → **Acquisition** → **Reward type**, with the Reward type options filtered by Acquisition.
- **Default:** `points_purchase` + `cashback`.
- **Changing Acquisition to `points_purchase`** while `tier_upgrade` is selected resets the reward type to `cashback`.
- **Edit mode:** both fields stay disabled (immutable).
- **Retired rows** open read-only with a "retired type" notice.
- **Model types** (`frontend.md`: string unions must match the backend registries exactly):
  - The `RewardType` / `RewardAcquisition` unions in `reward.model.ts` keep **all** values, because the API still returns retired rows.
  - A separate `CREATABLE_REWARD_TYPES` / `CREATABLE_ACQUISITIONS` constant drives the form. It mirrors the registry's creatable set.
- **i18n:** every string in the touched dialog and list page moves to keys in **both** `en.json` and `tr.json` (`rewards.acquisition.*`, `rewards.type.*`, ...), following `account-type-form.dialog.ts` (`frontend.md` i18n rule: touched strings are migrated).

### 3.3 Item 4: cashback configuration shape

- **New `CashbackConfig`:**

  ```json
  { "amount": "25.00", "currency": "SAR", "cash_account_type_id": "<guid>" }
  ```

- **Server validation** (`RewardTypeRegistry` for the shape; `RewardsAppService` for lookups that need the DB):
  - `currency` is one of `SupportedCurrencies.All`.
  - `cash_account_type_id` is a `CASH` account type in **the same tenant and program**.
  - Its `config.currency` equals `currency`.
  - The number of decimal places in `amount` is at most the wallet's `decimals` (for example 3 for KWD).
- **Portal:**
  - A currency `<select>` over `SUPPORTED_CURRENCIES`, defaulting to `DEFAULT_CURRENCY`, the same as the account type form.
  - Below it, a `SearchableSelect` of the program's CASH wallets filtered to that currency.
  - The amount stays a `DecimalString`.
- **The currency is locked after the first approval**, mirroring the CASH wallet's currency lock. That prevents an approved payout from being reinterpreted.
  - The wallet can still be switched to another CASH wallet **in the same currency**.
  - Switching the wallet (or changing the amount) sends the reward back to `pending_approval` (§3.4).

### 3.4 Item 4 + A4: second-admin approval for cashback rewards (CR-04 parity)

- **Schema:** `reward_definitions` gets `status`, `created_by` and `approved_by`, mirroring `Rule.CreatedBy` and `Rule.ApprovedBy`.
  - Following the safe-migration order in `backend-schema-shared.md`:
    1. add `status` as nullable,
    2. backfill existing rows to `active`,
    3. then make it `NOT NULL`.
  - Values are constants in a new `Shared/Constants/RewardStatus.cs` (`active` | `pending_approval`), stored as strings.
- **Creation:** a cashback reward is created `pending_approval`. A tier-upgrade reward is created `active`.
- **Approval endpoint:** `PATCH .../rewards/{rewardId}/approve`, the same verb and shape as the existing `PATCH .../rules/{ruleId}/approve` (`RulesModule.cs:80`).
  - The approver comes from `RequireAuthenticated().SubjectId`.
  - The approver must not be `created_by`, otherwise the call fails with `self_approval_forbidden`. The logic is copied from `RulesAppService.ApproveAsync`.
  - Approval calls `IProgramChangeTracker.MarkChangedAsync`, like any other nested-config change.
- **Edits that send an approved cashback back to `pending_approval`:** a change to `amount`, `cash_account_type_id` or `points_price`. This mirrors the CR-04 recheck on rule retargeting.
- **Consumers treat `pending_approval` as not purchasable:** both lookups (purchase and streak) require `IsActive && status = 'active'`.
- **Portal:**
  - A status badge on the list.
  - An **Approve** action that is hidden or disabled for the creator.
  - Approval is also shown on the streak form's reward picker.

### 3.5 Item 5 + A2: cashback fulfilment

- **A single engine seam.** A new `IRewardFulfilmentService` lives in `RuleEngine/Processing` (the same layer as `StampCompletionHandler`) and dispatches on `RewardType`. `RewardPurchaseHandler` and `StreakCampaignModule` both call it. That keeps the loose-coupling goal from 2026-09-17: one seam, not a branch in every handler.
- **Posting:** a ledger credit to `cash_account_type_id`. The steps are `UpsertAccountAsync` then `AddEntryAsync`, using the new `LedgerReason.RewardCashback = "reward_cashback"`.
- **Idempotency keys** (deterministic):
  - Purchase: `{eventId}:reward_cashback`.
  - Streak: `streak:{campaignId}:{contactKey}:{completionNo}:cashback`.
- **Purchase atomicity:** the points debit and the cash credit are posted in the **same transaction** that `RewardPurchaseHandler` already opens. That is two postings with one outcome, the same pattern as a transfer. Insufficient points still produces `loyalty.reward.purchase_failed` and credits nothing.
- **Streak completion:** the credit is posted inside the existing completion unit of work, next to `StreakLog`.
  - The cash ledger entry id is recorded in `StreakLog.RewardRef`, which is what `fixed_bonus` already does. The streak's unique index `(tenant, rule, contact, completion_no)` remains the last line of defence.
  - **No `RewardLog` row is added for streaks.** `RewardLog.AccountTypeId` and `LedgerResetEntryId` are required, and its unique index `(tenant, account_type_id, source_event_id)` would collide when two streak campaigns complete on the same event. A tier upgrade also has no ledger entry to point at.
  - The streak `fixed_bonus` path already passes the campaign id as `ruleId`. Cashback postings do the same, so the ledger can trace them.
- **Outbound event (additive, non-breaking):** `loyalty.reward.earned` gains these fields for cashback: `cashback_amount` (a string), `currency`, `cash_account_type_id` and `ledger_entry_id`. All snake_case.
- **Not covered:**
  - No reversal: refunding the order that built up a streak doesn't claw back the cashback (the same as a streak fixed bonus today).
  - No budget or limit cap exists on rewards, unlike rules (O7).

### 3.6 Item 5 + A2: tier-upgrade fulfilment (streak only)

- **Target:** the tier-qualifying `CustomerAccount` of the reward's program, upserted if missing.
- **If the target tier ranks above the current tier** (by `sort_order`):
  - Set `tier_id`.
  - Write a `TierUpgradeLog` (`source_event_id` = the streak's source event).
  - Publish `loyalty.tier.changed` (`direction = up`, new field `reason = "reward"`) with dedupe key `tier_changed:reward:{campaignId}:{contactKey}:{completionNo}`.
- **If the current tier is the same or higher:** no change to the tier. `loyalty.reward.earned` is still published, with `outcome = "already_at_or_above"`.
- **Duration** (`duration_days`):
  - A new nullable column `customer_accounts.tier_locked_until` (date, UTC).
  - `TierDowngradeJob` skips accounts where `tier_locked_until > today`.
  - When the lock lapses, the normal points-based evaluation (with grace) applies again.
  - The existing `tier_expires_at` is **not** reused: it means grace end, and overloading it would corrupt downgrade semantics.
- **Validation at creation:**
  - `target_tier_id` exists in **the same tenant and program**. This closes the gap in §2.1.
  - The program has a tier-qualifying account type, otherwise the request fails with `no_tier_qualifying_account`.
- **Tier deletion:** blocked while an active reward references the tier (`tier_in_use_by_reward`).

### 3.7 Item 6: `reward.purchase` and rule binding

- **`reward.purchase` stays the "buy with points" path, driven by the reward definition with no rule.**
  - Add an optional **`reward_id`** to the event (additive).
    - It goes on `RewardPurchaseRequest` (`EventsDtos.cs:12`) as a nullable `Guid`.
    - `RewardPurchaseRequestValidator` requires `reward_name` **or** `reward_id`.
    - The field is added to the `EventTypes.Catalog` entry for `reward.purchase`.
    - It travels in event `Data` as snake_case `reward_id`.
    - When present it is used, and the reward must belong to the tenant.
  - `reward_name` keeps working. Once A5 is in place (§3.10), an active reward name can match only one program in the tenant.
- **Streak campaign → reward** is the "earn" binding. Tighten it at the application layer only (still no foreign key, per the 2026-09-17 decision):
  - On streak campaign create or update, a `reward_definition_id` must reference an **active, approved, `streak_completion`** reward in the same program. Otherwise the request fails with `invalid_streak_reward`.
  - Deactivating or deleting a reward that an active streak campaign references is blocked (`reward_in_use_by_streak`).
  - The streak form's reward picker lists only eligible rewards.
- **Earn rules granting a reward definition** (for example an `order.created` rule that awards a cashback reward) are **not proposed**. That would be a new rule-calculation kind across the DSL, the engine and the rule builder, and needs its own CR. Today the same outcome is reached with a CASH-targeted FixedBonus or Spend rule, which is already approval-gated.

### 3.8 Item 7: event simulator (portal only, not a scope change)

- **Filter the scheduled types out of the dropdown.** Use the change log's snippet (`birthdaybonus` and `points.expired`). Also filter them out of the "Built-in: …" helper line.
  - Better alternative (O9): have the types endpoint return only externally publishable types, so that `EventTypes.IsExternallyPublishable` stays the single source.
- **Replace the helper paragraph** under the JSON textarea with the `@switch` block from the change log, with these corrections:
  - `order.refunded`: add "values as strings", because `refund_ratio`, `amount` and `original_amount` go through `GetString()`.
  - The `birthdaybonus` and `points.expired` cases can never be selected once they are filtered out. Drop them rather than keep dead markup.
  - `reward.purchase`: mention `reward_id`, and that `reward_name` is the full prefixed name (for example `fintech_cashback_50`).
  - **All new and touched strings go to keys in both `en.json` and `tr.json`** (`frontend.md` i18n rule). The page is hardcoded English today, and the strings it touches are migrated as part of this change. Keep the `<code>` markup outside the translated text.

### 3.9 Item 8: burn events and burn rules

**Goal:**
- Each burn trigger accepts only its own rule type.
- A burn rule, once created, actually changes what happens.
- Every burn event still produces **exactly one** set of postings.

**1. Trigger → rule-type allowlist (bug fix).**
- Add an explicit per-event allowlist to `RuleTypeCatalog`, checked **in addition to** the existing category/field-kind check:

  | Trigger | Allowed rule types |
  |---|---|
  | `points.transfer` | TransferRule only |
  | `points.redeem` | RedemptionRule only |
  | `reward.purchase` | none (configured via Rewards) |

- `RulesValidators` and `GET .../rules/metadata` both read the catalog, so the API and the portal follow automatically. `RuleTypeCatalog` stays the single source of truth (`backend-rule-engine.md`), with no second list anywhere.
- Existing rules that break the new mapping (for example a RedemptionRule on `points.transfer`) are reported by a pre-deploy query and **disabled** by the migration, not deleted. They have never posted anything (§2.8), so disabling them changes no balance.
  - Rows are soft-disabled (status change), so existing postings keep their `ruleId + ruleVersion` references.
  - The migration also bumps `updated_at` so that `RuleSyncService`'s delta sync picks the change up. Otherwise the Consumer must be restarted, or the `rules:{tenant}:{program}` cache invalidated.

**2. One posting path per burn event (scope change).**
- The existing handlers stay the **only** code that posts to the ledger for burn events. They already provide the transaction, the row locks, the redelivery guard, deterministic idempotency keys and the failure events.
- Before posting, each handler asks a **new RuleEngine seam, `Processing/IBurnRuleResolver`**, for the **winning burn rule**:
  - rule status Active and within its active window, from the cached rules (`IRuleCacheService`);
  - conditions pass (`GroupedConditionEvaluator`);
  - limits, budget and cooldown not exceeded (`IRuleLimitEvaluator` pre-check);
  - highest priority wins.

  It returns the rule (id, **version**, calculation) or none. It never posts.
  - Handlers must **not** call `ICampaignEvaluationService.EvaluateAsync`, which would run the engine twice (`backend-consumer.md`). The resolver is a narrower seam: Consumer → RuleEngine is an allowed reference (`backend.md`).
  - **Budget and limits:** when the winning rule has a budget, the handler reserves it **inside its own posting transaction** (`BudgetReservationService.LockAndGetUsageAsync` → `RecordUsageAsync`). It calls `ILimitCounterSync` after commit and writes a `RuleFireAudit` row, the same order as the engine pipeline (`backend-rule-engine.md`, "Money, rounding, limits").
  - **Idempotency keys stay deterministic:** the handlers' existing keys (`{eventId}:points_redeemed`, `{eventId}:transfer_out`, ...) are kept, so a redelivered event never posts twice whether or not a rule matched.
- **Which account the rule must match** (O12, recommended): the rule's target account type must equal the event's `source_account_type_id`.
- **Parameters the rule supplies** (existing `RuleCalculation` fields, camelCase as stored today):
  - TransferRule: `ratio`, `fee`, `maxPerDay`.
  - RedemptionRule: `rate` (cash per point, see O15), `minRedeem`, and the new `cashAccountTypeId` (see step 3).
- **No matching rule:** the handler falls back to today's account-type config (`config.redemption` / `config.transfer`). Existing tenants keep working without any rule.
- **A matching rule** overrides the account-type config for that event. The ledger entry records `ruleId` + `ruleVersion`, per the rule-versioning contract.
- **The generic engine must never post burn rules.**
  - Add `RedemptionRule` to the bypass set in `RuleTypes.UsesWinnerSelectorPipeline`, next to Transfer/Reversal. It becomes dual-entry (points debit + cash credit), which no longer fits the single-wallet winner model.
  - Remove the TransferRule/RedemptionRule dispatch from `RuleEngine.ProcessEventAsync`.
  - Keep `RedemptionRuleHandler` registered as an `IRuleTypeHandler`. Its pure `Compute` is reused by the resolver's caller, and unregistering it would make the registry fall back to `ZeroDeltaHandler` silently.
  - Whether `TransferRuleProcessor` is deleted or kept as the calculation helper is an implementation detail for the Architect's diff review.
  - This closes the double-debit risk in §2.8.
- Streak campaigns on burn events keep running through the generic path, unchanged.

**3. Rule-model gaps to close.**
- **RedemptionRule gets a cash leg.**
  - Add `calculation.cashAccountTypeId`. It must be a CASH wallet in the same tenant and program.
  - **Unit mismatch, resolved by A6:** the rule's existing `ratio` is documented as "points per currency unit" (cash = points ÷ ratio). The account type's `config.redemption.rate` is cash per point (cash = points × rate). The two are inverses. Two meanings would let admins configure a payout that is off by a factor of ratio². A6 settles it: cash per point, using the existing `rate` key.
  - The credit is rounded **down to the CASH wallet's `decimals`**. Today it is always 2 dp, which is wrong for KWD, BHD and OMR (3 dp).
  - The same rounding fix applies to the account-type fallback path.
- **CASH approval (O13, recommended yes):** a RedemptionRule with a cash leg credits real money, so it goes through the CR-04 second-admin approval even though its *target* is a POINTS account.
  - The CASH check in `RulesAppService` (create at `:98-123`, retarget at `:187-196`) is extended to "target is CASH **or** `calculation.cashAccountTypeId` is set".
  - Approval stays on the existing `PATCH .../rules/{ruleId}/approve`.
  - Editing the cash leg goes through `IRuleVersioningService` (new version, never an in-place update) and re-enters `PendingApproval`.
- **Rounding happens once:** the cash amount is rounded in one place only, in the resolver's caller at posting time (`backend-rule-engine.md`: "never round in two places").
- **Burn amount for conditions and limits:** `EvaluationEvent` reads `points_amount` for burn events (falling back to `amount`), so conditions such as "points_amount > 500" and amount-based limits see the real value.

**4. Handler behaviour fixes.**
- **`points.redeem`:** a business failure (`insufficient_points`, `below_minimum`) publishes a new outbound event `loyalty.points.redeem_failed` and marks the inbox `processed`, instead of throwing to the DLQ. This mirrors `points.transfer` and `reward.purchase`.
  - It is implemented the way those handlers do it: an explicit **balance pre-check under the row lock**, then the failure event through the outbox with a dedupe key (`redeem_failed:{eventId}`). **Not** by catching and swallowing an exception, which `01-workflow-and-debugging.md` forbids.
  - The ledger's own `insufficient_points` exception stays as the last guard.
  - A configuration error (`redemption_not_configured`, unknown account type) still throws to the DLQ, as today (`backend-consumer.md`: throw on unrecoverable input).
  - New constants are added to `Shared/Events/OutboundEventTypes.cs`.
- **`points.redeem` success:** a new additive outbound event `loyalty.points.redeemed` (points, cash amount, `rule_id` if any). Today success publishes nothing.
- **`reward.purchase`:** stays driven by the reward definition, with **no rule** (§3.7). The only changes are `reward_id` and the program-prefixed reward names (§3.10).

**5. Portal (`programs/rules/rule-form.page.ts`, bug fix).**
- When `typeOptions()` is empty, show "No rule type applies to this trigger" and disable Save. Remove the hidden `RULE_TYPES[0]` fallback, which is what currently shows POINTS/CASH targets for `reward.purchase`.
- In the trigger dropdown, label `reward.purchase` as "configured via Rewards" (or omit it).
- The RedemptionRule form gains a CASH wallet picker for `cashAccountTypeId`, and shows the CR-04 approval state. `rule.model.ts` gains `cashAccountTypeId?: string | null` in the calculation type. It uses camelCase to match the existing `RuleCalculation` keys (`minRedeem`, `maxPerDay`), not the snake_case used in reward `typeConfig`.
- New strings go to `en.json` and `tr.json`.

**6. Program status (O14).** Under 1.3.CL the generic engine only evaluates published + active programs, but the burn handlers don't check program status (SOW §3). Once a burn rule is resolved, it will naturally come only from published + active programs (the rule cache loads only those). The **fallback** path (no rule) stays ungated unless O14 decides otherwise.

**Phasing:**
- **P1 (no balance changes):**
  - step 1 (allowlist + rule-disable migration);
  - step 5 (portal);
  - from step 4, the `points.redeem` failure event and the `loyalty.points.redeemed` success event. They only change how an outcome is reported. The same postings happen or don't happen.
- **P5 (one unit):** steps 2 and 3.
  - Shipping step 2 without step 3's cash leg would make RedemptionRule debit points without paying cash.
  - **`EvaluationEvent` reading `points_amount` must never ship before the generic-pipeline bypass in step 2.** On its own, it would make burn rules fire in the generic engine *after* the handler has already posted. That would turn the latent double debit in §2.8 into a real one on every burn event.
  - The wallet-decimals rounding fix on the fallback path changes cash amounts, so it also goes in P5.
- **Per A7, this CR delivers P1 only.** P5 is fully specified here, so a follow-up CR can adopt it without redoing the analysis. Until then burn rules stay inert, and `points.redeem` / `points.transfer` stay driven by account-type config.

### 3.10 A5: program slug and program-prefixed reward names

**Goal:** an active `reward_name` identifies exactly one reward in a tenant, so that `reward.purchase` can never resolve to another program's reward (§2.6).

**Program slug (new field on Programs; touches the Programs baseline row).**
- **Schema:** a new column `programs.slug`, `varchar(40)`.
  - Following the safe-migration order in `backend-schema-shared.md`:
    1. add it as nullable,
    2. backfill it,
    3. then make it `NOT NULL`.
  - Unique index `ux_programs_tenant_slug` on `(tenant_id, slug)`.
- **Format:** `^[a-z0-9]+(-[a-z0-9]+)*$`, 2–40 characters (lowercase letters, digits and hyphens).
  - **Underscores are not allowed**, because `_` is the separator between slug and reward name.
  - Without that restriction, programs `fin` and `fin_x` could both own a reward called `fin_x_gold`.
- **Lifecycle:**
  - Set on create. `CreateProgramRequest` gets an optional `Slug`, defaulting to one derived from the name.
  - Editable while the program is **Draft**. **Locked once the program is first published** (`slug_locked`), mirroring the CASH currency lock. After publish, reward names and integrations depend on it.
  - Renaming the program (`name`) never changes the slug.
- **Audit:** create and edit already write `ConfigVersion` rows (`ProgramsAppService.cs:71, :99`). The slug is part of the entity, so it appears in them and in the publish snapshot (`PublishedProgram` gains `Slug`, additive).
- **Naming:** this is a *program* slug. It is unrelated to the tenant slug in the route (`/tenants/{tenantId}`). The DTO field is `slug`, and API docs must say "program slug" explicitly to avoid confusion.
- **Backfill for existing programs:**
  - Derive the slug from `name`: lowercase, non-alphanumeric runs → `-`, trimmed, truncated to 40 characters.
  - De-duplicate per tenant by adding `-2`, `-3`, …
  - A pre-deploy query lists the proposed slugs for PO review.
  - Already-published programs get their slug locked immediately. A wrong backfill can only be corrected by a platform-level fix, so review before deploy matters.

**Reward name rule.**
- **Pattern:** a new reward's `name` must match `{program.slug}_{suffix}`, where the suffix matches `^[a-z0-9_]+$` and the total is ≤ 100 characters (the existing limit). Example: `fintech_cashback_50`.
  - Checked in `RewardsAppService` (which needs the program) with a new error code `reward_name_prefix_required`.
- **Renames:** the same pattern applies when `UpdateRewardRequest.Name` changes a name.
- **Grandfathering:** existing rewards keep their current names until they are renamed.
  - Why this breaks nothing: after A1, the only rows left active are `tier_upgrade` rewards, which are bound to streaks **by id**, never by `reward_name`.
  - So no live `reward.purchase` integration loses a name.
  - `RewardLog.RewardName` keeps the name as it was at the time.
- **Database guarantee:** a new partial unique index `ux_reward_definitions_tenant_name_active` on `(tenant_id, name) WHERE is_active`.
  - Together with the unique slug, this guarantees the handler's `(tenant, name, active)` lookup returns at most one row, including for grandfathered names.
  - The existing `uq_reward_definitions_tenant_program_name` stays.
  - Creating the index fails if active duplicates exist, so a pre-deploy query is required.
- **Portal:**
  - Create program dialog: a slug field prefilled from the name, with a format hint. It is disabled after publish, with a "locked" hint.
  - Reward form: the name input shows `{slug}_` as a fixed, non-editable prefix, and the admin types only the suffix.
  - All strings go to `en.json` and `tr.json`.

**Why a prefix instead of (or in addition to) other options.** The prefix makes the owning program visible in every integration payload and log line. That is the benefit the developer chose it for. The partial unique index is the database-level guarantee behind it.

## 4. Blast radius (end to end)

| Layer | Files / artifacts | Change |
|---|---|---|
| **Shared** | `Shared/Constants/RewardAcquisition.cs`, `RewardType.cs`, `LedgerReason.cs`, new `RewardStatus.cs` | Creatable vs retired sets; `reward_cashback` reason; `active`/`pending_approval` |
| **Schema** | `Entities/RewardDefinition.cs`, `RewardDefinitionConfiguration.cs`, `Entities/CustomerAccount.cs` + configuration, **new migration** | `status`, `created_by`, `approved_by`; `customer_accounts.tier_locked_until`; deactivation `UPDATE`; optionally drop the stamp index |
| **API: Rewards** | `RewardsDtos.cs`, `RewardsValidators.cs`, `RewardTypeRegistry.cs`, `RewardsAppService.cs`, `RewardsModule.cs` | Compatibility matrix, retired types, cashback wallet and currency checks, tier ownership check, approve endpoint, `Status`/`CreatedBy`/`ApprovedBy` added to `RewardResponse` (additive) |
| **API: other** | `StreakCampaigns/*AppService.cs`, `*Validators.cs`; `Tiers/*AppService.cs`; `Events/EventsDtos.cs`, `EventsValidators.cs`; `Programs/ProgramsAppService.cs` + `ProgramsDtos.cs` (publish snapshot) | Streak reward eligibility; block tier delete; `reward_id` on `reward.purchase`; snapshot carries the new fields |
| **API framework** | `DomainErrorTranslator` prefix map | New codes: `tier_upgrade_requires_streak_completion`, `reward_type_retired`, `self_approval_forbidden`, `invalid_streak_reward`, `reward_in_use_by_streak`, `tier_in_use_by_reward`, `no_tier_qualifying_account`, plus the A5 codes below. New codes only; existing codes are never renamed (`backend-api-framework.md`). |
| **RuleEngine** | New `Processing/RewardFulfilmentService.cs`; `Campaigns/Streak/StreakCampaignModule.cs`; `Processing/StampCompletionHandler.cs`; `TierDowngradeJob.cs` | Fulfilment seam; streak calls it and records the result in `StreakLog.RewardRef` (no `RewardLog`, §3.5); stamp path no longer resolves definitions; downgrade honours `tier_locked_until` |
| **Consumer** | `Handlers/RewardPurchaseHandler.cs`, `Program.cs` (DI) | `reward_id`, approval check, cashback credit in the same transaction. The name lookup is unchanged and becomes unambiguous through the A5 index. |
| **Ledger** | No code change; new reason value only | Append-only preserved; deterministic keys (§3.5) |
| **Outbound events** | `loyalty.reward.earned`, `loyalty.tier.changed` | **Additive** fields. **Breaking:** stamp-sourced `reward.earned` stops (O1) |
| **Portal** | `programs/rewards/reward.model.ts`, `reward-form.dialog.ts`, `rewards-list.page.ts`, `rewards.service.ts`; `programs/streak-campaigns/streak-campaign-form.page.ts`; `events/event-simulator.page.ts`; `en.json`, `tr.json` | Field order, filtered options, currency and wallet pickers, approve action, retired-row display, simulator help |
| **A5: program slug** | `Schema/Entities/Program.cs`, `ProgramConfiguration.cs`, `RewardDefinitionConfiguration.cs` (partial unique index), new migration (slug add → backfill → NOT NULL); `Api/Programs/ProgramsDtos.cs`, `ProgramsValidators.cs`, `ProgramsAppService.cs` (slug lock on publish, snapshot); `Api/Rewards/RewardsAppService.cs` (prefix check on create and rename); portal `programs/program.model.ts`, `create-program.dialog.ts`, `programs.service.ts`, `rewards/reward-form.dialog.ts`, `en.json`, `tr.json` | New error codes `reward_name_prefix_required`, `slug_locked`, `slug_taken` (map them in `DomainErrorTranslator` if raised below the API) |
| **Item 8 (P1): RuleEngine** | `Metadata/RuleTypeCatalog.cs` (per-event allowlist) | Trigger → rule-type mapping |
| **Item 8 (P5, deferred): RuleEngine** | New `Processing/IBurnRuleResolver.cs` + implementation; `RuleEngine.cs` (drop the burn dispatch); `Models/EvaluationEvent.cs` (`points_amount`); `Models/RuleCalculation.cs` (`cashAccountTypeId`); `Shared/Constants/RuleTypes.cs` (`UsesWinnerSelectorPipeline`) | Single posting path; RedemptionRule cash leg |
| **Item 8: Consumer** | `Handlers/PointsRedeemHandler.cs`, `PointsTransferHandler.cs`, `Program.cs` | **P1:** `redeem_failed` / `redeemed` events in `PointsRedeemHandler`. **P5 (deferred):** resolve the rule, fall back to account-type config, wallet-decimals rounding, DI for the resolver. |
| **Item 8: API** | `Rules/RulesValidators.cs` | **P1:** trigger allowlist via `RuleTypeCatalog` (the allowlist itself lives in `Metadata/RuleTypeCatalog.cs`, which also ships in P1). **P5 (deferred):** RedemptionRule calculation fields per A6, and the CASH approval extension in `Rules/RulesAppService.cs`. |
| **Item 8 (P1): migration** | Disable rules that break the allowlist, and bump `updated_at` | Pre-deploy query required |
| **Item 8: Portal** | `programs/rules/rule-form.page.ts`, `rule.model.ts`, `en.json`, `tr.json` | **P1:** empty-type state and the `reward.purchase` trigger label. **P5 (deferred):** cash wallet picker, approval state. |
| **Tests** (`backend-tests.md`) | **Engine.Tests:** resolver winner selection, `Compute` table tests, tier lock, downgrade skip, wallet-decimals rounding (`[Theory]`, exact `decimal` asserts).<br>**IntegrationTests/Api:** reward matrix (valid case + each invalid case, including "field set when it must not be"), approval incl. self-approval, `reward_id`, each new error code → its HTTP status, a **tenant B cannot read or modify tenant A** test for rewards, wallets and tiers.<br>**IntegrationTests/E2E:** purchase + cashback, streak + tier/cashback, redeem/transfer with and without a rule. **Every ledger path asserts idempotency:** same event twice → one posting, one budget reservation, one limit increment.<br>**Every bug fix** (portal fallback, trigger allowlist, redeem failure) gets a test that fails without the fix.<br>**Also:** `TestCli/RewardTest.cs` (RW01 uses stamp + `free_product`, RW02 uses `discount`: both must be rewritten); portal specs. | |
| **Docs** | `SCOPE_BASELINE.md`, `SOW.md` §2.2/§3, `scripts/loyalty_schema_reference.md` (regenerate, never hand-edit), `scripts/loyalty_schema.sql` / `provision_tenant.sql` (keep in step if provisioning depends on the new columns), `LoyaltySaaSApi.md` (missing → regenerate first), `.claude/rules/01-workflow-and-debugging.md` pitfalls table (the "Redeem / transfer … inactive program" and burn-rule rows change meaning) | |

**Contract check** (`.claude/rules/00-project-guardrails.md` §2):

| Contract | How this CR keeps it |
|---|---|
| Tenancy | Strengthened: tier and wallet ownership checks, `reward_id` scoped to the tenant, program slug unique per tenant (never global) |
| Money | `amount` stays a decimal string / `numeric(20,4)` |
| Ledger | Append-only, with deterministic keys |
| Events | snake_case, outbox, `EventInbox` dedupe unchanged |
| CASH approval | Extended to rewards |
| Time | `tier_locked_until` is a UTC date |
| Rule versioning | Burn postings made under a rule carry `ruleId + ruleVersion`. Cash-leg edits create a new version (§3.9). |
| DSL parity | No operator or field-validation change. `points_amount` is already a catalog field, so the portal condition DSL is unaffected. |

## 5. Open decisions requiring explicit PO/Architect sign-off

| # | Decision | Recommendation |
|---|---|---|
| O1 | Stamp completions stop publishing `loyalty.reward.earned`. Does any live tenant integration consume it? Alternatively, keep publishing it without a definition (`reward_type = null`). | Confirm with PO before migrating. If unsure, keep publishing with `reward_name` from the stamp rule. |
| O2 | Existing `cashback` rows have no wallet, so they can't be fulfilled. Deactivate them, or backfill a wallet where the program has exactly one CASH wallet in that currency? | Deactivate them, and list them in the pre-deploy report so admins can re-create them. |
| O3 | Drop `ux_reward_definitions_active_stamp`, or keep it for the historical rows? | Keep it (harmless, avoids churn). |
| O4 | A tier upgrade with **no** `duration_days`: permanent (lock forever) or the normal lifecycle (may downgrade at period end)? | Normal lifecycle. "Permanent" in the UI is misleading, so relabel it "Until next tier review". |
| O5 | Should a forced upgrade reset `tier_period_start` to today? This changes the qualifying window. | No. Keep the current period, and the lock handles protection. |
| O6 | How to stop one `reward_name` matching rewards in several programs of the same tenant. | **Decided → A5 (2026-09-30):** a program-slug prefix plus a partial unique index on active names (§3.10). Considered and rejected: failing on ambiguity only, and tenant-wide uniqueness without a prefix. `reward_id` is added as optional, not required. |
| O7 | Cashback rewards have no budget or limit cap, unlike rules. Is that acceptable for v1? | Acceptable for v1, because approval gates the value. Budget caps would be a follow-up CR. |
| O8 | **Existing gap:** streak `fixed_bonus` into a CASH wallet bypasses CR-04. Fix it in this CR or in a separate one? | Separate CR, but approve it together with this one. It is the same money-safety contract. |
| O9 | Simulator: filter the scheduled types in the portal only, or at the API types endpoint? | API endpoint (single source), with the portal filter as a fallback. |
| O10 | Should the cash credit from a points purchase be gated on the program being published and active? Today `reward.purchase` isn't gated (SOW §3). | Yes, for the cashback credit at least. Real money shouldn't flow from an inactive program. This needs the SOW §3 wording updated. |
| O11 | Item 8: should burn rules become effective in this CR (§3.9 steps 2–3)? | **Decided → A7 (2026-09-30):** P1 (bug fixes) in this CR. P5 goes to a separate decision. |
| O12 | Burn rule matching: must the rule's target account equal the event's `source_account_type_id`? | (Applies when P5 is taken up.) Yes. Otherwise a rule for wallet A could burn from wallet B. |
| O13 | Does a RedemptionRule with a cash leg need CR-04 second-admin approval? | (Applies when P5 is taken up.) Yes. It credits CASH. |
| O14 | Program-status gating for the burn **fallback** path (no matching rule). | Keep today's behaviour (ungated, SOW §3) in this CR. Rule-driven burns are gated automatically via the rule cache. |
| O15 | RedemptionRule `ratio` ("points per currency unit") vs account-type `rate` (cash per point): which meaning wins? | **Decided → A6 (2026-09-30):** cash per point, using the existing `rate` key (`Factor`). Applies when P5 is taken up.<br>`ratio` is rejected for RedemptionRule on new versions. Existing rule rows are **not** rewritten (rule-versioning contract). An old version with only `ratio` has no cash leg and needs a new version.<br>**This is an explicit validator contract change, not a loosening:** `RulesValidators.cs:68-70` currently *requires* `ratio` for RedemptionRule. It would instead require `rate` > 0 and `cashAccountTypeId`, and reject `ratio`. The portal rule form changes to match. |

## 6. Risks

- **Real-money exposure.** Rewards move from notification-only to posting CASH. Any misconfiguration now pays out. Mitigations: approval (A4), wallet and currency checks, deterministic idempotency keys, and O10.
- **Data change on live tenants.** A1 deactivates **every pre-taxonomy reward** (they default to `points_bonus`), plus every stamp, discount, free-product and gift-card reward. Purchases of those rewards will fail with `unknown_reward` and go to the DLQ. **Run the pre-deploy query below per environment** and share the result with the PO before migrating.
- **Downstream event consumers.** New fields are additive and safe. The loss of stamp-sourced `reward.earned` is not safe (O1).
- **Tier semantics.** A lock that the downgrade job doesn't know about would silently revert upgrades. That is why the job change and the fulfilment change must ship together.
- **Double evaluation.** The known double-evaluation pitfall (`backend-consumer.md`) doesn't affect `reward.purchase` or the streak completion path, but the fulfilment idempotency keys are still the last line of defence.
- **Burn events (item 8).**
  - Until P5 ships, burn rules stay inert, and the double-debit path in §2.8 stays open for any caller that sends both `amount` and `points_amount`.
  - Once P5 ships, a matching burn rule changes real balances. Existing rules that pass the new allowlist (for example a RedemptionRule on `points.redeem`) will **start taking effect on deploy**. The pre-deploy query must list them so the PO can review each one first.
- **Program slug backfill (A5).** Slugs of already-published programs are locked as soon as they are backfilled. A poor auto-generated slug becomes permanent in every new reward name, so the proposed-slug query must be reviewed before deploy.
- **Outbound payloads.** Only fields are added, but a downstream consumer that validates a strict schema could reject the new fields (`backend-engine-framework.md`: payloads must stay byte-compatible). Notify integrators before deploy.

Pre-deploy query (read-only):

```sql
SELECT tenant_id, program_id, acquisition, reward_type, is_active, count(*)
FROM reward_definitions
WHERE acquisition = 'stamp_completion'
   OR reward_type NOT IN ('cashback','tier_upgrade')
   OR (reward_type = 'cashback' AND NOT (type_config ? 'cash_account_type_id'))
GROUP BY 1,2,3,4,5 ORDER BY 1,2;
```

Pre-deploy query for item 8 (read-only). It lists every burn rule, so the PO can see which rules would be **disabled** (wrong trigger) and which would **start taking effect** (right trigger):

```sql
SELECT tenant_id, program_id, id, name, trigger, type, status, current_version
FROM rules
WHERE type IN ('RedemptionRule','TransferRule')
  AND status <> 'deleted'
ORDER BY tenant_id, program_id, trigger;
```

Pre-deploy queries for A5 (read-only).

Active reward names that would block the new partial unique index. Run this **after** estimating A1's deactivations:

```sql
SELECT tenant_id, name, count(*) AS active_rows
FROM reward_definitions
WHERE is_active
  AND acquisition <> 'stamp_completion'
  AND reward_type IN ('cashback','tier_upgrade')
GROUP BY tenant_id, name
HAVING count(*) > 1;
```

Proposed program slugs for PO review. This mirrors the backfill rule, and duplicates are resolved with `-2`, `-3` in the migration:

```sql
SELECT tenant_id, id, name, publication_status,
       left(trim(both '-' from regexp_replace(lower(name), '[^a-z0-9]+', '-', 'g')), 40) AS proposed_slug
FROM programs
ORDER BY tenant_id, proposed_slug;
```

## 7. Suggested phasing and verification

| Phase | Content | Can ship alone? |
|---|---|---|
| P1 | Item 7 simulator; item 8 bug fixes (trigger → rule-type allowlist, portal empty-type state, redeem failure event) | Yes. None of these changes a balance. |
| P2 | Items 1–4: taxonomy, matrix, form order, currency and wallet, approval, migration | Only after the O1/O2 decisions |
| P3 | Item 5: fulfilment seam, cashback (purchase + streak), tier upgrade + lock + downgrade job | Must ship with P2 (otherwise cashback rewards exist but still pay nothing) |
| P4 | Item 6: `reward_id`, program slug + prefixed reward names (A5, §3.10), streak reward eligibility | With P2/P3. The slug backfill must be reviewed first. |
| P5 | Item 8 steps 2–3: burn rules effective through `IBurnRuleResolver`, RedemptionRule cash leg, generic-pipeline bypass | **Not in this CR (A7).** A follow-up decision; needs O12 and O13 then. |

Verification, per `.claude/rules/01-workflow-and-debugging.md`:
- `dotnet build dEngage.Loyalty.sln`.
- `dotnet test src/dEngage.Loyalty.Engine.Tests`.
- `dotnet test src/dEngage.Loyalty.IntegrationTests` (needs Docker).
- `npx ng lint`, `npm test` and `npx ng build --configuration development` in `web/`.
- A manual run through `.\run.ps1`:
  1. Create a CASH wallet and a cashback reward, then approve it as a second admin.
  2. Send `reward.purchase` → check the points debit, the cash credit and the event.
  3. Complete a streak bound to a tier-upgrade reward → check the tier change, the lock and the nightly job skip.

## 8. Explicitly out of scope

- Earn rules granting reward definitions (§3.7).
- A hard foreign key from streak campaigns to rewards (declined on 2026-09-17).
- Fulfilment of the retired reward types.
- Budget and limit caps on rewards (O7).
- Fixing the streak CASH approval gap (O8, separate CR).
- Program-status gating of redeem, transfer and expiry (SOW §3), except the cashback credit in O10. Rule-driven burns are gated implicitly through the rule cache (§3.9, O14).
- **P5: burn rules posting to the ledger** (§3.9 steps 2–3). Specified here but deferred by A7. Until then the generic pipeline is not changed at all.
- Earn-side changes to the generic rule pipeline.
- Renaming existing (grandfathered) rewards to the prefixed form (§3.10).

## 9. Next step

1. Raise a Jira Scope Change Request that references this document.
2. The Product Owner and Architect confirm A1–A7, resolve the remaining open decisions (O1–O5, O7–O10, and O12–O14 when P5 is taken up), and approve §3.
3. Only then does implementation start. At that point this document gets an approval record and, once the work is done, an implementation record listing every file that was changed.

## 10. Document record (no git — review record per `00-project-guardrails.md` §6)

| Date | Change | Files |
|---|---|---|
| 2026-09-30 | Draft created (items 1–7) | Created `docs/scope-changes/2026-09-30-reward-acquisition-fulfilment.md` |
| 2026-09-30 | Added item 8 (§0 row, §2.8, §3.9, §4 rows, O11–O15, P5, item-8 pre-deploy queries). Consistency pass against `.claude/rules/`. Changes from that pass: reward approve route uses PATCH like rules; migration marks published programs changed; `status` column added nullable → backfill → NOT NULL; streak rewards recorded in `StreakLog.RewardRef` rather than `RewardLog`; frontend unions keep retired values; i18n made mandatory; burn handlers use a dedicated resolver rather than `EvaluateAsync`; RedemptionRule cash leg uses the existing `rate` key; the O6 program-prefix option added. Contradictions fixed: the §4 streak `RewardLog` row, the Consumer "ambiguity check" row, redeem outcome events moved to P1 in both §3.9 and §7, the `points_amount` ↔ bypass ordering made explicit, and §9's O-range. | Changed `docs/scope-changes/2026-09-30-reward-acquisition-fulfilment.md` |
| 2026-09-30 | Developer decisions recorded: O6 → A5 (program-slug prefix, §3.10 added), O15 → A6 (cash per point), O11 → A7 (P1 now, P5 deferred). §0, §4, §5, §6, §7, §8 and §9 updated to match. A5 pre-deploy queries added. | Changed `docs/scope-changes/2026-09-30-reward-acquisition-fulfilment.md` |
| 2026-10-01 | Author set to Moiz. Status icons replaced with words. Proposed SOW text drafted. | Changed this file; changed `docs/SOW.md` (v1.4 draft: proposed changes marked "Proposed (CR 2026-09-30)", plus a revision-history row) |

| 2026-10-01 | CR approved (approval record added). Phase P1 implemented and verified (§11). | See §11.4 |
| 2026-10-01 | Phases P2–P4 implemented and verified (§11.6–§11.10). | See §11.9 |
| 2026-10-01 | Addendum A (approved): points-transfer limit on the account-type screen, the form's save bug, CASH transfer/redeem analysis (§12). | See §12.5 |

## 11. Implementation record

### 11.1 Phase P1 (2026-10-01): done

Implemented: §3.8 (Event Simulator), §3.9 step 1 (trigger → rule-type allowlist + migration), §3.9 step 4 (`points.redeem` outcome events), §3.9 step 5 (rule-form empty state).

### 11.2 Deviations from §3, and why

| Item | Deviation | Reason |
|---|---|---|
| O9 | Scheduled types are **not removed** from `GET events/types` → `builtIn`. Instead the response gains an additive `publishable` list (built-ins where `EventTypes.IsExternallyPublishable`, plus generic types); the simulator uses it and also filters as a fallback. | The rule and streak builders read the same endpoint and need `birthdaybonus` / `points.expired` as triggers. Removing them from `builtIn` would have broken birthday-bonus rules. |
| §3.9 step 5 | `reward.purchase` is **omitted** from the trigger list (the CR allowed "label it or omit it"), except when an existing rule already uses it. The rule is general: any trigger whose metadata lists no compatible rule type is omitted. | `SelectOption` has no disabled/label-suffix support; adding it would change a shared UI component outside this CR. |
| §3.9 step 4 | `points.redeem` also gained a **redelivery guard** (skip if `{eventId}:points_redeemed` already exists, before the checks). | Without it, a redelivered event would see the already-debited balance and publish a false `redeem_failed`. Same guard as `PointsTransferHandler`. |

### 11.3 Verification

| Check | Result |
|---|---|
| `dotnet build dEngage.Loyalty.sln` | **Cannot run:** the solution references a missing project `src/dEngage.Loyalty.LifeSim` (pre-existing, unrelated). Every real project was built individually instead: all succeed. Api and IntegrationTests were built into a scratch output folder because the running Api process (and Visual Studio) locks `src/dEngage.Loyalty.Api/bin`. |
| `dotnet test src/dEngage.Loyalty.Engine.Tests` | 77 passed, 0 failed (includes 12 new `RuleTypeCatalogTests` cases) |
| `dotnet test src/dEngage.Loyalty.IntegrationTests` (incl. E2E, Docker) | 114 passed, 0 failed (includes 4 new `BurnTriggerRuleTypesApiTests` and 5 new `PointsRedeemOutcomeE2ETests`). The E2E tests apply all migrations to Postgres, so the new migration's SQL was executed. |
| `npx ng lint` | No findings in changed files. 3 pre-existing errors in untouched files (`core/ui/confirm.service.ts` x2, `features/dashboard/dashboard.page.ts`). |
| `npm test` | 81 passed, 0 failed (includes 3 new `rule-form.page.spec.ts` cases) |
| `npx ng build --configuration development` | Succeeded |
| Manual run (`.un.ps1`) | Not done. The Api is already running from your session, and P1 has no flow the automated tests don't cover. |

Tooling: `dotnet-ef` 8.0.31 was installed globally (approved by the developer) to generate the migration.

### 11.4 Files created / changed in P1 (review record)

| File | Change | Why |
|---|---|---|
| `src/dEngage.Loyalty.Api/Events/EventsDtos.cs` | Changed | `EventTypesResponse` gains `Publishable` (O9) |
| `src/dEngage.Loyalty.Api/Events/EventsAppService.cs` | Changed | Computes `publishable` with `EventTypes.IsExternallyPublishable` |
| `src/dEngage.Loyalty.RuleEngine/Metadata/RuleTypeCatalog.cs` | Changed | Per-event rule-type allowlist in `IsCompatible` |
| `src/dEngage.Loyalty.Shared/Events/OutboundEventTypes.cs` | Changed | `PointsRedeemed`, `PointsRedeemFailed` |
| `src/dEngage.Loyalty.Consumer/Handlers/PointsRedeemHandler.cs` | Changed | Outcome events, redelivery guard |
| `src/dEngage.Loyalty.Schema/Migrations/20261001083819_BurnTriggerRuleTypesCr0930.cs` + `.Designer.cs` | Created | Disables rules that break the allowlist; marks published programs changed |
| `src/dEngage.Loyalty.Schema/Migrations/LoyaltyDbContextModelSnapshot.cs` | Regenerated by `dotnet ef` | No model change (data-only migration) |
| `src/dEngage.Loyalty.TestCli/TiqmoTest.cs` | Changed | TQ03 expectation updated to the approved behaviour: below-minimum is now processed + `redeem_failed`, not dead-lettered |
| `src/dEngage.Loyalty.Engine.Tests/RuleTypeCatalogTests.cs` | Created | Allowlist table tests |
| `src/dEngage.Loyalty.IntegrationTests/Api/BurnTriggerRuleTypesApiTests.cs` | Created | API validation, metadata, `publishable` |
| `src/dEngage.Loyalty.IntegrationTests/E2E/PointsRedeemOutcomeE2ETests.cs` | Created | Redeem outcome events, redelivery, config errors (Postgres) |
| `web/src/app/core/events/event-types.service.ts` | Changed | `publishable` on `EventTypesCatalog` |
| `web/src/app/features/events/event-simulator.page.ts` | Changed | Publishable types, per-event field help, i18n |
| `web/src/app/features/programs/rules/rule-form.page.ts` | Changed | Omit rule-less triggers, empty state, no hidden fallback, Save disabled |
| `web/src/app/features/programs/rules/rule-form.page.spec.ts` | Created | Trigger-list tests |
| `web/public/i18n/en.json`, `web/public/i18n/tr.json` | Changed | Simulator and rule-form keys |
| `docs/SCOPE_BASELINE.md` | Changed | Events, Rules, Consumer, `events`, `programs/rules` rows; revision row |
| `docs/SOW.md` | Changed | P1 items marked "Built"; status line |
| `docs/scope-changes/2026-09-30-reward-acquisition-fulfilment.md` | Changed | Approval record, this implementation record |

Not changed: `scripts/loyalty_schema_reference.md` (P1's migration is data-only, so the schema is unchanged). `LoyaltySaaSApi.md` is still missing, so the additive `publishable` field isn't recorded there.

### 11.5 Found during P1, outside this CR (not fixed)

- **`POST /events/points-transfer` was broken — fixed 2026-10-01 (bug fix, not a scope change).** `PointsTransferRequest` has `Amount` and `AccountTypeId`, which were published as `amount` / `account_type_id`, while `PointsTransferHandler` reads `points_amount` / `source_account_type_id`; every event sent through that route was dead-lettered. Fix: the route now publishes `request.ToEventData()` (`PointsTransferEventData`), mapping to the handler's field names. The request contract is unchanged, so no caller breaks. Files: `src/dEngage.Loyalty.Api/Events/EventsDtos.cs`, `EventsModule.cs` (changed); `src/dEngage.Loyalty.IntegrationTests/Api/PointsTransferRouteApiTests.cs` (created — fails with `KeyNotFoundException` without the fix, verified). Integration suite: 151 passed.
- **`dEngage.Loyalty.sln` references a missing project** (`src/dEngage.Loyalty.LifeSim`), so `dotnet build dEngage.Loyalty.sln` fails before compiling.
- `RulesValidators.cs` says `IsCompatible` is shared with the engine's runtime matching; it isn't (`RuleMatcher` doesn't call it). The comment is inaccurate, which is why the migration has to disable non-compliant rules.

### 11.6 Phases P2–P4 (2026-10-01): done

Implemented: §3.1 (taxonomy, matrix, retired values, migration), §3.2 (form order and filtering), §3.3 (cashback currency + wallet), §3.4 (cashback approval), §3.5 (cashback fulfilment), §3.6 (tier-upgrade fulfilment + lock), §3.7 (`reward_id`, streak reward eligibility, in-use guards), §3.10 (program slug + prefixed reward names), and the approved defaults O1–O5, O7, O9, O10. P5 stays deferred (A7).

### 11.7 Deviations from §3, and why

| Item | Deviation | Reason |
|---|---|---|
| §3.5 | Cashback ledger postings carry **no `ruleId`**. The streak campaign / purchase source is recorded in the entry's metadata instead. | `ledger_entries.rule_id` is mapped as a foreign key to `rules`; a streak campaign id isn't a rule id, so passing it could violate the FK. Reward postings aren't rule postings, so the CR-09 `ruleId + ruleVersion` rule doesn't apply to them. |
| §3.4 | The portal shows **Approve** for every pending reward, and the API rejects self-approval (400), instead of hiding the button for the creator. The self-approval error is a `ValidationApiException` with a message, not a `self_approval_forbidden` code. | Mirrors the existing CASH-rule approval exactly (`RulesAppService.ApproveAsync` and the rules list page). The portal doesn't compare the current admin with `createdBy` anywhere today. |
| §3.5 | A `reward.purchase` for a cashback reward that is still pending approval **throws `reward_not_approved`** (inbox failed → DLQ), like `unknown_reward`. | Selling an unapproved reward is a configuration problem that needs an admin, not a customer-facing outcome. |
| §3.5 | `loyalty.reward.earned` also gains `reward_id` (purchase and streak). | Additive; lets consumers identify the reward exactly, matching the new `reward_id` input. |
| §3.5 | `StreakLog.RewardRef` is the cash ledger entry id for cashback, and stays the reward definition id otherwise (tier upgrade, already at or above). | A tier upgrade has no ledger entry; keeping the definition id there is the pre-CR behaviour. |
| §3.6 | The fulfilment service reads the customer's current tier **from the database**, not from the account instance `UpsertAccountAsync` returns. | Found by the E2E test: that instance can be one the `DbContext` already tracks, with a stale `TierId` after a set-based update, which would have upgraded a customer already above the target. |
| §3.10 | `programs.slug` has a **database-only default** (`p-` + 10 random hex characters), and the entity has the same kind of default. | The tenant seed scripts (`scripts/*_loyalty.sql`, reference data, untouched) insert programs with raw SQL and no slug. Code paths always set the slug explicitly. |
| §3.10 | A Draft program's slug can be changed even if it already has rewards; their names keep the old prefix (grandfathered like pre-CR names). | The CR allows slug edits while Draft; blocking them would add a rule the CR didn't ask for. |
| §3.2 | Money fields in the reward form are text inputs holding `DecimalString`s; the old form used number inputs and `Number(...)`. | Required by the money contract (never a JS `number`). |
| §4 | `DomainErrorTranslator` is **unchanged**. | All new API-side codes are thrown as typed `ApiException`s (`ValidationApiException` / `ConflictApiException`), which need no prefix mapping. The new consumer-side codes (`reward_not_approved`) never reach the API. |
| §4 | `scripts/loyalty_schema_reference.md` is **not** updated. | Same open item as 1.3.CL §10.4: it is prose with no generator, and the rules forbid hand-editing it. `scripts/loyalty_schema.sql` *was* regenerated. Needs a decision: allow a hand update, or add a generator. |

### 11.8 Verification

| Check | Result |
|---|---|
| Backend build | Every project builds individually (the solution file still references the missing `LifeSim` project, §11.5). Api and IntegrationTests built into a scratch folder because the running Api locks its `bin`. |
| `dotnet test src/dEngage.Loyalty.Engine.Tests` | 79 passed, 0 failed (2 new: `StreakRewardDefinitionTests`) |
| `dotnet test src/dEngage.Loyalty.IntegrationTests` (incl. E2E, Docker) | 150 passed, 0 failed. New: 28 `RewardCatalogCr0930ApiTests` (matrix, retired values, currency/wallet/decimals, tenant isolation, approval, re-approval, currency lock, slug rules, tier ownership, in-use guards, streak eligibility, `reward_id` validator), 7 `RewardFulfilmentE2ETests` (cashback once on redelivery, by `reward_id`, refused when the program isn't live, pending not sellable, tier upgrade + lock + idempotency, never down, nightly downgrade honours the lock), 1 `StampCompletionCr0930Tests` (O1). |
| `npm test` | 92 passed, 0 failed (11 new in `reward.model.spec.ts`; `program-overview.page.spec.ts` fixture gained `slug`) |
| `npx ng lint` | No findings in changed files; the same 3 pre-existing errors in untouched files. |
| `npx ng build --configuration development` | Succeeded |
| `scripts/loyalty_schema.sql` | Regenerated; applied twice to an empty Postgres 16: both runs OK (idempotent), both `…Cr0930` migrations recorded, `programs.slug` NOT NULL with its default. |
| Data migration on realistic data | Fresh Postgres migrated to `ProgramPublicationCl13`, seeded with edge cases, then migrated to latest: slugs derived and de-duplicated (`fin-tech`, `fin-tech-a00000`, `program-x`, `shop-n-code-co`); stamp / points_bonus / wallet-less cashback / points-bought tier upgrade deactivated; valid rewards kept active and `status = active`; the published program marked changed; a raw-SQL program insert got a default slug. |
| TestCli `RewardTest` | **Builds, not run.** It needs the full local stack with the seeded Starbucks tenant. |
| Manual run (`.\run.ps1`) | Not done. |

### 11.9 Files created / changed in P2–P4 (review record)

| File | Change | Why |
|---|---|---|
| `src/dEngage.Loyalty.Shared/Constants/RewardAcquisition.cs`, `RewardType.cs` | Changed | Creatable vs retired values; compatibility matrix |
| `src/dEngage.Loyalty.Shared/Constants/RewardStatus.cs` | Created | `active` / `pending_approval` (A4) |
| `src/dEngage.Loyalty.Shared/Constants/LedgerReason.cs` | Changed | `reward_cashback` |
| `src/dEngage.Loyalty.Shared/Events/EventTypes.cs` | Changed | `reward_id` field on `reward.purchase` |
| `src/dEngage.Loyalty.Schema/Entities/Program.cs`, `RewardDefinition.cs`, `CustomerAccount.cs` | Changed | `Slug`; `Status`/`CreatedBy`/`ApprovedBy`; `TierLockedUntil` |
| `src/dEngage.Loyalty.Schema/Configurations/ProgramConfiguration.cs`, `RewardDefinitionConfiguration.cs`, `CustomerAccountConfiguration.cs` | Changed | Column mappings; `ux_programs_tenant_slug`; `ux_reward_definitions_tenant_name_active` |
| `src/dEngage.Loyalty.Schema/Migrations/20261001090316_RewardCatalogFulfilmentCr0930.cs` + `.Designer.cs` | Created | Columns, slug backfill + default, reward deactivation, indexes |
| `src/dEngage.Loyalty.Schema/Migrations/LoyaltyDbContextModelSnapshot.cs` | Regenerated by `dotnet ef` | New columns and indexes |
| `src/dEngage.Loyalty.Api/Rewards/RewardTypeRegistry.cs`, `RewardsAppService.cs`, `RewardsDtos.cs`, `RewardsValidators.cs`, `RewardsModule.cs` | Changed | Matrix, retired guard, wallet/currency/tier checks, prefix rule, approval + approve route, in-use guard |
| `src/dEngage.Loyalty.Api/Programs/ProgramsDtos.cs`, `ProgramsValidators.cs`, `ProgramsAppService.cs` | Changed | Program slug (derive, unique, lock after publish); snapshot carries slug + reward status |
| `src/dEngage.Loyalty.Api/StreakCampaigns/StreakCampaignsAppService.cs` | Changed | Streak reward eligibility on create / update / re-enable |
| `src/dEngage.Loyalty.Api/Tiers/TiersAppService.cs` | Changed | `tier_in_use_by_reward` |
| `src/dEngage.Loyalty.Api/Events/EventsDtos.cs`, `EventsValidators.cs` | Changed | Optional `RewardId` on `reward.purchase` |
| `src/dEngage.Loyalty.RuleEngine/Processing/IRewardFulfilmentService.cs`, `RewardFulfilmentService.cs` | Created | Cashback credit and tier upgrade (§3.5, §3.6) |
| `src/dEngage.Loyalty.RuleEngine/Campaigns/Streak/StreakCampaignModule.cs` | Changed | Pays approved rewards through the fulfilment seam |
| `src/dEngage.Loyalty.RuleEngine/Processing/StampCompletionHandler.cs` | Changed | O1: no definition lookup; always announces the completion |
| `src/dEngage.Loyalty.RuleEngine/TierDowngradeJob.cs` | Changed | Honours `tier_locked_until` |
| `src/dEngage.Loyalty.Consumer/Handlers/RewardPurchaseHandler.cs` | Changed | `reward_id`, approval check, O10 gate, cashback credit in the same transaction |
| `src/dEngage.Loyalty.Consumer/Program.cs` | Changed | Registers `IRewardFulfilmentService` |
| `src/dEngage.Loyalty.Engine.Tests/StreakCampaignModuleTests.cs`, `src/dEngage.Loyalty.IntegrationTests/Engine/StreakMonthlySumTests.cs` | Changed | New constructor argument (mocked fulfilment) |
| `src/dEngage.Loyalty.Engine.Tests/StreakRewardDefinitionTests.cs` | Created | Streak → fulfilment wiring |
| `src/dEngage.Loyalty.IntegrationTests/Api/RewardCatalogCr0930ApiTests.cs` | Created | API behaviour (see §11.8) |
| `src/dEngage.Loyalty.IntegrationTests/E2E/RewardFulfilmentE2ETests.cs` | Created | Money paths and the tier lock on Postgres |
| `src/dEngage.Loyalty.IntegrationTests/Engine/StampCompletionCr0930Tests.cs` | Created | O1 |
| `src/dEngage.Loyalty.TestCli/RewardTest.cs` | Changed | RW01 (stamp without definition), RW02/RW03 (cashback reward instead of the retired discount) |
| `scripts/loyalty_schema.sql` | Regenerated (`dotnet ef migrations script --idempotent`) | Schema script in step |
| `web/src/app/features/programs/program.model.ts`, `create-program.dialog.ts`, `program-overview.page.ts` | Changed | Program slug |
| `web/src/app/features/programs/program-overview.page.spec.ts` | Changed | Fixture gains `slug` |
| `web/src/app/features/programs/rewards/reward.model.ts`, `reward-form.dialog.ts`, `rewards-list.page.ts`, `rewards.service.ts` | Changed | New catalog, cashback wallet/currency, tier lock, approval, prefixed names, retired view-only. Prettier also reformatted untouched lines in these files (project rule: run prettier on changed files). |
| `web/src/app/features/programs/rewards/reward.model.spec.ts` | Created | Matrix / retired table tests |
| `web/src/app/features/programs/streak-campaigns/streak-campaign-form.page.ts` | Changed | Eligible-reward picker |
| `web/public/i18n/en.json`, `web/public/i18n/tr.json` | Changed | 49 new keys each |
| `docs/SCOPE_BASELINE.md`, `docs/SOW.md` | Changed | Rows updated; items marked built; revision rows |
| `.claude/rules/01-workflow-and-debugging.md` | Changed | Pitfalls: cashback gating exception; "reward paid nothing" diagnosis |
| `docs/scope-changes/2026-09-30-reward-acquisition-fulfilment.md` | Changed | This record |

Not changed: `LoyaltySaaSApi.md` is still missing, so the API additions (`slug`, reward `status`/`createdBy`/`approvedBy`, `PATCH .../rewards/{id}/approve`, `rewardId`) aren't recorded there. The tenant seed scripts were left untouched (reference data).

### 11.10 Deploy notes

1. Run the pre-deploy queries in §6 per environment and share them with the PO: rewards that will be deactivated, proposed program slugs (published programs' slugs lock immediately), and active reward names shared across programs. Two still-active rewards with the same name in one tenant make the migration fail on purpose.
2. Existing cashback rewards are deactivated (O2). Admins re-create them with a CASH wallet, and a second admin approves them.
3. Integrations that buy a reward by name keep working; new reward names start with the program slug.
4. `loyalty.reward.earned` and `loyalty.tier.changed` gain additive fields; notify consumers that validate a strict schema.

## 12. Addendum A (2026-10-01): points-transfer limit on the account-type screen

**Status:** approved 2026-10-01 by Product Owner + Architect (confirmed by the developer, Moiz); implemented and verified.

### 12.1 What and why

- **Request:** set the points-transfer daily limit from the portal. Until now it could only be set by SQL or the API (`transfer.daily_limit` in the POINTS account type's config), although transfer with a daily cap is in scope (SOW §2.4).
- **Bug found while analysing it (fixed here):** the account-type form rebuilt a wallet's settings from its own fields only, so **every save of a POINTS account type in the portal deleted `transfer`** (and any other setting the form doesn't show). After that, every `points.transfer` failed with `transfer_not_configured`.
- **API gap (fixed here):** the transfer setting wasn't validated. A non-numeric or zero limit was accepted and only failed when a transfer ran.

### 12.2 Product analysis: transfer and redeem for CASH (decision: not offered)

| Option | Assessment |
|---|---|
| Redeem CASH → points | Low value; amounts to buying points, undermines earn rules and point scarcity, adds a second conversion rate to keep consistent. |
| Redeem CASH → bank/card payout | A real need, but a payments feature (provider, KYC/AML, settlement, failures). The tenant's payment system pays out and reports `cash.spent` (or a future `cash.withdrawn`). |
| Transfer CASH between customers | Money transmission / e-money (licensing), high fraud/AML risk (mules, laundering farmed cashback), moves the tenant's liability between customers; cashback is normally non-transferable by T&Cs. |

**Decision:** CASH stays non-transferable and non-redeemable in the loyalty platform (recorded in SOW §3). A licensed tenant that needs cash P2P raises its own Scope Change Request (per-tenant flag, KYC gate via `kyc.completed`, daily/monthly money limits, same currency only, CR-04-style approval, monitoring events). Points transfer remains the supported gifting/pooling mechanism.

### 12.3 What was built

- **API** (`AccountTypeConfigValidators.cs`): POINTS `transfer`, when present, must be an object with a numeric `daily_limit` > 0. CASH and STAMP configs with `transfer` are rejected ("only POINTS can be transferred between customers"). No handler change: `PointsTransferHandler` already enforced the limit.
- **Portal** (`account-type-form.dialog.ts`): for POINTS, an "Allow points transfer between customers" checkbox and a "Daily transfer limit (points)" field (min 1; validated only while enabled). The form now starts from the stored config minus the keys it owns, so settings it doesn't show survive a save. New strings in `en.json` and `tr.json`.
- **Model** (`account-type.model.ts`): `PointsConfig.transfer`.

### 12.4 Verification

| Check | Result |
|---|---|
| `dotnet test src/dEngage.Loyalty.IntegrationTests` (incl. E2E, Docker) | 162 passed, 0 failed. New: 9 `AccountTypeTransferConfigApiTests` (valid limit, 5 invalid shapes, CASH and STAMP refused, change and remove on an existing wallet), 2 `PointsTransferLimitE2ETests` (transfers up to exactly the limit succeed and the next is refused with `daily_limit_exceeded`; a wallet without the setting refuses with `transfer_not_configured`). |
| `dotnet test src/dEngage.Loyalty.Engine.Tests` | 79 passed (built into a scratch folder; the running Consumer locks its own output). |
| `npm test` | 96 passed (4 new). The save regression test was verified to **fail without the fix** (temporarily removed, then restored). |
| `npx ng lint` / `npx ng build --configuration development` | No findings in changed files (same 3 pre-existing errors elsewhere) / succeeded. |
| Manual check in the running portal | Not done here. The Api must be restarted to pick up the validator change. |

### 12.5 Files

| File | Change | Why |
|---|---|---|
| `src/dEngage.Loyalty.Api/AccountTypes/AccountTypeConfigValidators.cs` | Changed | Transfer validation; CASH/STAMP refuse it |
| `web/src/app/features/programs/account-types/account-type-form.dialog.ts` | Changed | Transfer checkbox + limit; keep unknown settings on save |
| `web/src/app/features/programs/account-types/account-type.model.ts` | Changed | `PointsConfig.transfer` |
| `web/src/app/features/programs/account-types/account-type-form.dialog.spec.ts` | Changed | 4 tests incl. the save regression |
| `web/public/i18n/en.json`, `web/public/i18n/tr.json` | Changed | 5 keys each |
| `src/dEngage.Loyalty.IntegrationTests/Api/AccountTypeTransferConfigApiTests.cs` | Created | API validation tests |
| `src/dEngage.Loyalty.IntegrationTests/E2E/PointsTransferLimitE2ETests.cs` | Created | Limit enforced end to end |
| `docs/SCOPE_BASELINE.md`, `docs/SOW.md` | Changed | AccountTypes rows, SOW §2.2 and §3, revision rows |
| `docs/scope-changes/2026-09-30-reward-acquisition-fulfilment.md` | Changed | This section |
