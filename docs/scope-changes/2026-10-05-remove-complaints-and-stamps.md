# Scope Change Impact Analysis: Remove Complaints, Stamps and the `points.expired` rule trigger

**Date:** 2026-10-05 (revision 4)
**Status:** **Approved** by Product Owner + Architect (confirmed by the developer, 2026-10-05). **P1–P4 implemented** on branch `1.6-remove-complaints-stamps-expiryrule` — see §15 for the implementation record, deviations and verification. **Addendum A (birthday bonus removal): approved by all stakeholders 2026-10-06 and implemented — see A.13.**
**Type:** Scope change (removes capabilities). Jira Scope Change Request: _to be raised_.
**Basis:** code read on 2026-10-05 across Api, Api.Framework, RuleEngine, Ledger, Consumer, Schema, Shared, tests, TestCli, portal (`web/`), seeds and docs. Nothing was built or run.

---

## 1. Summary

Three capabilities are removed from the product:

1. **Complaints.** The whole module goes: the API resource, the portal page and sidebar entry, the dashboard breakdown and the `complaints` table.
2. **Stamps.** That covers the STAMP account type (wallet), the `StampRule` rule type, stamp-card completion (reset plus the `loyalty.reward.earned` announcement), the STAMP target on ManualAdjustment rules, and the last stamp traces in Rewards (`stampAccountTypeId` and its database column, `stamp_completion`).
3. **The `points.expired` rule trigger and the `ExpiryRule` rule type.** These are offered on the rule definition screen but have never worked: nothing publishes `points.expired`, so an ExpiryRule can never fire. **Points expiry itself is not touched.** That covers the expiration days and "expiring soon" warning days on the POINTS account type, the nightly `PointsExpirationJob` and expiring-soon detector, the `points_expired` ledger reason, and the outbound `loyalty.points.expired` event the client application receives.

These are not the same kind of removal:
- **Complaints** has no ties to money or the ledger, so it is a **hard removal** that drops its table.
- **Stamps** have ledger history, and ledger rows are append-only (guardrail §2), so stamps are **retired**. Nothing new can be created, configured or earned, and STAMP wallets disappear from the account-type screens. Existing STAMP wallets, balances, ledger entries and reward-log rows stay in the database and remain readable as customer history in Customer 360.
- **`points.expired` / ExpiryRule** never posted anything, so their removal has no ledger or customer effect. It only takes a non-working option off the rule screen.

**Reviewed and kept (not part of this change):** the `signup` and `birthdaybonus` triggers and `kyc.completed`. See §2 D13–D14 and §11.

## 2. Decisions agreed with the developer (2026-10-05)

| # | Decision |
|---|---|
| D1 | **Stamps are retired; history is kept.** All create, edit and earn paths are removed. A migration disables every rule that is a `StampRule` or targets a STAMP wallet. Existing STAMP account types, customer accounts and balances, `stamp_earn`/`stamp_reset` ledger rows and stamp `reward_log` rows are kept. Follows the CR 2026-09-30 retirement pattern. |
| D2 | **The `complaints` table is dropped by migration.** The entity, configuration and `DbSet` are removed. |
| D3 | **Breaking API changes are made outright, not through a deprecation window.** The `/complaints` endpoints, `DashboardSummaryResponse.complaints` and the reward `stampAccountTypeId` field are removed. `STAMP`, `StampRule` and `stamp_completion` are no longer accepted on any write. They are documented as breaking in `LoyaltySaaSApi.md`. |
| D4 | **Also in scope:** stopping the stamp-completion `loyalty.reward.earned` publication, the seed SQL scripts, the TestCli scenarios, and superseded notes on the earlier CR docs. |
| D5 (Q1) | **Historical `stamp_earn` stays in the tier and cap calculations.** `TierEvaluationService`, `TierDowngradeJob`, `RuleLimitEvaluator` and `LimitCacheService` are unchanged. |
| D6 (Q2) | **A refund still reverses stamps earned before retirement.** `RefundService` and `ReversalRuleProcessor` are unchanged. The reversal is a compensating entry on the dormant wallet. |
| D7 (Q3) | **`reward_definitions.stamp_account_type_id` is dropped**, together with its FK, `IX_reward_definitions_stamp_account_type_id` and `ux_reward_definitions_active_stamp`. Retired `stamp_completion` reward rows lose their link to the STAMP wallet; `acquisition = 'stamp_completion'` itself stays as their history marker. |
| D8 (Q4) | **STAMP wallets are hidden from the account-type list.** The list endpoint and every account-type picker exclude them. Customer 360 still shows stamp balances and history. |
| D9 (Q5) | **No export of complaints data.** Data loss on drop is accepted. |
| D10 (Q7) | **No compensation** for customers holding partial stamp cards. |
| D11 (Q8) | **The dashboard complaint card is removed and the layout collapses.** Nothing replaces it in this CR. |
| D12 (Q6) | **The portal is the only client of the Loyalty REST API**, so D3's outright breaks need no deprecation window; the portal ships in the same release. **The client-side application listens to the outbox events**, so the client app team is told before P3 ships that `loyalty.reward.earned` with `source = stamp_completion` stops. Every other outbound event, including `loyalty.reward.earned` for streak and purchase rewards, is unchanged. |
| D13 (Q9) | **`signup` stays as it is.** A signup-triggered rule (for example a FixedBonusRule) is the only way the platform can pay a fixed signup bonus; no REST endpoint credits a bonus directly. Signup also feeds "within N hours of signup" conditions, such as KYC-within-1-hour cashback (`agg.hoursSince.signup`, read from `event_log`). The known "once per customer is not enforced" gap stays with the rule-config CR (`2026-10-05-rule-config-limits-by-trigger.md`). |
| D14 (Q10, Q11) | **`birthdaybonus` stays as it is**: the trigger, `BirthdayBonusJob` / `BirthdayBonusWorker`, `POST customers/{ref}/birthday` and the `customer_birthdays` table. **`kyc.completed` stays as it is.** |
| D15 (Q12) | **Points expiry on the wallet stays exactly as is.** The `points.expired` trigger, `PointsExpiredHandler` and the `ExpiryRule` type and handler are removed from rules (API, catalog, engine, rule form). Existing ExpiryRule / `points.expired` rules and streak campaigns are disabled, not deleted. As in D3, the break is made outright: `ExpiryRule` and the `points.expired` trigger are no longer accepted on any write, and the `ExpiryRule` constant stays as a retired value so existing rows still load. |

**How D1 and D3 fit together.** "Remove the enum value" means *rejected on write*. It does not mean *unreadable*. `account_types.type`, `rules.type` and `reward_definitions.acquisition` are strings, so existing rows still load. The constants stay as **retired** values, in the same way `RewardAcquisition.StampCompletion` is kept today. Because of D8, the main place a client still sees `"STAMP"` is in Customer 360 wallet data; disabled stamp rules still show their type in the rules list.

**Two different "points expired" names.** They must not be confused during implementation:
- **Inbound `points.expired`** (`EventTypes.PointsExpired`) is a rule trigger that nothing ever publishes. It is **removed**.
- **Outbound `loyalty.points.expired`** (`OutboundEventTypes.PointsExpired`) is sent to the client app by `PointsExpirationJob` when points actually expire. It is **kept**. The `points_expired` ledger reason (`LedgerReason.PointsExpired`) is also kept.

## 3. Baseline rows touched (`docs/SCOPE_BASELINE.md`)

| Row | Change |
|---|---|
| AccountTypes (§2.2) | STAMP removed from creatable types; existing STAMP wallets hidden from the list, not editable. POINTS expiry and warning days unchanged. |
| Complaints (§2.5) — API + portal rows | Removed |
| Rewards (§2.2) | `stampAccountTypeId` removed from the contract and the database; `stamp_completion` stays retired (history only) |
| Rules (§2.2) | 8 → 6 rule types (StampRule and ExpiryRule removed); STAMP removed from rule targets; `points.expired` no longer a trigger |
| Rule type handlers (§2.2) | `StampRuleHandler` and `ExpiryRuleHandler` removed |
| Event taxonomy (§2.3, l.106 "14 built-ins") | 14 → 13 built-in event types (`points.expired` removed) |
| Consumer (l.55) | `PointsExpiredHandler` removed from the per-event-type handlers |
| Ledger posting / stamp completion (§2.3, §2.7) | Stamp completion removed; no more `stamp_completion` `loyalty.reward.earned` |
| dashboard (portal) | Complaint breakdown removed |
| customers (§2.4) | Stamp history still shown (wallets, ledger reasons, rewards tab); no new stamp data |
| events (portal, l.74) | Simulator note updated: only `birthdaybonus` is a hidden scheduled type |

`docs/SOW.md` sections affected:
- §2.2 Account Types, Rules and Rewards
- §2.3: the event list (l.73–75, `points.expired` removed) and the pipeline step "stamp-completion"
- §2.5 Complaints (removed) and §2.6 dashboard complaint breakdown
- the event simulator note (l.94)
- the §3 notes "Complaints is lightweight" and "STAMP account-type config has no fixed schema" (both removed)

---

## 4. Current state (as-built, verified by code read)

### 4.1 Complaints

