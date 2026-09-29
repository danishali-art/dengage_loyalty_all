# Scope Baseline — dEngage.Loyalty

Status: **Reconciled against `docs/SOW.md` v1.0** (an as-built SOW authored from the codebase,
since the original "Tiqmo SOW – 12th Aug 2026 v1.0" was not recoverable in this environment).
Pending Product Owner + Architect sign-off on `docs/SOW.md` §7 — once signed off, this table is
the locked scope: nothing is "in scope" without a row here. See the process in the repo-root
`CLAUDE.md`.

Legend: **Done** (implemented) / **In Progress** / **Not Started** / **Built-but-not-in-SOW**
(needs a PO decision: keep in scope, or explicitly cut).

## Backend — `src/dEngage.Loyalty.Api`

| Module | Capability | Status | SOW ref |
|---|---|---|---|
| AccountTypes | Wallet definitions: POINTS (precision 0–4 — used by Spend rules, expiry, "expiring soon" warning days, redemption, tier-qualifying flag — at most one per program, POINTS only, locked while the program is active), CASH (currency from a fixed whitelist, default SAR, locked after creation; precision; **no expiry**), STAMP (free-form). Immutable type after creation, no delete. (1.3.CL) | Done | §2.2 |
| Auth | JWT login for admin portal (email/password, PBKDF2) | Done | §2.7 |
| CardBuckets | MCC/amount/country/time-window targeted bonus rules (specialized FixedBonusRule) | Done | §2.2 |
| Complaints | Create/list/filter, inline status transitions | Done | §2.5 |
| ConfigVersions | Append-only audit trail for Program/Tier mutations, plus one aggregate `ProgramPublication` snapshot per Publish (program + account types + tiers + rewards + rules incl. card buckets + streak campaigns; 1.3.CL) | Done | §2.2 |
| Customers | Read-only 360° view: balances, tier progress, ledger, tier history; `POST customers/{ref}/birthday` (MM-DD only) feeds the CR-10 birthday-bonus job — a deliberate, minimal exception to the module's read-only scope | Done | §2.4 |
| Dashboard | Tenant-wide summary metrics, filterable by program/date | Done | §2.6 |
| Events | Ingestion endpoint + status lookup + built-in/generic type catalog; 14 built-in events carry category/source/cardinality/field metadata (CR-01), served read-only via `GET rules/metadata` | Done | §2.3 |
| Platform | Tenant provisioning (incl. partition creation), API keys, admin users | Done | §2.1 |
| Programs | Program CRUD, versioning; Draft → Published lifecycle (`POST .../publish`), unpublished-changes tracking across nested config, Active/Inactive only after publish (1.3.CL). Qualifying account and expiry warning moved to AccountTypes. | Done | §2.2 |
| Program publishing | `POST programs/{id}/publish`: Draft → Published; each publish writes one `ProgramPublication` ConfigVersion (own version sequence = publish number); prerequisites: ≥1 account type, and a tier-qualifying wallet when tiers exist; nested edits set `hasUnpublishedChanges`; edits stay live (no staging) (1.3.CL) | Done | §2.2 |
| Rewards | Catalog: Acquisition (points-purchase / stamp-completion / streak-completion) × RewardType (registry-validated: points_bonus / discount / cashback / free_product / gift_card / tier_upgrade), type-specific fields in `TypeConfig`. `ExternalCouponType` removed. | Done | §2.2 |
| Rules | 8 rule types (Spend/Stamp/FixedBonus/Redemption/Transfer/Reversal/Expiry/ManualAdjustment, CR-02) targeting POINTS/CASH/STAMP (CR-04, CASH requires a second admin's approval — creator cannot self-approve, `PATCH .../{ruleId}/approve`); grouped AND/OR condition tree (CR-05, replacing the old flat DSL for Rules/CardBuckets — Streak Campaigns keep the flat DSL unchanged); stackable/exclusive resolution (CR-06; named exclusivity groups and multiplier stacking retired by 1.3.CL — exclusive rules compete per target account type, stackable rules add); expanded limits — budget (total/per-period, reserved transactionally), cardinality, cooldown, min/max event amount, period/reset-window, on-breach clamp-or-skip (CR-07), each value validated server-side and in the portal (positive, ≤4 decimals, ≤2 for cooldown hours, whole-number max customers, period required for per-period caps, per-day/per-period ≤ total); per-rule configuration — rounding, immediate/delayed posting with a held-then-promoted state, hold/expiry override, reversible, test mode, notify-on-award (CR-08); rule versioning, insert-new-version-on-edit, postings reference ruleId+version (CR-09); single compatibility catalog (`GET rules/metadata`) drives both server-side validation and the Angular rule builder (CR-03, CR-11) | Done | §2.2 |
| StreakCampaigns | Consecutive-period campaigns, timezone-aware, restart/stop | Done | §2.2 |
| Tiers | Point-threshold ladder, lifetime/periodic qualification, grace period | Done | §2.2 |

**Program gating (1.3.CL):** the consumer evaluates a program only when it is **published and active** (`CampaignEvaluationService`, `RuleSyncService`, `BirthdayBonusJob`). Known remaining gap: the per-event redeem/transfer/reward-purchase handlers and the ledger jobs do not check program status — see `docs/SOW.md` §3.

## Backend — `src/dEngage.Loyalty.RuleEngine`

| Capability | Status | SOW ref |
|---|---|---|
| Condition evaluation: flat AND-only DSL (`ConditionClause`, Streak Campaigns only) + grouped AND/OR condition tree (`ConditionTree`, Rules/CardBuckets, CR-05) — two DSLs by design, not drift; Streak's matching was out of scope for this change | Done | §2.3 |
| Rule type handlers: Spend / Stamp / FixedBonus / Redemption / Transfer / Reversal / Expiry / ManualAdjustment (CR-02). Spend rounds down to the target POINTS wallet's `decimals` (0–4; 1.3.CL — other types unchanged). Transfer/Reversal post through dedicated processors (dual-entry / inherited-target) that bypass the shared winner-selection pipeline | Done | §2.2 |
| Winner selection: exclusive rules compete per target account type by priority-desc/ruleId-asc, stackable rules add on top, resolved independently per wallet (CR-06; named groups and multipliers retired by 1.3.CL — the multiplier code path remains until a clean-up CR) | Done | §2.3 |
| Tier evaluation (real-time upgrade) + nightly downgrade job, on the account type flagged tier-qualifying (1.3.CL) | Done | §2.2 |
| Streak campaign processing + nightly maintenance job | Done | §2.2 |
| Limits: per-customer total/daily (legacy, Redis-cached), max/min per event, cooldown, max distinct customers, rule budget (total/per-period, reserved inside the posting transaction), per-customer-per-period, on-breach clamp-or-skip, backed by a durable Postgres `rule_limit_counters` table (CR-07/CR-09) | Done | §2.2 |
| Rule versioning: edits insert a new version rather than mutating in place; ledger postings and reversals reference `ruleId + ruleVersion` for real (CR-09) | Done | §2.2 |
| Delayed posting: a held-postings state plus a nightly promotion job (same self-scheduling pattern as the points-expiration job) (CR-08) | Done | §2.2 |
| Deterministic scheduled/synthetic triggers: birthday bonus (idempotent per customer per year, Feb 29 pays on Feb 28 in non-leap years) (CR-09/CR-10) | Done | §2.2 |
| Ledger posting, stamp completion, rule-fire audit trail | Done | §2.3, §2.7 |

## Backend — Ledger, Schema, Consumer, Shared

| Module | Capability | Status | SOW ref |
|---|---|---|---|
| `Ledger` | Append-only, tenant-partitioned postings; refunds (capped); FIFO points expiry + "expiring soon" warnings driven by the POINTS account type's `warning_days` (1.3.CL); transactional outbox | Done | §2.4 |
| `Schema` | EF Core entities/migrations (Postgres, tenant-partitioned ledger/event tables) | Done | §4 |
| `Consumer` | RabbitMQ worker: ordered per-customer processing, per-event-type handlers (incl. CR-01's Signup/KycCompleted/CardTransaction/Remittance/PointsAdjusted/PointsExpired), all background/maintenance jobs (incl. CR-08's delayed-posting promotion and CR-09/CR-10's birthday-bonus workers) | Done | §2.3 |
| `Shared` | Constants, domain events, AES-256-GCM field-level config encryption | Done | §2.7 |

## Backend — `src/dEngage.Loyalty.Api.Framework`

| Capability | Status | SOW ref |
|---|---|---|
| Multi-tenancy (slug↔GUID resolution, tenant scoping enforcement) | Done | §2.7 |
| Auth pipeline: JWT + API-key, unified principal | Done | §2.7 |
| Redis-backed per-tenant rate limiting | Done | §2.7 |

## Frontend — `web/src/app/features`

| Feature | Capability | Status | SOW ref |
|---|---|---|---|
| auth | Login (no signup/reset/MFA) | Done | §2.8, §3 |
| dashboard | Tenant overview, filters, complaint breakdown | Done | §2.6 |
| customers | Search + read-only 360° profile | Done | §2.4 |
| complaints | Inline create + status management | Done | §2.5 |
| events | Event simulator (publish + poll status) | Done | §2.3 |
| platform | Tenants, API keys, tenant admins, tenant overview | Done | §2.1 |
| programs | List/overview/create/history; Draft/Published badge, Publish action, Active/Inactive switch, published-version history with sectioned snapshot view (1.3.CL) | Done | §2.2 |
| programs/account-types | Account type list/form: decimals info tooltip, expiry warning days, tier-qualifying toggle + badge, CASH currency dropdown, no CASH expiry (1.3.CL) | Done | §2.2 |
| programs/card-buckets | Card bucket list/form | Done | §2.2 |
| programs/rewards | Reward list/form: RewardType × Acquisition, per-type `typeConfig` fields | Done | §2.2 |
| programs/rules | Rule builder: metadata-driven cascading trigger→type→target selection (no hardcoded option lists — reads the same `rules/metadata` catalog the engine validates against), grouped AND/OR condition tree editor, per-type calculation fields, expanded limits, per-rule configuration, Stackable checkbox (exclusivity group / stack mode retired, 1.3.CL), list page grouped the way rules apply — by trigger event → target account, exclusive (highest priority wins) vs stackable (added on top), Transfer/Reversal listed separately (1.3.CL item 6 addendum), CASH approval action on the list page | Done | §2.2 |
| programs/streak-campaigns | Streak campaign list/form | Done | §2.2 |
| programs/tiers | Tier list/form + manual reordering | Done | §2.2 |
| layout (shell/sidebar/topbar/tenant-switcher) | Multi-tenant-aware navigation shell | Done | §2.8 |

**Not started / placeholder (flagged in sidebar, no implementation):** Reports, Settings,
functional global search. See `docs/SOW.md` §3 for the full known-limitations list, including
partial i18n coverage (en/tr, nav+auth+error strings only).

## Open items for Product Owner review

- Confirm none of the rows above should be `Built-but-not-in-SOW` — i.e., that everything
  currently built genuinely reflects intended scope, not accidental scope creep.
- Decide on each item in `docs/SOW.md` §3 (known limitations): accept as out-of-scope for now,
  or raise as a Scope Change Request.
- If phased delivery boundaries ("Faz1/Faz2/Faz3") from prior project history still apply,
  annotate which rows belong to which phase — this baseline currently treats everything above
  as one undifferentiated "current" scope.

## Revision history

| Date | Change | By |
|---|---|---|
| 2026-09-07 | Initial draft baseline from code inventory; SOW reconciliation pending | Claude Code |
| 2026-09-08 | Reconciled against new as-built `docs/SOW.md` v1.0; all rows cross-referenced | Claude Code |
| 2026-09-17 | Scope Change (approved): Reward model split into Acquisition × RewardType, `ExternalCouponType` removed, backend and frontend (`programs/rewards`) both updated. See `docs/scope-changes/2026-09-17-reward-type-taxonomy.md`. | Claude Code |
| 2026-09-22 | Scope Change (approved, CR-01 through CR-11): Rules engine rework — event taxonomy (14 built-ins with category/source/cardinality/fields), 5 new rule types, CASH approval gate, single compatibility catalog, grouped AND/OR conditions, formal stacking resolution, expanded limits with transactional budget reservation, per-rule configuration incl. delayed posting, rule versioning, customer birthday endpoint, full Angular rule-builder rebuild. TIER_POINTS deferred (still out of scope — tier qualification/progression unchanged). Fixed an incidental wire-format break this surfaced in `programs/card-buckets`' `additionalConditions` field (moved with the rest of Rules' condition storage from the flat DSL to the grouped one; the frontend hadn't been updated to match). See `docs/scope-changes/2026-09-22-rules-engine-taxonomy.md`. | Claude Code |
| 2026-09-28 | Scope Change (approved, 1.3.CL — program / account type changes): expiry warning days and the tier-qualifying account moved from Program to AccountType; Spend rules honour account-type decimals; CASH wallets lose expiry and get a currency whitelist; rule exclusivity groups and multiplier stacking retired, Stackable column added; Program Draft → Publish lifecycle with an Active/Inactive switch and one aggregate ConfigVersion snapshot per publish. Backend, migrations and portal updated together. See `docs/scope-changes/2026-09-28-program-account-type-changes.md`. | Claude Code |
