# Scope Change Impact Analysis: Rules Engine Taxonomy (CR-01 through CR-11)

**Status:** Approved 2026-09-22 by Product Owner + Architect — implemented on branch `feature/reward-type-taxonomy`

> **Superseded in part by CR 2026-10-05 ([2026-10-05-remove-complaints-and-stamps.md](2026-10-05-remove-complaints-and-stamps.md)):** the `StampRule` and `ExpiryRule` types, the `points.expired` trigger and STAMP targets were removed (8 → 6 rule types, 14 → 13 built-in events).

**Date:** 2026-09-22
**Author:** Claude Code, at the request of danishaliqau@gmail.com
**Affects:** `docs/SOW.md` §2.2/§2.3/§2.4/§3/§4, `docs/SCOPE_BASELINE.md`, `scripts/loyalty_schema_reference.md`
**Source docs:** `docs/scope-change-rules/rules-engine-requirements-and-sow-changelog.md` (Part A:
requirements A1–A11; Part B: SOW change log CR-01–CR-11), `docs/scope-change-rules/loyalty-rule-builder.html`
(reference UI, used as UX inspiration for the rebuilt rule builder, not a literal spec)

This document is step 2 of the scope-change process defined in `CLAUDE.md`: an impact analysis
against the `docs/SCOPE_BASELINE.md` baseline and the as-built code, now updated with the
approval record and an as-implemented summary since the branch is complete.

## Approval record

Product Owner + Architect approved the direction below on 2026-09-22 and resolved the open
questions as follows:

1. **Delivery shape** — all 11 CRs on a single feature branch (the pre-existing
   `feature/reward-type-taxonomy` branch, per explicit instruction — no new branch), one PR,
   reviewed as one diff, sequenced internally by the milestone plan below so the diff stays
   reviewable milestone-by-milestone even though it merges as one PR.
2. **TIER_POINTS** — deferred entirely, not built. Part A's account-kind table lists it, but
   Part B puts tier qualification/progression explicitly out of scope, and building a real
   TIER_POINTS wallet would reopen that. `FixedBonusRule` ships with targets `POINTS, CASH`
   (not `TIER_POINTS`); `ManualAdjustmentRule` ships with `POINTS, CASH, STAMP` (not "any").
3. **Condition operators/fields not in Part A** (`occurred_within`, `gt`, `lt`, the special-cased
   `tier`/`event.hour_of_day`/`event.day_of_week` fields) — mapped onto the new namespace model
   rather than dropped: lookback folds into `agg.*`, `tier` moves under `profile.*`,
   `event.hour_of_day`/`event.day_of_week` stay under `event.*`.
4. **CASH approval** — a lightweight approval gate: CASH-targeted rules save as
   `PendingApproval`; a distinct admin (`CreatedBy ≠ ApprovedBy`, identity comparison, since
   current RBAC has no per-feature roles to gate a real "approver" role on) activates them via
   `PATCH .../approve`. A fuller workflow (roles, multi-step, notifications) is a future CR.

Milestone-level questions resolved during implementation (all "Recommended" options accepted):

5. The 5 under-counted event types (`signup`, `kyc.completed`, `card.transaction`, `remittance`,
   `points.adjusted`) added as new built-in events rather than left as generic/tenant-config.
6. Burn/Reverse/Adjust rule types built **additively** — existing `points.redeem` /
   `points.transfer` / `order.refunded` handlers untouched; the new rule types become available
   for newly-configured rules going forward.
7. Transfer/Reversal execute through **dedicated processors** that bypass the shared
   `WinnerSelector`/`LedgerPoster` pipeline (dual-entry posting / inherited target account
   respectively), not shoehorned into it.