| Layer | Where |
|---|---|
| API | [Complaints/](../../src/dEngage.Loyalty.Api/Complaints/): `ComplaintsModule` (GET list, GET `/summary`, GET `/{id}`, POST, PATCH `/{id}/status`), AppService, Dtos, Validators; registered in [Program.cs](../../src/dEngage.Loyalty.Api/Program.cs) (l.55 service, l.78 module) |
| Dashboard | [DashboardAppService.cs](../../src/dEngage.Loyalty.Api/Dashboard/DashboardAppService.cs) injects `IComplaintsAppService` and calls `SummaryAsync`; [DashboardDtos.cs](../../src/dEngage.Loyalty.Api/Dashboard/DashboardDtos.cs) carries `ComplaintSummaryResponse Complaints` |
| Schema | [Complaint.cs](../../src/dEngage.Loyalty.Schema/Entities/Complaint.cs), [ComplaintConfiguration.cs](../../src/dEngage.Loyalty.Schema/Configurations/ComplaintConfiguration.cs), `DbSet<Complaint>` in [LoyaltyDbContext.cs](../../src/dEngage.Loyalty.Schema/LoyaltyDbContext.cs); table created by `20260905184227_AddComplaints` |
| Shared | [ComplaintStatus.cs](../../src/dEngage.Loyalty.Shared/Constants/ComplaintStatus.cs) |
| Portal | [features/complaints/](../../web/src/app/features/complaints/) (model, list page, routes, service); route in [app.routes.ts](../../web/src/app/app.routes.ts); sidebar entry `nav.complaints` in [sidebar.ts](../../web/src/app/layout/sidebar/sidebar.ts); dashboard card in [dashboard.page.ts](../../web/src/app/features/dashboard/dashboard.page.ts) + `ComplaintSummary` in [dashboard.model.ts](../../web/src/app/features/dashboard/dashboard.model.ts); i18n keys in `web/public/i18n/en.json` / `tr.json` |
| Tests | `Phase4FeaturesSmokeTests.Complaint_lifecycle_*`, `DashboardEndpointE2ETests.Dashboard_summary_reflects_created_programs_and_complaints` |
| Docs | `LoyaltySaaSApi.md` §Complaints + dashboard summary, `scripts/loyalty_schema_reference.md`, SOW §2.5, baseline |

No engine, consumer, event or ledger code touches complaints. The one match in [CachedCampaignConfig.cs](../../src/dEngage.Loyalty.RuleEngine/Campaigns/CachedCampaignConfig.cs) is the English word in a comment.

### 4.2 Stamps

**Write paths, which are removed:**

| Layer | Where | What it does |
|---|---|---|
| API: account types | [AccountTypesValidators.cs](../../src/dEngage.Loyalty.Api/AccountTypes/AccountTypesValidators.cs) (accepts `STAMP`), [AccountTypeConfigValidators.cs](../../src/dEngage.Loyalty.Api/AccountTypes/AccountTypeConfigValidators.cs) `StampAccountTypeConfigValidator` (free-form JSON), registered in Api `Program.cs` l.62; list `GET account-types` in the AccountTypes module | Create/edit/list STAMP wallets |
| API: rules | `RuleTypes.StampRule` in [RuleTypes.cs](../../src/dEngage.Loyalty.Shared/Constants/RuleTypes.cs) `All`; [RuleTypeCatalog.cs](../../src/dEngage.Loyalty.RuleEngine/Metadata/RuleTypeCatalog.cs) — `StampRule` → STAMP, `ManualAdjustmentRule` → POINTS/CASH/**STAMP**; served by `GET rules/metadata` | Configure stamp rules / stamp adjustments |
| API: rewards | `StampAccountTypeId` on the 3 reward DTOs ([RewardsDtos.cs](../../src/dEngage.Loyalty.Api/Rewards/RewardsDtos.cs)), validator rules ([RewardsValidators.cs](../../src/dEngage.Loyalty.Api/Rewards/RewardsValidators.cs) l.29–43), AppService guard (l.93) and mapping (l.70, l.308); `PublishedReward` snapshot in [ProgramsDtos.cs](../../src/dEngage.Loyalty.Api/Programs/ProgramsDtos.cs) l.40 / [ProgramsAppService.cs](../../src/dEngage.Loyalty.Api/Programs/ProgramsAppService.cs) l.208 | Already rejected unless null; contract field only |
| Schema: rewards | `RewardDefinition.StampAccountTypeId` + `StampAccountType` navigation ([RewardDefinition.cs](../../src/dEngage.Loyalty.Schema/Entities/RewardDefinition.cs) l.14, l.28); column, FK and two indexes in [RewardDefinitionConfiguration.cs](../../src/dEngage.Loyalty.Schema/Configurations/RewardDefinitionConfiguration.cs) l.23, l.39–41, l.54–58 | Link a stamp reward to its wallet (no live use since CR 2026-09-30) |
| Engine | [StampRuleHandler.cs](../../src/dEngage.Loyalty.RuleEngine/Calculation/StampRuleHandler.cs) (+1 per positive-amount event), [LedgerPoster.cs](../../src/dEngage.Loyalty.RuleEngine/Processing/LedgerPoster.cs) (l.47 `isStamp`, l.123 completion hook, l.271 `StampRule → stamp_earn`), [StampCompletionHandler.cs](../../src/dEngage.Loyalty.RuleEngine/Processing/StampCompletionHandler.cs) + [IStampCompletionHandler.cs](../../src/dEngage.Loyalty.RuleEngine/Processing/IStampCompletionHandler.cs) (posts `stamp_reset`, writes `RewardLog`, enqueues `loyalty.reward.earned` with `source = stamp_completion`) | Earn stamps, complete cards, announce completion |
| Consumer | [Program.cs](../../src/dEngage.Loyalty.Consumer/Program.cs) l.71 (`StampRuleHandler`), l.84 (`StampCompletionHandler`) | DI registration |
| Portal | [account-type-form.dialog.ts](../../web/src/app/features/programs/account-types/account-type-form.dialog.ts) (STAMP option + JSON config editor), [account-type.model.ts](../../web/src/app/features/programs/account-types/account-type.model.ts) `AccountTypeKind`, [rule-form.page.ts](../../web/src/app/features/programs/rules/rule-form.page.ts) (`StampRule` case l.510, l.1077), [rule.model.ts](../../web/src/app/features/programs/rules/rule.model.ts), [reward.model.ts](../../web/src/app/features/programs/rewards/reward.model.ts) `stampAccountTypeId` | Configure stamps |

**Read and calculation paths over historical stamp data, which are kept (D5, D6):**

| Where | Uses `stamp_earn` / `stamp_reset` / `stamp_completion` for |
|---|---|
| [TierEvaluationService.cs](../../src/dEngage.Loyalty.RuleEngine/TierEvaluationService.cs) l.151, [TierDowngradeJob.cs](../../src/dEngage.Loyalty.RuleEngine/TierDowngradeJob.cs) l.94 | Tier qualifying points (summed per *tier-qualifying* account — POINTS only since 1.3.CL) |
| [RuleLimitEvaluator.cs](../../src/dEngage.Loyalty.RuleEngine/Processing/RuleLimitEvaluator.cs), [LimitCacheService.cs](../../src/dEngage.Loyalty.RuleEngine/Cache/LimitCacheService.cs) | Per-rule cap usage (inert once no StampRule is active) |
| [RefundService.cs](../../src/dEngage.Loyalty.Ledger/RefundService.cs) l.53/101, [ReversalRuleProcessor.cs](../../src/dEngage.Loyalty.RuleEngine/Processing/ReversalRuleProcessor.cs) l.51/79 | Reversing an earlier stamp earning on `order.refunded` (whole stamps only) |
| [LedgerReasonGroups.cs](../../src/dEngage.Loyalty.Api/Customers/LedgerReasonGroups.cs), [CustomerViewRules.cs](../../src/dEngage.Loyalty.Api/Customers/CustomerViewRules.cs), [CustomersAppService.cs](../../src/dEngage.Loyalty.Api/Customers/CustomersAppService.cs) l.690–748 | Customer 360 ledger filters, cap usage, Rewards tab (stamp completions from `reward_log`; does not read `stamp_account_type_id`) |
| Portal [customer.model.ts](../../web/src/app/features/customers/customer.model.ts), [customer-icon.ts](../../web/src/app/features/customers/customer-icon.ts), [customer-rewards.tab.ts](../../web/src/app/features/customers/customer-rewards.tab.ts) | Rendering historical stamp wallets, reasons and rewards |
| Shared | `LedgerReason.StampEarn/StampReset`, `RewardAcquisition.StampCompletion` (retired constants) |

### 4.3 `points.expired` trigger and `ExpiryRule`

**Why it doesn't work today.** `ExpiryRuleHandler` only negates an amount that the publisher of `points.expired` would have to compute from point lots. No publisher was ever built: the follow-up to reroute `PointsExpirationJob` through ingestion was never done. `PointsExpirationJob` posts the expiry directly to the ledger instead. The trigger is also Scheduled-source, so `POST /events` refuses it. Both the handler comments ([PointsExpiredHandler.cs](../../src/dEngage.Loyalty.Consumer/Handlers/PointsExpiredHandler.cs), [ExpiryRuleHandler.cs](../../src/dEngage.Loyalty.RuleEngine/Calculation/ExpiryRuleHandler.cs)) state that they are unreachable in production. An admin can still pick `points.expired` and `ExpiryRule` on the rule screen and save a rule that never fires.

**Removed:**

| Layer | Where |
|---|---|
| Shared | `EventTypes.PointsExpired` in `All` and `Catalog` ([EventTypes.cs](../../src/dEngage.Loyalty.Shared/Events/EventTypes.cs) l.47, l.53, l.151); `RuleTypes.ExpiryRule` in `All` ([RuleTypes.cs](../../src/dEngage.Loyalty.Shared/Constants/RuleTypes.cs) l.19, l.25) |
| API | [RulesValidators.cs](../../src/dEngage.Loyalty.Api/Rules/RulesValidators.cs) l.59 (type list), l.84–89 (`ageDays` / `order` checks); `GET events/types` `builtIn` list (feeds the rule and streak trigger pickers); comments in [EventsAppService.cs](../../src/dEngage.Loyalty.Api/Events/EventsAppService.cs) l.40 and [EventsDtos.cs](../../src/dEngage.Loyalty.Api/Events/EventsDtos.cs) l.28–30 |
| Engine | [ExpiryRuleHandler.cs](../../src/dEngage.Loyalty.RuleEngine/Calculation/ExpiryRuleHandler.cs); `ExpiryRule` entry in [RuleTypeCatalog.cs](../../src/dEngage.Loyalty.RuleEngine/Metadata/RuleTypeCatalog.cs) l.47; `ExpiryRule → points_expired` mapping in [LedgerPoster.cs](../../src/dEngage.Loyalty.RuleEngine/Processing/LedgerPoster.cs) l.273; `AgeDays` / `Order` in [RuleCalculation.cs](../../src/dEngage.Loyalty.RuleEngine/Models/RuleCalculation.cs) l.41–46 |
| Consumer | [PointsExpiredHandler.cs](../../src/dEngage.Loyalty.Consumer/Handlers/PointsExpiredHandler.cs); DI in [Program.cs](../../src/dEngage.Loyalty.Consumer/Program.cs) l.76 (`ExpiryRuleHandler`), l.125 (`PointsExpiredHandler`) |
| Portal | [rule-form.page.ts](../../web/src/app/features/programs/rules/rule-form.page.ts) `ExpiryRule` case (l.457–478), `calcAgeDays` control (l.799–800, l.984, l.1043), payload mapping (l.1095–1096); [rule.model.ts](../../web/src/app/features/programs/rules/rule.model.ts) l.18, l.32–33; `SCHEDULED_EVENT_TYPES` in [event-simulator.page.ts](../../web/src/app/features/events/event-simulator.page.ts) l.26 and the comment in [event-types.service.ts](../../web/src/app/core/events/event-types.service.ts) l.10–12 |
| Tests | [BurnTriggerRuleTypesApiTests.cs](../../src/dEngage.Loyalty.IntegrationTests/Api/BurnTriggerRuleTypesApiTests.cs) l.105–106; `ExpiryRuleHandler` in [RuleEngineTestHarness.cs](../../src/dEngage.Loyalty.IntegrationTests/Fixtures/RuleEngineTestHarness.cs) l.65 |

**Kept: wallet expiry (not referenced by any of the above):**

| What | Where |
|---|---|
| Expiration days + expiring-soon warning days on the POINTS account type | AccountType config + [AccountTypeConfigValidators.cs](../../src/dEngage.Loyalty.Api/AccountTypes/AccountTypeConfigValidators.cs), account-type form |
| Nightly expiry (FIFO lots) posting `points_expired` and sending outbound `loyalty.points.expired` | [PointsExpirationJob.cs](../../src/dEngage.Loyalty.Ledger/PointsExpirationJob.cs) l.204–216, `PointsExpirationWorker` |
| Expiring-soon warning to the client app | [PointsExpiringDetectorJob.cs](../../src/dEngage.Loyalty.Ledger/PointsExpiringDetectorJob.cs), `PointsExpiringDetectorWorker` |
| Ledger reason and outbound type | `LedgerReason.PointsExpired`, `OutboundEventTypes.PointsExpired` |
| Customer 360 expiry display | `LedgerReasonGroups`, `CustomerViewRules`, `customer.model.ts` (`points_expired`) |
| Tests and TestCli | `PointsExpirationJobE2ETests`, `CustomerViewCr1002ApiTests`, TestCli `ExpireTest` / `ExpireJobVerify` (all exercise wallet expiry, not ExpiryRule) |

---

## 5. Proposed change

### 5.1 Complaints: hard removal

- **API:** delete `src/dEngage.Loyalty.Api/Complaints/` (4 files) and both registrations in Api `Program.cs`. Remove `Complaints` from `DashboardSummaryResponse` and the `IComplaintsAppService` dependency in `DashboardAppService`. `/complaints/*` now returns 404.
- **Schema:** delete `Complaint`, `ComplaintConfiguration` and the `DbSet`. Add a new migration `RemoveComplaintsCrNN` (`DropTable("complaints")`, with a `Down` that recreates the empty table). Delete `ComplaintStatus`. No export beforehand (D9).
- **Portal:** delete `features/complaints/`. Remove the route, the sidebar item, the dashboard card (layout collapses, D11) and `ComplaintSummary`, and the `complaints` / `nav.complaints` i18n keys (en and tr).
- **Tests:** remove the two complaint tests. The dashboard E2E test keeps its program and tier assertions; its complaint assertion is removed. Add an assertion that `GET /complaints` returns 404.

### 5.2 Stamps: retirement

**Migration `RetireStampsCrNN`.** Generated with `dotnet ef migrations add` (it contains a schema change), with SQL steps added at the top of `Up`, following the pattern in `20261001083819_BurnTriggerRuleTypesCr0930`:
1. Mark published programs that have an affected rule as `has_unpublished_changes = true`. This runs first.
2. Set `rules.status = 'disabled'` where `type = 'StampRule'` **or** `target_account_type_id` is a STAMP account type, which catches ManualAdjustment rules aimed at a STAMP wallet. Rules are disabled, not deleted, and rule versions are untouched (guardrail §2, rule versioning).
3. Schema (D7): drop index `ux_reward_definitions_active_stamp`, FK `FK_reward_definitions_account_types_stamp_account_type_id`, index `IX_reward_definitions_stamp_account_type_id`, then column `reward_definitions.stamp_account_type_id`.
4. `Down` restores the column, FK and indexes as empty/nullable. It cannot restore the dropped values or re-enable rules (same reasoning as the CR 2026-09-30 migration).
5. No change to `account_types`, `customer_accounts`, `ledger_entries` or `reward_log`.

**API:**
- **Account types:**
  - `AccountTypesValidators`: `Type` must be `POINTS` or `CASH`, otherwise `account_type_retired`. Delete `StampAccountTypeConfigValidator` and its registration.
  - **List (D8):** `GET account-types` excludes `type = 'STAMP'`, so they disappear from the account-type screen and every picker (rule target, reward wallet, tier-qualifying).
  - `GET account-types/{id}` on a STAMP wallet still returns it, so historical references resolve. `PATCH` on it → `account_type_retired`.
- **Rules:**
  - `RuleTypes`: remove `StampRule` from `All` but keep the constant, marked retired.
  - Rule create/edit/approve, and a status change to active, on a `StampRule` or a STAMP-targeted rule → `rule_type_retired`.
  - `RuleTypeCatalog`: drop the `StampRule` entry and STAMP from ManualAdjustment targets, so `GET rules/metadata` no longer offers them.
- **Rewards:**
  - Remove `StampAccountTypeId` from the 3 DTOs and from `PublishedReward`, along with the validator lines and the AppService guard and mappings.
  - Remove `RewardDefinition.StampAccountTypeId` and the `StampAccountType` navigation, plus their configuration lines.
  - Older published `ConfigVersion` JSON snapshots keep the field; they are historical text and are left alone.
- **Other:**
  - Map the new codes' prefixes in `DomainErrorTranslator` if they need it.
  - `AccountType` C# enum in Shared: remove `STAMP` if nothing references it (to verify at implementation time). The DB column is a string, so stored rows are unaffected.

**Engine/Consumer:**
- Delete `StampRuleHandler`, `StampCompletionHandler` and `IStampCompletionHandler`, and their 2 registrations in Consumer `Program.cs`.
- In `LedgerPoster`, remove the `isStamp` branch, the completion call, the constructor parameter and the `StampRule → stamp_earn` mapping. Remove the stamp note in `DelayedPostingPromotionJob`.
- This ends the `loyalty.reward.earned` publication with `source = stamp_completion` (D4). The outbound event type itself stays, because streak and purchase rewards still use it.
- Read paths in §4.2 (tier, caps, refund/reversal) are **kept unchanged** (D5, D6).

**Portal:**
- **Account types:**
  - Remove the STAMP option and the JSON config editor from the account-type form.
  - `AccountTypeKind` keeps `'STAMP'` only so Customer 360 can render historical wallets. The account-type list needs no change, because the API no longer returns STAMP rows (D8).
- **Rules:**
  - Remove the `StampRule` case from the rule form and from the creatable rule-type list.
  - Disabled stamp rules still appear in the rules list. Their target wallet is now hidden, so the list must show a fallback label (for example "Retired wallet") instead of a blank or an error when the target can't be resolved from the account-type list.
  - The rule builder already reads `rules/metadata`, so the type and target disappear from it automatically.
- **Rewards:** remove `stampAccountTypeId` from `reward.model.ts`.
- **Customer 360:** unchanged; it keeps rendering historical stamp wallets and rewards.

**Seeds and TestCli (D4):**
- `starbucks_loyalty.sql`: remove the STAMP account type block (§2c) and the stamp rule (l.301).
- `fintech_loyalty.sql`: remove `stamp_account_type_id` from the two `reward_definitions` inserts (l.311, l.332) and their values (D7).
- `loyalty_schema.sql`: regenerate from migrations (idempotent script).
- TestCli: remove the stamp and complaint scenarios. This includes the `stamp_account_type_id` column in the [RewardTest.cs](../../src/dEngage.Loyalty.TestCli/RewardTest.cs) l.262 insert. Several TestCli files already have uncommitted local edits, so those must be reconciled first.

### 5.3 `points.expired` trigger and `ExpiryRule`: removal from rules (D15)

**Migration `RetireExpiryRuleCrNN`.** SQL only, same pattern as §5.2 steps 1–2. It can be folded into `RetireStampsCrNN` if P2 and P4 ship together.
1. Mark published programs that have an affected rule or streak campaign as `has_unpublished_changes = true`.
2. Set `rules.status = 'disabled'` where `type = 'ExpiryRule'` **or** `trigger = 'points.expired'`. These rules could never fire, so no customer-visible behaviour changes.
3. Set `streak_campaigns.status = 'disabled'` where `trigger = 'points.expired'`. The streak trigger is a free string (`StreakCampaignsValidators` only checks it isn't empty), so such campaigns may exist, and they never progressed.
4. `Down` is a no-op.

**Shared:**
- Remove `PointsExpired` from `EventTypes.All` and `EventTypes.Catalog`, and delete the `EventTypes.PointsExpired` constant. Nothing ever published it, so no `event_inbox` or `event_log` rows carry it.
- Keep `OutboundEventTypes.PointsExpired` and `LedgerReason.PointsExpired`, which wallet expiry uses.
- `RuleTypes`: remove `ExpiryRule` from `All`, and keep the constant marked retired.

**API:**
- **Rules:**
  - Creating or editing a rule with type `ExpiryRule` → `rule_type_retired`.
  - A rule with trigger `points.expired` → 400, because the trigger is no longer a built-in or generic type. This is the existing unknown-trigger path.
  - Activating a disabled ExpiryRule is refused the same way as a StampRule.
  - Remove the `ageDays` / `order` validators (l.84–89) and `ExpiryRule` from the type list at l.59.
- **Events:**
  - `GET events/types` no longer lists `points.expired` in `builtIn`, so it disappears from the rule and streak trigger pickers. `birthdaybonus` stays and is still left out of `publishable`.
  - `POST /events/generic` with `points.expired` is still refused, now as "not a built-in type" instead of "scheduled internally".
  - Update the comments in `EventsAppService` and `EventsDtos`.

**Engine/Consumer:**
- Delete `ExpiryRuleHandler` and `PointsExpiredHandler`, and their DI registrations (Consumer `Program.cs` l.76, l.125).
- Remove the `ExpiryRule` catalog entry and the `ExpiryRule → points_expired` mapping in `LedgerPoster`.
- Remove `AgeDays` / `Order` from `RuleCalculation`. Older rule versions store them in their calculation JSON; deserialization ignores the unknown properties, so those rows still load.
- `ExpiryRule` was the only rule type wired for the `Adjust` category besides `ManualAdjustmentRule`. `points.adjusted` and ManualAdjustment are unchanged.

**Portal:**
- **Rule form:** remove the `ExpiryRule` case, the `calcAgeDays` / `calcOrder` controls and the payload mapping. Remove `'ExpiryRule'`, `ageDays` and `order` from `rule.model.ts`. The trigger list comes from `events/types`, so `points.expired` disappears from it automatically.
- **Event simulator:** `SCHEDULED_EVENT_TYPES` becomes `['birthdaybonus']`, and the `event-types.service.ts` comment is updated.
- **Rules list:** disabled ExpiryRule rules show as disabled, with their type label kept.
- **Account-type form:** the POINTS expiry and warning-day fields are **unchanged**.

**Docs to correct beyond the baseline and SOW:** `LoyaltySaaSApi.md` (rule `type` list l.120, the `events/types` note), and `.claude/rules/backend-*.md` and `01-workflow-and-debugging.md` (the pitfall listing `points.expired` among double-evaluated events) wherever they name `points.expired` or ExpiryRule. The in-flight `2026-10-05-rule-config-limits-by-trigger.md` (group "E. Adjust") gets a cross-reference note that `points.expired` / Expiry is removed by this CR.

---

## 6. End-to-end impact (blast radius)

| Area | Complaints | Stamps | `points.expired` / ExpiryRule |
|---|---|---|---|
| DB migrations | 1 (drop table) — **destructive**, no export (D9) | 1: disable rules (SQL) + drop `reward_definitions.stamp_account_type_id`, FK and 2 indexes — **destructive** for that column (D7) | 1, SQL only: disable ExpiryRule / `points.expired` rules and streak campaigns (can be merged with the stamp migration) |
| API endpoints removed | 5 (`/complaints/*`) | none | none |
| API contract breaks | `DashboardSummaryResponse.complaints` removed | `stampAccountTypeId` removed from reward request/response + publish snapshot; `STAMP`/`StampRule` rejected on write; `GET account-types` no longer returns STAMP; `rules/metadata` shrinks | `ExpiryRule` and trigger `points.expired` rejected on write; `ageDays`/`order` removed from rule `calculation`; `events/types.builtIn` loses `points.expired`; `rules/metadata` shrinks |
| Engine / Consumer | none | handler + completion removed; `LedgerPoster` simplified | `ExpiryRuleHandler`, `PointsExpiredHandler` removed (both unreachable today) |
| Outbound events | none | `loyalty.reward.earned` with `source=stamp_completion` no longer published | **none** — `loyalty.points.expired` and the expiring-soon warning are unchanged |
| Ledger | none | no new `stamp_earn`/`stamp_reset`; history untouched | none (ExpiryRule never posted; wallet expiry keeps posting `points_expired`) |
| Redis caches | none | rule cache must resync so disabled stamp rules drop out (`RuleSyncService` / Consumer restart) | same resync (covers both) |
| Portal | 1 feature folder, route, sidebar, dashboard card, i18n | account-type form, rule form (+ fallback label for a hidden target), reward model; Customer 360 rendering kept | rule form ExpiryRule section, rule model, event simulator constant |
| Tests | 2 tests trimmed | `StampRuleHandler` unit tests, `StampCompletionCr0930Tests`, `RuleTypeHandlerRegistryTests`, `RuleEngineTestHarness`, `StackingResolutionWorkedExampleTests` (stamp leg of the 480-point worked example), `AccountTypeTransferConfigApiTests` (STAMP case), `RewardCatalogCr0930ApiTests` (still asserts `stamp_completion` rejected, which stays valid; drop the `StampAccountTypeId` argument) | `BurnTriggerRuleTypesApiTests` l.105–106 (now assert `points.expired` absent from `builtIn`, `birthdaybonus` still present and not publishable); `RuleEngineTestHarness` l.65 |
| Seeds / TestCli | complaint scenarios | `starbucks_loyalty.sql`, `fintech_loyalty.sql`, regenerated `loyalty_schema.sql`, TestCli stamp scenarios + `RewardTest.cs` insert | none found that create ExpiryRule or `points.expired` rules (TestCli expiry scenarios test wallet expiry and stay) |
| Docs | baseline, SOW, `LoyaltySaaSApi.md`, `loyalty_schema_reference.md` (remove `complaints`) | baseline, SOW, `LoyaltySaaSApi.md`, `loyalty_schema_reference.md` (remove `stamp_account_type_id`, its FK and the 2 indexes from `reward_definitions`; note STAMP retired), `backend-*.md` rules if they mention stamps, superseded notes on CRs 2026-09-17 / 09-22 / 09-28 / 09-30 / 10-02 | baseline, SOW, `LoyaltySaaSApi.md`, `.claude/rules` pitfall text, superseded note on CR 2026-09-22 (CR-01/CR-02 introduced them), cross-reference in the rule-config CR |

**Tests for removed features.** Guardrail §7 says never to delete a test to make something pass. Here the tests go away because the behaviour they cover is removed. Each one is replaced by a test that asserts the rejection or absence, so it fails if the removed path comes back:
- creating a STAMP account type → 400 `account_type_retired`
- `GET account-types` doesn't list an existing STAMP wallet
- creating or activating a `StampRule` → 400 `rule_type_retired`
- creating an `ExpiryRule`, or a rule with trigger `points.expired` → 400
- `rules/metadata` has no StampRule, no ExpiryRule and no STAMP target
- `events/types` has no `points.expired`
- `/complaints` → 404
- a migration test confirms that stamp and expiry rules end up disabled
- the existing `PointsExpirationJobE2ETests` must pass **unchanged**, which proves wallet expiry is untouched

## 7. Contracts check (`00-project-guardrails.md` §2)

| Contract | Effect |
|---|---|
| Tenancy | No change. The migrations update rows across all tenants by type or trigger, which is correct for a platform retirement. The list filter for STAMP is added alongside the existing tenant filter, never in place of it. |
| Money and points | No change. |
| Ledger | **Preserved.** No ledger row is updated or deleted, and history stays. Dropping `stamp_account_type_id` touches reward configuration, not the ledger. Wallet expiry keeps posting `points_expired` exactly as today. |
| Events | Fewer outbound messages (stamp completion only); no shape change. Inbound: `points.expired` was never publishable and is now unknown. Consumers keep `EventInbox` dedupe. Event `Data` stays snake_case. |
| Rule versioning | Stamp and expiry rules are disabled through `status`; versions are untouched. Programs are flagged as having unpublished changes. |
| CASH rules | No change. |
| Time | No change. |
| DSL parity | `rules/metadata` and `events/types` drive both sides, so the portal follows automatically. Check that the portal validator has no hard-coded `StampRule`, `ExpiryRule` or `points.expired`. |

## 8. Risks

| Risk | Mitigation |
|---|---|
| **Consumer deployed before the migration**: an active StampRule with no registered handler falls back to the zero-delta handler and fires for 0 (known pitfall) | Deploy order: migration → Api → Consumer, then force a rule-cache resync. Alternatively keep `StampRuleHandler` registered for one release. (Not a risk for ExpiryRule, which never fires.) |
| **Api deployed before the migration**: EF no longer maps `stamp_account_type_id` but the column is still there | Harmless (the column is nullable and unused). The reverse order is the risk: an **old** Api running after the migration fails on reward reads. Run the migration and deploy the Api in the same release window. |
| Implementer confuses inbound `points.expired` with outbound `loyalty.points.expired` / `LedgerReason.PointsExpired` and breaks wallet expiry | §2 "Two different points-expired names"; `OutboundEventTypes` and `LedgerReason` are explicitly out of the change. The unchanged `PointsExpirationJobE2ETests` must pass, and the client app must still receive `loyalty.points.expired` in the smoke test. |
| Complaints data and the stamp-reward wallet links are lost | Accepted (D7, D9). The normal pre-deploy database backup still applies. |
| The client-side application handles stamp-completion `loyalty.reward.earned` (e.g. shows a "card complete" message) | Tell the client app team before P3 ships (D12). After P3 the event never arrives with `source = stamp_completion`, so their handler for it goes quiet rather than failing. They can remove it at their own pace. |
| The portal and Api go out of step during deploy (removed DTO fields, `/complaints` 404) | The portal is the only API client (D12): ship the portal in the same release as each phase's Api change. |
| Disabled stamp rules point at a wallet the account-type list no longer returns | Portal fallback label (§5.2); API rule responses keep the raw `targetAccountTypeId`. |
| An old order refunded after retirement posts a negative `stamp_earn` reversal on a dormant wallet | Intended (D6). The balance stays correct and is visible in Customer 360. |
| Customers with partial stamp cards lose them without compensation | Accepted (D10). Customer communication, if any, is outside engineering scope. |
| Merge conflicts with the in-flight 2026-10-05 CRs (burn rules / dynamic reward purchase, rule-config limits), which also edit `RuleTypes`, `RuleTypeCatalog`, `RulesValidators` and the rule form | Agree the merge order with the Architect. This CR's edits to those files are small deletions and rebase easily. |
| Uncommitted TestCli and config edits in the working tree | Reconcile them before starting the TestCli part. |

## 9. Phasing and verification

1. **P1, Complaints** (independent, low risk). Covers API, schema migration, portal, tests and docs.
2. **P2, Stamp write paths and schema.** Covers the `RetireStampsCrNN` migration (rule disable + column drop), API validators, account-type list filter, catalog, rewards DTOs and entity, portal forms and the rule-list fallback, fintech seed, tests and docs.
3. **P3, Stamp engine.** Removes the handler, the completion step and the `LedgerPoster` hook, plus Consumer DI, engine tests, the starbucks seed and TestCli. Ship it together with or after P2, never before (see §8, row 1).
4. **P4, `points.expired` / ExpiryRule** (independent of P1–P3, low risk, because the removed code never runs). Covers the disable migration, Shared, API, engine, Consumer, portal, tests and docs. It can ship with P2 to share one migration and one rules-screen release.

Verification for each phase: `dotnet build dEngage.Loyalty.sln`, `dotnet test src/dEngage.Loyalty.Engine.Tests`, and `dotnet test src/dEngage.Loyalty.IntegrationTests` (Docker). From `web/`: `npx ng lint`, `npm test` and `npx ng build --configuration development`. Then a manual `.\run.ps1` smoke test:
- the dashboard loads without complaints
- STAMP is not offered and an existing STAMP wallet is not listed
- an existing stamp rule shows disabled with the fallback label
- reward create/edit works without `stampAccountTypeId`
- Customer 360 still shows stamp history
- the rule form offers neither ExpiryRule nor `points.expired`, while `signup` and `birthdaybonus` are still offered as triggers
- POINTS expiry days and warning days still save on the account type
- running the expiry job still posts `points_expired` and enqueues `loyalty.points.expired`

**Before deploy:** count, per tenant, the following and record the counts on the Jira ticket:
- STAMP account types
- active StampRule and STAMP-targeted rules
- non-zero STAMP balances
- `reward_definitions` rows with a non-null `stamp_account_type_id`
- complaints rows
- ExpiryRule / `points.expired` rules and `points.expired` streak campaigns

## 10. Open questions for the review

| # | Question | Status |
|---|---|---|
| Q1 | Keep `stamp_earn` in the tier-qualifying and cap calculations? | **Resolved → D5 (keep)** |
| Q2 | Should a refund of an order that earned stamps before retirement still reverse those stamps? | **Resolved → D6 (yes)** |
| Q3 | Drop `reward_definitions.stamp_account_type_id`, its FK and indexes? | **Resolved → D7 (drop)** |
| Q4 | Existing STAMP wallets: show them read-only or hide them? | **Resolved → D8 (hide from the account-type list)** |
| Q5 | Export the `complaints` data before the drop? | **Resolved → D9 (no export)** |
| Q6 | Is the portal the only client of the Loyalty REST API, and who listens to `loyalty.reward.earned`? | **Resolved → D12** (portal is the only API client; the client-side application listens to outbox events and is notified before P3) |
| Q7 | Compensation or communication for customers with partial stamp cards? | **Resolved → D10 (no compensation)** |
| Q8 | New content for the dashboard space the complaint card leaves? | **Resolved → D11 (collapse, nothing new)** |
| Q9 | Remove the `signup` trigger? | **Resolved → D13 (keep).** A signup-triggered rule is the only way to pay a fixed signup bonus, and signup feeds "within N hours of signup" conditions. |
| Q10 | Remove the `birthdaybonus` trigger and the birthday feature? | **Resolved → D14 (keep everything)** |
| Q11 | Remove `kyc.completed` too? | **Resolved → D14 (keep)** |
| Q12 | What exactly to remove for `points.expired`? | **Resolved → D15:** the trigger and ExpiryRule are removed from rules; wallet expiry (expiration and warning days, nightly job, `loyalty.points.expired`) is unchanged |

## 11. Explicitly out of scope

- Deleting or rewriting any ledger, reward-log or customer-account data.
- Deleting STAMP account types (they are hidden, not removed).
- Converting stamp balances into points or cash, or compensating customers.
- Removing the `loyalty.reward.earned` event type (streak and purchase rewards still use it).
- Removing `stamp_earn` from the tier, cap or refund calculations.
- Any replacement for complaints (ticketing or CRM integration) or for the dashboard card.
- **Any change to points expiry on the wallet:** POINTS expiration days, expiring-soon warning days, `PointsExpirationJob`, `PointsExpiringDetectorJob`, the `points_expired` ledger reason, and the outbound `loyalty.points.expired` and expiring-soon events.
- **The `signup`, `kyc.completed` and `birthdaybonus` triggers**, the birthday job and worker, `POST customers/{ref}/birthday`, and the `customer_birthdays` table.
- Enforcing "once per customer" for signup or "once per year" for birthday (tracked in `2026-10-05-rule-config-limits-by-trigger.md`).
- The per-rule "Expiry override" configuration field (a separate, non-working rule setting covered by the rule-config CR, not by this one).

## 12. Next step

All open questions are resolved. The Architect and PO review this draft. After the developer confirms approval, implementation runs P1 → P2 → P3 (with P4 alongside P2 or on its own) on a feature branch, and the docs are updated in the same change. Before P3 ships, the client app team is told about the end of stamp-completion `loyalty.reward.earned` (D12).

## 13. Revision history

| Date | Revision | Change |
|---|---|---|
| 2026-10-05 | 1 | First draft with D1–D4 and open questions Q1–Q8. |
| 2026-10-05 | 2 | Developer answered Q1–Q5, Q7, Q8 → D5–D11. Q3 changed the design: the stamp reward column, FK and indexes are now dropped (migration, entity, seeds, TestCli, schema reference). Q4 changed it too: STAMP wallets are hidden from the account-type list, with a portal fallback label for disabled stamp rules. Q5: no complaints export. Q6 still open. |
| 2026-10-05 | 3 | Q6 answered → D12: the portal is the only API client (outright breaks confirmed, portal ships with each phase); the client-side application listens to outbox events and is notified before P3. Risks updated. No open questions remain. |
| 2026-10-05 | 4 | Removal of the `birthdaybonus`, `signup` and `points.expired` triggers analysed (Q9–Q12). Signup, birthdaybonus and kyc.completed are kept (D13, D14). Signup is the only way to pay a signup bonus and feeds "within N hours of signup" conditions. The non-working `points.expired` trigger and `ExpiryRule` type are removed from rules while wallet expiry stays unchanged (D15): new §4.3, §5.3, phase P4, and updates to impact, risks, verification and scope. Rule types 8 → 6, built-in events 14 → 13. Title updated. |
| 2026-10-05 | 4.1 | Restored D3 and the "How D1 and D3 fit together" note to their revision 3 wording. Revision 4 had extended these already-agreed items to cover ExpiryRule / `points.expired`; that coverage now sits in D15. Q1–Q8 were not changed in revision 4. |
| 2026-10-05 | 5 | Approved; P1–P4 implemented. Status line updated and §15 (implementation record) added. Decisions D1–D15 unchanged; implementation deviations are listed in §15.2. |
| 2026-10-06 | 6 | Addendum A drafted: remove the birthday bonus end to end (decisions A-D1–A-D6; supersedes D14 for `birthdaybonus` only). Analysis only. |
| 2026-10-06 | 7 | Addendum A approved by all stakeholders and implemented; A.13 implementation record added. A-D1–A-D6 unchanged. |

## 14. Document record (guardrails §6)

| File | Change |
|---|---|
| `docs/scope-changes/2026-10-05-remove-complaints-and-stamps.md` | Created (rev 1), updated (rev 2, rev 3, rev 4) as analysis only; rev 5 records the implementation. The implementation's file list is in §15.4. |

## 15. Implementation record (2026-10-05)

Implemented on branch `1.6-remove-complaints-stamps-expiryrule` (created from `1.5-customer-360-view-implementation`; nothing committed). The developer's earlier staged TestCli, `docker-compose.yml`, Consumer `appsettings*.json` and `LoyaltyDbContextFactory.cs` changes were left as they were; the TestCli edits below sit on top of them.

### 15.1 What was built

| Phase | Done |
|---|---|
| P1 Complaints | Api `Complaints/` module and its registrations removed; `DashboardSummaryResponse.complaints` removed; `Complaint` entity, configuration, `DbSet` and `ComplaintStatus` removed; migration `20261005140753_RemoveComplaintsCr1005` drops `complaints`; portal `features/complaints/`, route, sidebar entry, dashboard card and `nav.complaints` (en/tr) removed. |
| P2 Stamp write paths + schema | STAMP rejected on create (400) and edit (409 `account_type_retired`), hidden from `GET account-types`; `StampAccountTypeConfigValidator` removed; `StampRule` out of `RuleTypes.All` and the catalog, STAMP out of ManualAdjustment targets; retired rules refused on edit/approve/activate (409 `rule_type_retired`); `stampAccountTypeId` removed from reward DTOs, publish snapshot, entity and configuration; migration `20261005141326_RetireStampsAndExpiryRuleCr1005` disables affected rules and streak campaigns and drops the column, FK and both indexes; portal account-type form, rule form, reward model and the rules-list "Retired wallet" fallback (en/tr). |
| P3 Stamp engine | `StampRuleHandler`, `StampCompletionHandler`, `IStampCompletionHandler` deleted; `LedgerPoster` stamp branch, completion hook, constructor parameter and `StampRule → stamp_earn` mapping removed; Consumer DI updated. Historical read paths (tier, caps, refund/reversal, Customer 360) untouched (D5, D6). |
| P4 `points.expired` / ExpiryRule | `points.expired` out of `EventTypes.All`/`Catalog`; `ExpiryRule` out of `RuleTypes.All` and the catalog; `ExpiryRuleHandler` and `PointsExpiredHandler` deleted; `ageDays`/`order` removed from `RuleCalculation` and the portal; rules and streak campaigns refuse the trigger (`trigger_retired`); event simulator constant and comments updated. Wallet expiry untouched. |
| Seeds, TestCli, docs | `starbucks_loyalty.sql` (STAMP wallet + stamp rule removed), `fintech_loyalty.sql` (dropped column), `loyalty_schema.sql` regenerated (a pure 96-line append); TestCli stamp scenarios removed; baseline, SOW, API doc, schema reference, `.claude/rules`, `CLAUDE.md` and superseded notes on the earlier CR docs updated. |

### 15.2 Deviations from the analysis

1. **`EventTypes.PointsExpired` is kept as a retired constant**, not deleted (§5.3 said "delete"). Once out of the catalog, an unknown trigger is treated as a tenant generic type and accepted, so the API must reject `points.expired` by name. Same "retired constant" pattern as `StampRule` / `ExpiryRule` / `RewardAcquisition.StampCompletion`.
2. **Status codes follow the existing `reward_type_retired` pattern**: creating with a retired value fails validation (**400**); editing, approving or activating an existing retired row is **409** `account_type_retired` / `rule_type_retired` / `trigger_retired` (§6 said 400 for all).
3. **One migration for P2 + P4** (`RetireStampsAndExpiryRuleCr1005`), as §5.3 allowed, since both shipped together.
4. **`AccountType.STAMP` enum member kept** (marked retired): it is used via `nameof` for the list filter and edit guard, and stored rows carry it.
5. **TestCli Starbucks scenarios adjusted, not just deleted**: T07 keeps its 8 orders (later tests depend on the balance) and now asserts Stars = 365; OB01/OB03/RW06 expected counts drop by the stamp rule's contribution; stamp-only tests (D01–D03, LB05, S03, RW01) and their two orphaned helpers removed.
6. **`fintech_loyalty.sql` was already stale** before this change (it still inserts `external_coupon_type` and uses a slug as `tenant_id`). Only the dropped column was removed; the unrelated staleness is left for a separate fix.

### 15.3 Verification

| Check | Result |
|---|---|
| `dotnet build dEngage.Loyalty.sln` | **0 errors**, 1 pre-existing warning (CS1998 in `RuleVersioningServiceTests.cs`, untouched). Built into a scratch output folder because the running Api (pid 37828) and Consumer (pid 38820) lock their `bin` folders. |
| `dotnet test src/dEngage.Loyalty.Engine.Tests` | **84 passed**, 0 failed |
| `dotnet test src/dEngage.Loyalty.IntegrationTests` | **219 passed**, 0 failed (includes the new `RetiredStampsAndExpiryRuleApiTests`) |
| `dotnet test ... --filter Category=E2E` (Postgres Testcontainers) | **28 passed**, 0 failed — applies every migration, including both new ones, to a real Postgres |
| Portal `npm test` | **138 passed** (22 files) |
| Portal `npx ng build --configuration development` | Succeeds |
| Portal `npx ng lint` | 3 errors, all **pre-existing** and in untouched lines (`autofocus` ×2 in `core/ui/confirm.service.ts`; the program-filter `<label>` in `dashboard.page.ts`) |
| Prettier | Not applied file-wide: the touched files weren't Prettier-clean before, and a whole-file pass re-wrapped unrelated code. Edits follow the surrounding formatting instead. |
| **Not run** | TestCli (needs the live local stack with this branch's Consumer); the `.\run.ps1` manual smoke test in §9; the migrations against the shared dev database. |

**Before deploy:** run the §9 per-tenant counts and record them on the Jira ticket; deploy migration → Api + portal → Consumer, then force a rule-cache resync (§8 row 1); tell the client app team that stamp-completion `loyalty.reward.earned` stops (D12).

### 15.4 Files (review record, guardrails §6)

**Created**
- `src/dEngage.Loyalty.Schema/Migrations/20261005140753_RemoveComplaintsCr1005.cs` + `.Designer.cs` — drop `complaints`.
- `src/dEngage.Loyalty.Schema/Migrations/20261005141326_RetireStampsAndExpiryRuleCr1005.cs` + `.Designer.cs` — disable retired rules/streaks, drop the stamp reward link.
- `src/dEngage.Loyalty.IntegrationTests/Api/RetiredStampsAndExpiryRuleApiTests.cs` — guards every retirement path.

**Deleted**
- `src/dEngage.Loyalty.Api/Complaints/` (AppService, Dtos, Module, Validators); `src/dEngage.Loyalty.Schema/Entities/Complaint.cs`; `src/dEngage.Loyalty.Schema/Configurations/ComplaintConfiguration.cs`; `src/dEngage.Loyalty.Shared/Constants/ComplaintStatus.cs`; `web/src/app/features/complaints/` (model, list page, routes, service) — complaints removed.
- `src/dEngage.Loyalty.RuleEngine/Calculation/StampRuleHandler.cs`, `Processing/StampCompletionHandler.cs`, `Processing/IStampCompletionHandler.cs` — stamp engine removed.
- `src/dEngage.Loyalty.RuleEngine/Calculation/ExpiryRuleHandler.cs`, `src/dEngage.Loyalty.Consumer/Handlers/PointsExpiredHandler.cs` — ExpiryRule / `points.expired` removed.
- `src/dEngage.Loyalty.IntegrationTests/Engine/StampCompletionCr0930Tests.cs` — covered removed behaviour; replaced by the retirement guards.

**Changed: backend**
- `src/dEngage.Loyalty.Api/Program.cs` — complaints and STAMP validator registrations removed.
- `Api/Dashboard/DashboardAppService.cs`, `DashboardDtos.cs` — `complaints` removed.
- `Api/AccountTypes/AccountTypesValidators.cs`, `AccountTypeConfigValidators.cs`, `AccountTypesAppService.cs` — STAMP rejected, hidden from the list, edit refused.
- `Api/Rules/RulesValidators.cs`, `RulesAppService.cs` — retired types/trigger/targets refused.
- `Api/StreakCampaigns/StreakCampaignsValidators.cs`, `StreakCampaignsAppService.cs` — `points.expired` refused.
- `Api/Rewards/RewardsDtos.cs`, `RewardsValidators.cs`, `RewardsAppService.cs`, `Api/Programs/ProgramsDtos.cs`, `ProgramsAppService.cs` — `stampAccountTypeId` removed.
- `Api/Events/EventsAppService.cs`, `EventsDtos.cs` — comments only.
- `src/dEngage.Loyalty.Consumer/Program.cs` — stamp/expiry handler registrations removed.
- `RuleEngine/Metadata/RuleTypeCatalog.cs`, `Models/RuleCalculation.cs`, `Processing/LedgerPoster.cs`, `Processing/DelayedPostingPromotionJob.cs`, `Calculation/ManualAdjustmentRuleHandler.cs` — stamp/expiry removed (the last two: comments only).
- `Schema/LoyaltyDbContext.cs`, `Entities/RewardDefinition.cs`, `Configurations/RewardDefinitionConfiguration.cs`, `Migrations/LoyaltyDbContextModelSnapshot.cs` (generated) — schema.
- `Shared/Constants/RuleTypes.cs`, `AccountType.cs`, `Events/EventTypes.cs` — retired values.

**Changed: tests and TestCli**
- `Engine.Tests/RuleTypeHandlerRegistryTests.cs`; `IntegrationTests/Fixtures/RuleEngineTestHarness.cs`, `Engine/StackingResolutionWorkedExampleTests.cs`, `Api/AccountTypeTransferConfigApiTests.cs`, `Api/BurnTriggerRuleTypesApiTests.cs`, `Api/Phase4FeaturesSmokeTests.cs`, `E2E/DashboardEndpointE2ETests.cs` — stamp/complaint/expiry expectations removed or inverted.
- `TestCli/IntegrationTest.cs`, `OutboundTest.cs`, `RewardTest.cs`, `MultiTenantTest.cs` (comment) — stamp scenarios removed (on top of the developer's staged edits).

**Changed: portal**
- `web/src/app/app.routes.ts`, `layout/sidebar/sidebar.ts`, `features/dashboard/dashboard.page.ts`, `dashboard.model.ts`, `public/i18n/en.json`, `tr.json` — complaints removed; `rules.groups.retiredAccount` added.
- `features/programs/account-types/account-type-form.dialog.ts`, `account-type.model.ts` — STAMP option removed.
- `features/programs/rules/rule.model.ts`, `rule-form.page.ts`, `rules-list.page.ts` — StampRule/ExpiryRule removed; retired-wallet label.
- `features/programs/rewards/reward.model.ts` — `stampAccountTypeId` removed.
- `features/events/event-simulator.page.ts`, `core/events/event-types.service.ts` — `points.expired` removed.

**Changed: seeds and docs**
- `scripts/starbucks_loyalty.sql`, `scripts/fintech_loyalty.sql`, `scripts/loyalty_schema.sql` (regenerated), `scripts/loyalty_schema_reference.md`.
- `LoyaltySaaSApi.md`, `docs/SCOPE_BASELINE.md`, `docs/SOW.md`, `CLAUDE.md`, `.claude/rules/01-workflow-and-debugging.md`, `.claude/rules/backend-api.md`.
- Superseded/cross-reference notes: `docs/scope-changes/2026-09-17-reward-type-taxonomy.md`, `2026-09-22-rules-engine-taxonomy.md`, `2026-09-28-program-account-type-changes.md`, `2026-09-30-reward-acquisition-fulfilment.md`, `2026-10-02-customer-360.md`, `2026-10-05-rule-config-limits-by-trigger.md`.

---

## Addendum A — Remove the birthday bonus end to end (2026-10-06)

**Status:** **Approved** by all stakeholders (confirmed by the developer, 2026-10-06). **Implemented** — see A.13.
**Type:** Scope change. It **supersedes D14 for `birthdaybonus` only**; `signup` and `kyc.completed` stay as D13/D14 decided. D14's text above is left as it was approved.
**Basis:** code read on 2026-10-06 of the branch `1.6-remove-complaints-stamps-expiryrule` (including the in-flight 2026-10-05 burn-rules work in the working tree) and the local `loyalty_dev` database. Nothing was built or run for this addendum.

### A.1 Summary

The birthday bonus is removed: the `birthdaybonus` rule trigger, the nightly `BirthdayBonusJob` / `BirthdayBonusWorker` (02:00 UTC), the `POST customers/{contactKey}/birthday` endpoint and the `customer_birthdays` table. Afterwards the platform keeps no customer birth dates and can no longer pay a birthday bonus.

Ledger postings already made by the job (source event id `birthday:{contactKey}:{year}`) stay as history, because the ledger is append-only (guardrail §2). On 2026-10-06, `loyalty_dev` has 0 birthday rules, 0 registered birthdays and 0 birthday postings.

### A.2 Decisions agreed with the developer (2026-10-06)

| # | Decision |
|---|---|
| A-D1 | **Remove the birthday bonus end to end.** Supersedes D14 for `birthdaybonus`; `signup` and `kyc.completed` are unaffected. |
| A-D2 | **Drop `customer_birthdays` by migration.** Entity, configuration and `DbSet` removed; registered birthdays are lost (0 rows locally — check other environments before deploy). The table holds customer data the platform no longer needs. |
| A-D3 | **`POST customers/{contactKey}/birthday` is removed outright (404).** The developer confirmed no system calls it (the portal never had a birthday screen). Documented as a breaking change. |
| A-D4 | **Same retirement pattern as `points.expired` (D15).** `birthdaybonus` leaves `EventTypes.All` and the catalog but stays as a retired constant. Rules and streak campaigns then reject it by name (`trigger_retired`) instead of accepting it as an unknown generic type. Existing rules and streak campaigns on it are disabled by migration, never deleted, and can't be edited or re-activated. |
| A-D5 | **Ledger history stays.** Past birthday postings are not touched; Customer 360 keeps showing them (they have no inbound event, like other job postings). |
| A-D6 | **The analysis lives in this document as Addendum A** (same pattern as the CR 2026-09-30 addendum A). |

### A.3 Baseline and SOW rows touched

| Where | Change |
|---|---|
| `SCOPE_BASELINE.md` Customers (l.21) | Remove the `POST customers/{ref}/birthday` exception; the module becomes fully read-only |
| `SCOPE_BASELINE.md` scheduled triggers (l.46) | "Deterministic scheduled/synthetic triggers: birthday bonus" → Removed |
| `SCOPE_BASELINE.md` program gating note (l.32), Consumer (l.55), events (l.74) | Drop the birthday-bonus mentions; the simulator no longer has any scheduled type to hide |
| `SCOPE_BASELINE.md` event count | 13 → 12 built-in event types |
| `SOW.md` | §2.3 event list (l.74), batch jobs (l.87), simulator note (l.92); §2.4 birthday endpoint exception (l.115–116); §3 program-status note (l.204); §4 scheduler additions (l.247) |

### A.4 Current state (as-built, verified by code read)

| Layer | Where |
|---|---|
| Shared | `EventTypes.BirthdayBonus` in `All` and `Catalog` (Earn / Scheduled / OncePerPeriod / Yearly) — [EventTypes.cs](../../src/dEngage.Loyalty.Shared/Events/EventTypes.cs) l.44, l.60, l.86, comments l.39, l.165 |
| RuleEngine | [BirthdayBonusJob.cs](../../src/dEngage.Loyalty.RuleEngine/Processing/BirthdayBonusJob.cs) reads `customer_birthdays` and calls `IRuleEngine.ProcessEventAsync` per live program with event id `birthday:{contactKey}:{year}`. There is also a comment in [ProgramLiveness.cs](../../src/dEngage.Loyalty.RuleEngine/Processing/ProgramLiveness.cs) l.8, a file that belongs to the in-flight 2026-10-05 burn-rules work. |
| Consumer | [BirthdayBonusWorker.cs](../../src/dEngage.Loyalty.Consumer/BirthdayBonusWorker.cs) (02:00 UTC); DI in [Program.cs](../../src/dEngage.Loyalty.Consumer/Program.cs) l.93 (job), l.134 (hosted service) |
| Api | [CustomersModule.cs](../../src/dEngage.Loyalty.Api/Customers/CustomersModule.cs) l.11–15, l.131–136 (route, validator injection); [CustomersAppService.cs](../../src/dEngage.Loyalty.Api/Customers/CustomersAppService.cs) l.38–42, l.498–523 (`RegisterBirthdayAsync`); [CustomersDtos.cs](../../src/dEngage.Loyalty.Api/Customers/CustomersDtos.cs) l.111–113; [CustomersValidators.cs](../../src/dEngage.Loyalty.Api/Customers/CustomersValidators.cs) (`RegisterBirthdayRequestValidator`); comments in [EventsAppService.cs](../../src/dEngage.Loyalty.Api/Events/EventsAppService.cs) l.40 and [EventsDtos.cs](../../src/dEngage.Loyalty.Api/Events/EventsDtos.cs) l.30 |
| Schema | [CustomerBirthday.cs](../../src/dEngage.Loyalty.Schema/Entities/CustomerBirthday.cs), [CustomerBirthdayConfiguration.cs](../../src/dEngage.Loyalty.Schema/Configurations/CustomerBirthdayConfiguration.cs), `DbSet` in [LoyaltyDbContext.cs](../../src/dEngage.Loyalty.Schema/LoyaltyDbContext.cs) l.18. The table and its 2 indexes come from `20260921161222_CustomerBirthdayCr10`, which stays (applied migrations are never edited). |
| Portal | No screen. `SCHEDULED_EVENT_TYPES = ['birthdaybonus']` in [event-simulator.page.ts](../../web/src/app/features/events/event-simulator.page.ts) l.26; comments in [event-types.service.ts](../../web/src/app/core/events/event-types.service.ts) l.11 and [customer-profile-panel.ts](../../web/src/app/features/customers/customer-profile-panel.ts) l.11. The rule and streak trigger pickers read `events/types`, so the trigger disappears from them automatically. |
| Tests | [BirthdayBonusJobTests.cs](../../src/dEngage.Loyalty.IntegrationTests/Engine/BirthdayBonusJobTests.cs) (2 tests); `BirthdayBonus` assertions in [BurnTriggerRuleTypesApiTests.cs](../../src/dEngage.Loyalty.IntegrationTests/Api/BurnTriggerRuleTypesApiTests.cs) l.109–112 and [RetiredStampsAndExpiryRuleApiTests.cs](../../src/dEngage.Loyalty.IntegrationTests/Api/RetiredStampsAndExpiryRuleApiTests.cs) l.183. Two references are unrelated and stay unchanged: `StackingResolutionWorkedExampleTests` has a rule *named* "Birthday Bonus" on `card.transaction`, and `AccountTypeChangesCl13E2ETests` uses `CustomerBirthdayCr10` only as a migration-ordering anchor. |
| TestCli, seeds | No birthday scenario, birthday rule or registered birthday. |
| Docs | `LoyaltySaaSApi.md` l.168, l.184; `scripts/loyalty_schema_reference.md` l.21, l.321–344; `.claude/rules/backend-consumer.md` l.44 (`BirthdayBonusWorker` is the reference example for workers) and l.50 (nightly order), `.claude/rules/backend-api.md` l.74; `docs/SOW.md` and `docs/SCOPE_BASELINE.md` as in A.3. |

### A.5 Proposed change

**Migration `RemoveBirthdayBonusCr1006`.** Generated with `dotnet ef migrations add`, with SQL steps added first, in the same shape as `RetireStampsAndExpiryRuleCr1005`:
1. Mark published programs with an affected rule or streak campaign as `has_unpublished_changes = true`.
2. Set `rules.status = 'disabled'` where `status IN ('active','pending_approval') AND trigger = 'birthdaybonus'`.
3. Set `streak_campaigns.status = 'disabled'` where `status = 'active' AND trigger = 'birthdaybonus'`.
4. `DropTable("customer_birthdays")` (A-D2). `Down` recreates the empty table and indexes; disabled rows and dropped birthdays are not restored.

**Shared:**
- Remove `BirthdayBonus` from `EventTypes.All` and `Catalog`, and keep the constant marked retired (A-D4).
- `EventCardinality.OncePerPeriod` and `EventPeriod.Yearly` are no longer used by any built-in but stay. They are part of the `rules/metadata` contract and the portal's `EventMetadata` type, and removing enum values is a separate decision.

**RuleEngine / Consumer:**
- Delete `BirthdayBonusJob` and `BirthdayBonusWorker` and their two DI registrations.
- Update the `ProgramLiveness` comment, coordinating with the burn-rules work, which owns that file.

**Api:**
- Remove the `/{contactKey}/birthday` route, the validator injection in `CustomersModule`, `RegisterBirthdayAsync` (interface and implementation), `RegisterBirthdayRequest` / `BirthdayResponse`, and `RegisterBirthdayRequestValidator`. Customers becomes fully read-only; update the module and app-service comments.
- Rules and streak campaigns: reject `birthdaybonus` by name with `trigger_retired` (create/update → 400), and treat an existing rule or campaign on it as retired (edit/approve/activate → 409), extending the D15 guards.
- Update the comments in `EventsAppService` and `EventsDtos`. `EventTypes.IsExternallyPublishable` stays: with no Scheduled built-in left it has no effect, but it is the guard for any future scheduled type.

**Portal:**
- `SCHEDULED_EVENT_TYPES` becomes empty, or the constant and its filter are removed because `publishable` already covers it. Either way the behaviour is the same; decide at implementation.
- Update the comments in `event-types.service.ts` and `customer-profile-panel.ts`.
- Nothing else changes: the trigger pickers follow `events/types`.

**Schema docs and scripts:** regenerate `scripts/loyalty_schema.sql`, and remove `customer_birthdays` from `scripts/loyalty_schema_reference.md` with a removal note. No seed creates birthdays or birthday rules (checked).

**Tests:**
- Delete `BirthdayBonusJobTests`, because the behaviour is removed (same reasoning as §6 "Tests for removed features").
- `BurnTriggerRuleTypesApiTests`: assert `birthdaybonus` is absent from `builtIn`.
- `RetiredStampsAndExpiryRuleApiTests`: move `BirthdayBonus` from the "still offered" list to the retired assertions, and add three checks:
  - a rule or streak campaign on `birthdaybonus` → 400 `trigger_retired`;
  - an existing disabled birthday rule can't be re-activated (409);
  - `POST customers/{ref}/birthday` → 404.

### A.6 End-to-end impact

| Area | Impact |
|---|---|
| DB | 1 migration: disable rules/streaks (SQL) + **drop `customer_birthdays`** (destructive; A-D2) |
| API contract (breaking) | `POST customers/{contactKey}/birthday` removed (404); `birthdaybonus` rejected as a trigger; `events/types.builtIn` and `rules/metadata.events` no longer list it |
| Engine / Consumer | Job and nightly worker removed; no other job's schedule depends on it (tier 00:00 and expiry 03:00 are independent) |
| Ledger | None — past `birthday:*` postings stay |
| Outbound events | None of its own (birthday postings produced the normal `loyalty.points.earned`; that event type stays) |
| Redis caches | Rule-cache resync after the migration, as for CR 2026-10-05 |
| Portal | One constant and comments |
| Client app | No caller of the endpoint (A-D3); if the client app showed "birthday bonus" earnings, those simply stop |

### A.7 Contracts check

- **No change:** tenancy, money, CASH approval, time.
- **Ledger:** preserved; no update or delete.
- **Rule versioning:** rules are disabled through `status`, versions are untouched, and programs are flagged as having unpublished changes.
- **Events:** no shape change.
- **DSL parity:** driven by `events/types` and `rules/metadata`, so the portal follows automatically.

### A.8 Risks

| Risk | Mitigation |
|---|---|
| A customer whose birthday falls on deploy night is paid by an old Consumer and not by the new one | Accepted — the feature is being removed. Deploy migration → Api → Consumer as for CR 2026-10-05. |
| An old Consumer still running after the migration: `BirthdayBonusJob` fails at 02:00 because the table is gone | The failure is caught and logged ("will retry tomorrow"), with no data harm. Restart the Consumer from the new build in the same window. |
| Another environment holds registered birthdays | Count `customer_birthdays` per tenant before deploy and record it on the Jira ticket (A-D2 accepts the loss). |
| Merge with the in-flight burn-rules work (`ProgramLiveness.cs`, rule validators, Consumer `Program.cs`) | Small deletions; agree the merge order with the Architect. |

### A.9 Phasing and verification

A single phase (P5). Verification follows §9:
- backend build, Engine.Tests, and IntegrationTests including `Category=E2E` (which applies the new migration to Postgres);
- portal lint, test and build;
- then the local migration and a smoke test: the rule form no longer offers `birthdaybonus`, and `POST .../birthday` → 404.

### A.10 Open questions

None. A-D1 to A-D6 were agreed with the developer on 2026-10-06. Implementation waits for PO + Architect approval.

### A.11 Explicitly out of scope

- Any replacement for the birthday bonus (for example a generic "date-based" campaign).
- Removing `EventCardinality.OncePerPeriod` / `EventPeriod.Yearly`.
- `signup` and `kyc.completed` (kept, D13/D14).
- Touching past birthday ledger postings.

### A.12 Document record

| File | Change |
|---|---|
| `docs/scope-changes/2026-10-05-remove-complaints-and-stamps.md` | Addendum A added (analysis only); status sentence and revision row 6 added. No code, schema or other doc changed. |

### A.13 Implementation record (2026-10-06)

Implemented on branch `1.6-remove-complaints-stamps-expiryrule`, nothing committed. Other in-flight work in the same working tree was left as it was. That includes the 2026-10-05 burn-rules change and its migration `20261005180559_DisableLegacyBurnRulesCr1005`; only the comment in `ProgramLiveness.cs` was touched there.

**What was built.** Everything in A.5, with these choices made at implementation:
- `EventTypes.IsRetiredTrigger(eventType)` (points.expired or birthdaybonus) is now the single check behind the rule and streak guards. It replaces the `points.expired`-only comparisons added for D15, so both triggers get the same 400 / 409 `trigger_retired` / `rule_type_retired` behaviour.
- Portal: the event simulator's `SCHEDULED_EVENT_TYPES` fallback list and its filter were removed (A.5 left this open). `publishable` from the API already excludes scheduled types, and none are left.
- `CustomersValidators.cs` held only `RegisterBirthdayRequestValidator`, so the file was deleted. Three usings in `CustomersModule.cs` that only the birthday route needed were removed.
- `.claude/rules/backend-consumer.md` now names `PointsExpirationWorker` as the reference worker, since `BirthdayBonusWorker` was the example.

**Verification**

| Check | Result |
|---|---|
| `dotnet build dEngage.Loyalty.sln` (scratch output; the running Api/Consumer lock their `bin`) | 0 errors, 1 pre-existing warning (CS1998, untouched file) |
| Engine.Tests | **95 passed**, 0 failed |
| IntegrationTests | **251 passed**, 0 failed |
| IntegrationTests `Category=E2E` (Postgres; applies every migration incl. `RemoveBirthdayBonusCr1006`) | **44 passed**, 0 failed |
| Portal `npm test` / `ng build --configuration development` | **145 passed** / succeeds |
| Portal `ng lint` | the same 3 pre-existing errors as §15.3, nothing new |
| **Not run** | the migration against `loyalty_dev`; a manual smoke test on the running stack |

Test counts are higher than in §15.3 because the in-flight burn-rules work added tests.

**Before deploy:**
- Count `customer_birthdays` rows per tenant and record the counts on the Jira ticket.
- Deploy migration → Api + portal → Consumer, then resync the rule cache.
- `dotnet ef database update` now also applies the burn-rules migration `DisableLegacyBurnRulesCr1005`, which sits before this one. That migration needs its own approval and deploy record.
- `scripts/loyalty_schema.sql` was regenerated as a pure append (84 lines), and that append includes the burn-rules migration as well as this one.

**Files**

*Created*
- `src/dEngage.Loyalty.Schema/Migrations/20261006082544_RemoveBirthdayBonusCr1006.cs` + `.Designer.cs` — disables birthday rules/streaks and drops `customer_birthdays`.

*Deleted*
- `src/dEngage.Loyalty.RuleEngine/Processing/BirthdayBonusJob.cs`, `src/dEngage.Loyalty.Consumer/BirthdayBonusWorker.cs` — job and nightly worker.
- `src/dEngage.Loyalty.Api/Customers/CustomersValidators.cs` — only held the birthday validator.
- `src/dEngage.Loyalty.Schema/Entities/CustomerBirthday.cs`, `Configurations/CustomerBirthdayConfiguration.cs` — table mapping.
- `src/dEngage.Loyalty.IntegrationTests/Engine/BirthdayBonusJobTests.cs` — tested the removed job; replaced by the retirement guards below.

*Changed*
- `src/dEngage.Loyalty.Shared/Events/EventTypes.cs` — `birthdaybonus` retired, `IsRetiredTrigger` added.
- `src/dEngage.Loyalty.Api/Customers/CustomersModule.cs`, `CustomersAppService.cs`, `CustomersDtos.cs` — birthday route, method and DTOs removed.
- `src/dEngage.Loyalty.Api/Rules/RulesValidators.cs`, `RulesAppService.cs`, `src/dEngage.Loyalty.Api/StreakCampaigns/StreakCampaignsValidators.cs`, `StreakCampaignsAppService.cs` — retired-trigger guards cover both triggers.
- `src/dEngage.Loyalty.Api/Events/EventsAppService.cs`, `EventsDtos.cs`, `src/dEngage.Loyalty.RuleEngine/Processing/ProgramLiveness.cs` — comments only.
- `src/dEngage.Loyalty.Consumer/Program.cs` — job and worker registrations removed.
- `src/dEngage.Loyalty.Schema/LoyaltyDbContext.cs`, `Migrations/LoyaltyDbContextModelSnapshot.cs` (generated) — `DbSet` removed.
- `src/dEngage.Loyalty.IntegrationTests/Api/BurnTriggerRuleTypesApiTests.cs`, `RetiredStampsAndExpiryRuleApiTests.cs` — `birthdaybonus` asserted absent and refused; existing birthday rule can't be re-activated; birthday route → 404.
- `web/src/app/features/events/event-simulator.page.ts`, `web/src/app/core/events/event-types.service.ts` — scheduled-type fallback removed; comment.
- `scripts/loyalty_schema.sql` (regenerated), `scripts/loyalty_schema_reference.md` — `customer_birthdays` removed with a note.
- `LoyaltySaaSApi.md`, `docs/SCOPE_BASELINE.md`, `docs/SOW.md`, `.claude/rules/backend-consumer.md`, `.claude/rules/backend-api.md` — docs in step.
- `docs/scope-changes/2026-10-05-remove-complaints-and-stamps.md` — this record.