8. `RuleCalculation.AllowNegative` (originally boolean in Part A's illustrative shape) generalized
   to `"allow negative" | "clamp to zero"` to match the string-enum convention used elsewhere in
   the same model.
9. `TransferRuleProcessor`/`ReversalRuleProcessor` built fully per spec but **deliberately left
   disconnected** from live event dispatch this branch — they exist and are tested, but nothing
   currently routes `points.transfer`/reversal-triggering events to them, avoiding a
   double-processing risk against the pre-existing handlers until a follow-on change explicitly
   cuts over.
10. Delayed posting (CR-08) built now, following the existing nightly-job pattern
    (`PointsExpirationWorker`), rather than deferred.
11. Rule versioning built as insert-new-version-on-edit (not a temporal table); the CR-09 limit
    race is closed by moving limit checks inside the posting transaction (budget reservation),
    not just documented as a known gap.
12. Two CR-09 gaps **knowingly left open**, not fixed on this branch: `OncePerCustomer`/
    `OncePerPeriod` cardinality is check-then-write, not a DB unique index; transactional
    counters cover rule budgets only, not every limit type. Both are called out in §6 (Risks) and
    in `docs/SOW.md` §3 — revisit before this engine carries adversarial-scale traffic.

## 1. Why this change is being proposed

The reference docs (Part A/B) describe a materially richer Rules feature than what's built today:
3 rule types → 8, a flat AND-only condition list → a typed grouped AND/OR tree, 2 limit fields →
8, a boolean `Stackable` → real exclusivity-group + multiplier/additive stacking resolution, and a
set of engine guarantees (idempotency, versioning, transactional counters, audit) only partially
met by the current implementation. Confirmed against code before this branch started: 3 rule
types, flat AND-only conditions, 2 limit fields, boolean `Stackable`, 7 built-in events — matches
the "Baseline" column of every CR in Part B exactly, so this analysis was written directly against
the current `docs/SCOPE_BASELINE.md`/`docs/SOW.md` (themselves still pending their own sign-off,
but accurate for Rules as of this branch's start).

## 2. Architectural principles applied

- **One compatibility source of truth.** The UI previously hardcoded `RULE_TYPES`/`EVENT_TRIGGERS`
  independently of the engine's matching logic — exactly the drift CR-03 exists to prevent. Event
  taxonomy and rule-type metadata are now defined once (`RuleEngine.Metadata.RuleTypeCatalog`,
  `Shared.Events.EventTypes`), consumed directly by the engine (`RulesValidators`, `RuleMatcher`
  via `RuleTypeCatalog.IsCompatible`) and exposed read-only through
  `GET .../rules/metadata`, which the Angular rule builder now calls instead of hardcoding
  options — the same pattern already used for `RewardTypeRegistry` → Angular.
- **Ledger stays append-only and the system of record; Redis stays a cache.** The prior gap
  (limit counters incremented in Redis outside the posting DB transaction) violated engine
  guarantee #3 and is closed: a new Postgres `rule_limit_counters` table is the durable counter
  store, Redis caches in front of it. Real rule versioning (`rule_versions`) replaces the
  timestamp-only `RuleFireAudit.RuleVersion` proxy, so "postings reference ruleId+ruleVersion" is
  literally true now.
- **Reuse the existing nightly-job pattern** (`PointsExpirationWorker`/`StreakMaintenanceJob`:
  self-scheduling `BackgroundService`, idempotent SQL) for every new scheduled/synthetic trigger
  (`birthdaybonus`, delayed-posting promotion) rather than introducing a new scheduling mechanism.
- **Additive only, throughout.** Every new rule type, event, and processor is new code alongside
  the old, not a rewrite of a working path — repeated explicitly as a design principle at each
  milestone rather than assumed.

## 3. As-implemented summary

All 11 milestones (M1–M11, one per CR, sequenced per the dependency graph in Part B) are complete
on this branch:

| Milestone | CR | What changed |
|---|---|---|
| M1 | CR-01 | `Shared/Events/EventTypes.cs`: 14 built-in events, each with category/source/cardinality/period/fields. No DB migration (event catalog is code, matching prior architecture). |
| M2 | CR-02 | 5 new rule types + handlers (`Calculation/{Redemption,Expiry,ManualAdjustment}RuleHandler.cs`, dedicated `Transfer`/`ReversalRuleProcessor`), `Rule.TargetAccountTypeId` made nullable for `ReversalRule`. |
| M3 | CR-04 | CASH approval gate (`PendingApproval` status, `CreatedBy`/`ApprovedBy`, `PATCH .../approve`); closed two self-discovered approval-bypass gaps (generic status endpoint, retargeting via update) before this milestone was reported done. |
| M4 | CR-03 | `RuleTypeCatalog.IsCompatible` as the single predicate; `GET .../rules/metadata`. |
| M5 | CR-05 | `ConditionTree`/`ConditionGroup`/`ConditionLeaf`/`ConditionValue` (new, parallel to the untouched flat `ConditionClause` DSL that Streak Campaigns keep using); `GroupedConditionEvaluator`; migration wraps existing rules' flat conditions into a single AND group so behavior is unchanged; `CardBucketConditionMapper` rewritten onto the new shape (see §5 for a wire-format gap this surfaced downstream, in the frontend). |
| M6 | CR-06 | `WinnerSelector` rewritten for the A6 stacking algorithm (exclusivity groups, priority-desc/ruleId-asc, additive+multiplier per wallet); `ExclusivityGroup`/`StackMode` columns, backfilled from each rule's target account name to preserve current behavior; the A6 worked example (500 SAR grocery → 480 points, 1 stamp) is a golden test. |
| M7 | CR-07 | `RuleLimits` grown from 2 to 8 fields + period/reset-window/on-breach; `RuleLimitEvaluator`/`PeriodWindow`. |
| M8 | CR-08 | `RuleSettings` (rounding/posting/holdDays/expiryOverrideDays/reversible/testMode/notifyOnAward); `held_postings` table + `DelayedPostingPromotionJob`/Worker; test-mode bypass threaded through `LedgerPoster`. |
| M9 | CR-09 | `RuleVersioningService` (insert-new-version-on-edit, `rule_versions`); `BudgetReservationService` (reserve-inside-the-posting-transaction, provider-aware Postgres `FOR UPDATE`/SQLite fallback); `BirthdayBonusJob` (deterministic idempotent `eventId`, Feb 29 → Feb 28 in non-leap years). |
| M10 | CR-10 | Confirmed already satisfied (engine stores only `contactKey`/balances/postings/counters, no profile data); net-new `POST customers/{ref}/birthday` (`customer_birthdays` table) as the one deliberate exception to the Customers module's read-only scope. |
| M11 | CR-11 | Angular rule builder rebuilt: `rule.model.ts` (discriminated calculation/limits/configuration shapes matching backend `[JsonPropertyName]` casing exactly), `rules.service.ts` (`getMetadata`/`approve`), `rule-form.page.ts` (metadata-driven cascading trigger→type→target, per-type calculation fields, expanded limits/configuration sections, `ConditionTreeEditor` integration), `rules-list.page.ts` (pending-approval status handling + approve action), new `shared/forms/condition-tree-dsl.ts` + `shared/ui/condition-tree-editor.ts` (grouped AND/OR editor, parallel to and not replacing the flat `condition-dsl.ts`/`ConditionsEditor` Streak Campaigns still use). |

Verification: `dEngage.Loyalty.Engine.Tests` 53/53 passing; `dEngage.Loyalty.IntegrationTests`
33/36 passing (3 pre-existing Docker/Testcontainers-dependent E2E failures, unrelated to this
branch, confirmed unavailable in this environment both before and after this change); Angular
`ng build`/`ng lint` clean across the whole app.

## 4. Blast radius

- **Schema** — 6 new migrations: `RuleTypeExpansionCr02`, `CashApprovalGateCr04`,
  `StackingResolutionCr06`, `RuleConfigurationCr08`, `EngineGuaranteesCr09`,
  `CustomerBirthdayCr10`. New tables: `rule_versions`, `rule_limit_counters`, `held_postings`,
  `customer_birthdays`. `rules` table: +8 columns (`exclusivity_group`, `stack_mode`,
  `configuration`, `current_version`, `created_by`, `approved_by`, plus `target_account_type_id`
  made nullable and `status` gains `pending_approval`). Backfills preserve existing-rule behavior
  (exclusivity group ← target account name; conditions wrapped in one AND group).
- **Ledger ↔ RuleEngine layering** — kept intact: `Ledger` cannot reference `RuleEngine` (would
  be circular), so `RefundService`'s new budget-release and reversible-check logic reads raw
  SQL/JSON rather than adding a project reference.
- **API surface** — new endpoints: `GET .../rules/metadata`, `PATCH .../rules/{id}/approve`,
  `POST customers/{ref}/birthday`. Changed: `RuleResponse`/`CreateRuleRequest`/
  `UpdateRuleRequest` DTO shapes (see `docs/SCOPE_BASELINE.md` for the full field list);
  `CreateCardBucketRequest`/`UpdateCardBucketRequest.AdditionalConditions` moved from the flat
  `ConditionClause` shape to the grouped `ConditionLeaf` shape as a consequence of `Rule`s and
  Card Buckets sharing storage (§5).
- **Frontend** — `programs/rules` rebuilt; `programs/card-buckets` required a follow-on fix (§5).
  `programs/streak-campaigns` intentionally untouched.
- **Docs** — `docs/SCOPE_BASELINE.md`, `docs/SOW.md` (§2.2, §2.3, §2.4, §3, §4), and
  `scripts/loyalty_schema_reference.md` all updated in this same change, per `CLAUDE.md`.
  `LoyaltySaaSApi.md` was **not** created — see §7.

## 5. Issues found and fixed during implementation (not in the original plan)

Several real defects surfaced while implementing and verifying this change — some in this
branch's own new code, some pre-existing — all fixed on this branch rather than left as
follow-on work:

1. **Card Buckets wire-format break.** `CardBucketConditionMapper` (M5) was rewritten onto the
   new `ConditionTree`/`ConditionLeaf` model, changing `CreateCardBucketRequest.AdditionalConditions`
   from `List<ConditionClause>` (`{field, op, value}`, bare scalar) to `List<ConditionLeaf>`
   (`{field, operator, value:{type,data,currency,inferred}}`) server-side — but the Card Buckets
   Angular page (`card-bucket-form.page.ts`, `card-bucket.model.ts`) still imported the old flat
   `ConditionClause` type and the flat `ConditionsEditor`, so its "additional conditions" escape
   hatch would have serialized the wrong JSON shape end-to-end. Fixed: `card-bucket.model.ts` now
   imports `ConditionLeaf` from the new `condition-tree-dsl.ts`; a new, narrowly-scoped
   `shared/ui/condition-leaf-list-editor.ts` replaces `ConditionsEditor` there — a flat list with
   no AND/OR grouping UI, matching the backend's own behavior (`CardBucketConditionMapper` always
   folds `AdditionalConditions` into a single AND group; offering OR-grouping in that form would
   promise something the API can't represent). `ConditionTreeEditor` (the full grouped editor)
   stays Rules-only.
2. **`rule_fire_audit.rule_version` cast failure (this branch's own bug).**
   `EngineGuaranteesCr09`'s `AlterColumn<int>` tried to convert the column directly from
   `timestamp with time zone` (the old timestamp-only version proxy this CR replaces) to
   `integer` with no `USING` clause — Postgres has no automatic cast between those types, so
   `dotnet ef database update` failed outright applying this migration to a real database with
   existing audit rows (surfaced applying this branch to the local dev database — see below).
   There is no meaningful conversion from a historical timestamp to a real version number, so the
   fix drops and re-adds the column instead of casting, defaulting existing rows to version 1 —
   the same "existing rows become version 1" reasoning the sibling `rules.current_version` column
   right above it in the same migration already used.
3. **Broken idempotent migration SQL — a systemic pre-existing pattern, not isolated.** Found
   first in this branch's own `20260921111821_StackingResolutionCr06.cs` (its exclusivity-group
   backfill `UPDATE` had no trailing semicolon), which prompted a full sweep of every
   `migrationBuilder.Sql(...)` call in the repo's migration history. Found and fixed **6 total**
   instances across **3 files** (1 new to this branch, 2 pre-existing from early September):
   `StackingResolutionCr06.cs` (1), `20260903170930_AddStreakCampaigns.cs` (2), and
   `20260903193446_TenantGuidSurrogateKey.cs` (3). All share the same shape: a raw SQL string
   with no trailing `;`, which `dotnet ef database update` applies fine as a single top-level
   command, but which becomes a PL/pgSQL syntax error once `dotnet ef migrations script
   --idempotent` wraps it in a `DO $EF$ BEGIN IF NOT EXISTS(...) THEN <sql> END IF; END $EF$;`
   block — because PL/pgSQL compiles the full block body at invocation time, the malformed SQL
   fails even for a migration that's already applied and whose `IF NOT EXISTS` would evaluate
   false. Verified fixed by regenerating `scripts/loyalty_schema.sql` end-to-end and running it
   unmodified against a throwaway empty database (`docker exec ... psql -v ON_ERROR_STOP=1`) —
   exit 0, all 28 tables present, history at the latest migration.
4. **The actual trigger for this section: the local `loyalty_dev` database was 7 migrations
   behind.** A live consumer/API run against it surfaced defects 2 and 3 above as real runtime
   failures (`relation "held_postings" does not exist`, `column r.approved_by does not exist`,
   then the cast failure once the missing migrations were applied) — not simulated. Fixed by
   stopping the locally running `dEngage.Loyalty.Api.exe` (it held `Schema.dll` locked, blocking
   the build `dotnet ef` needs) and running `dotnet ef database update` directly against
   `loyalty_dev`, after fixing defect 2. All 7 pending migrations (`RewardTypeTaxonomy` through
   `CustomerBirthdayCr10`) applied cleanly on the retry, preserving the database's existing
   tenant data throughout (transactional DDL rolled back the one failed attempt cleanly, per-row
   data untouched).
5. **Severity-critical: the CR-05 conditions backfill this document's own §6 risk warned about
   had, in fact, never been wired to run against real data — and every pre-existing rule across
   every tenant broke as a result.** Once the migrations above were applied, `RuleCacheService`
   started throwing `JsonException` and silently skipping every rule whose `conditions` column
   still held the pre-CR-05 flat `[{field,op,value}]` array (a JSON array at the root) instead of
   the new `{op,groups}` tree it now deserializes directly — confirmed against the real
   `loyalty_dev` data: 15 rules across every seeded tenant (`fintech`, `burgerking`, `starbucks`,
   `novapay`, `tiqmo`). `FlatConditionsMigrator` (the CR-05 converter) existed and was well unit
   tested, but nothing ever called it against a real row — it was built as a migration step to be
   run separately and that step was never written. Every one of those rules had stopped earning
   for its tenant.
   Fixed with a read-repair, not a separate migration step to remember: `RuleCacheService.
   LoadFromDbAsync` (the rule-engine matching path) now detects which shape a row is in via the
   new `FlatConditionsMigrator.ParseConditions(json, out bool wasFlat)`, converts on read, and
   persists the converted shape back onto the row — so it self-heals once per row, the moment
   that row is next loaded, with no separate deploy-time step for an operator to remember. The
   same gap existed independently in the admin API read paths (`RulesAppService.ToResponse`/
   `UpdateAsync`, `CardBucketsAppService.ToResponse`/`UpdateAsync` — 4 call sites total), which
   would have thrown viewing or editing any not-yet-healed rule (in particular `disabled`/
   `pending_approval` rows, which `RuleCacheService` never reaches since it only loads `active`
   ones); fixed the same way via the shared parser, without the write-back (those are read
   endpoints; the row heals for real the next time `RuleCacheService` loads it).
   Verified against the real `loyalty_dev` data, not just tests: ran the Consumer locally, watched
   all 15 rules go from `invalid — skipped` to `backfilled to the grouped shape` in the log,
   confirmed `SELECT jsonb_typeof(conditions::jsonb)` flipped from `array` to `object` for the
   affected rows and that zero `active` rules remain in the flat shape, then re-verified the fix
   a second time after refactoring `RuleCacheService` to call the shared parser instead of
   duplicating its detection logic. Added `ParseConditions`-specific tests to the existing
   `FlatConditionsMigratorTests` in `GroupedConditionEvaluatorTests.cs` (Engine.Tests): both
   shapes parse correctly, `wasFlat` reports accurately, `null` and empty-array inputs behave.

## 6. Risks (carried into `docs/SOW.md` §3 as known limitations)

- **CR-09 engine guarantees** touch money-adjacent transactional logic across multiple existing
  modules — treated as the gate for this branch, not an optional tail, per Part B's own guidance
  ("CR-09 should land before any production traffic").
- **CR-05 conditions migration** — this risk materialized for real during this branch (§5 item 5)
  and is now closed differently than originally planned: instead of a one-time deploy-time
  backfill step, `RuleCacheService` and the Rules/CardBuckets admin API self-heal each row's
  condition shape the moment it's next read, with no separate operational step required in any
  environment this branch deploys to.
- Two CR-09 gaps knowingly left open (approval item #12 above): cardinality check-then-write
  instead of a DB unique index; transactional counters cover rule budgets only.
- `TransferRuleProcessor`/`ReversalRuleProcessor` exist and are tested but are **not wired to any
  live event** — cutting them over is explicitly future work, to avoid double-processing against
  the pre-existing `points.transfer`/`order.refunded` handlers.
- `PointsExpirationJob` was **not** rerouted through an inbound `points.expired` event this
  branch, despite `points.expired` now existing as a built-in event type — the original plan
  flagged this as a real behavior change to an already-working nightly job, requiring careful
  before/after testing; deferred, not forgotten.

## 7. Known gap this change does not close

`LoyaltySaaSApi.md` (referenced by `web/CLAUDE.md` as "the API contract this app targets") does
not exist anywhere in this repository and was not recovered from prior history — the same
situation the original SOW was in before `docs/SOW.md` was authored from the codebase. It was not
created as part of this change: doing so properly means documenting all ~13 backend modules'
request/response contracts, not just the ones this change touched, which is a separate,
significantly larger undertaking than this scope change. Flagging it explicitly rather than
silently leaving it missing — recommend it become its own tracked piece of work (either a full
regeneration pass, or an OpenAPI/Swagger export if the existing Swagger UI at `/swagger` is judged
sufficient as the source of truth instead of a hand-maintained markdown file).

## 8. Explicitly out of scope (unchanged from the source docs)

TIER_POINTS / tier qualification & progression (deferred, approval item #2), reward catalog
management, partner/coalition earning, points purchase, fraud detection. Each requires its own
future Scope Change Request.
