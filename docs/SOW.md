# Statement of Work — dEngage.Loyalty Platform

| | |
|---|---|
| **Document status** | v1.4 — DRAFT, pending Product Owner + Architect sign-off. Includes the 2026-09-30 scope change, **approved 2026-10-01**: items marked "Built (CR 2026-09-30, P1)" and "Built (CR 2026-09-30, P2–P4)" are implemented. Phase P5 (burn rules posting) is deferred, see §3. |
| **Basis** | Authored from the as-built codebase (reverse-engineered), not a prior contractual SOW |
| **Prepared** | 2026-09-08, by Claude Code |
| **Supersedes** | The original "Tiqmo SOW – 12th Aug 2026 v1.0" (not available in this repository/environment) |

## 0. Why this document exists

A prior SOW for this engagement (Tiqmo, dated 12 Aug 2026) is referenced in project history but
is not present in this repository or recoverable in the current environment. Rather than block
scope-lock on recovering it, this document was authored directly from the current implementation
so the team has an accurate, agreed baseline to lock *now*. It should be read, corrected, and
signed off by the Product Owner and Architect — anything below that misrepresents intended
scope (over- or under-states it) should be edited before this becomes the locked baseline in
`docs/SCOPE_BASELINE.md`.

Once approved, this SOW is the reference point for every future **Scope Change Request** (see
`CLAUDE.md` for the process): a request either extends this document or it isn't in scope.

## 1. Product summary

dEngage.Loyalty is a **multi-tenant SaaS loyalty engine**: a platform operator provisions
isolated tenants, each tenant runs one or more loyalty **programs**, and each program defines
its own wallets, earning rules, tiers, rewards, and campaigns. Customer-facing events (purchases,
cash loads, redemptions, transfers, etc.) flow in via an event-ingestion API, are processed
asynchronously by a rule engine, and post to an append-only ledger that drives customer balances,
tier status, and rewards. A web-based admin portal lets platform operators manage tenants and
lets tenant staff configure programs and support customers.

Two deployable applications make up the system:

- **Backend** (`src/`) — .NET, event-driven, Postgres + Redis + RabbitMQ.
- **Admin portal** (`web/`) — Angular, the operational UI for both platform operators and
  tenant staff.

## 2. In-scope capabilities

### 2.1 Platform administration (superadmin control plane)

Gated to a `platform_admin` role; tenant staff never see this.

- Tenant provisioning: create a tenant (immutable lowercase/underscore id + display name), which
  atomically creates the tenant record **and** its dedicated Postgres partitions for
  ledger/event tables — no manual DB scripting needed for onboarding.
- Tenant lifecycle: list, view, suspend/reactivate.
- API key management per tenant: issue (secret shown once, never retrievable again), list
  (prefix, created/last-used dates, revoked state), revoke.
- Tenant admin user provisioning: create `tenant_admin` accounts (email + temporary password)
  scoped to one tenant.

### 2.2 Program configuration (tenant-level)

Everything below is scoped to one tenant and, within it, to one program.

| Area | What can be configured |
|---|---|
| **Programs** | Create/edit a program (name, description); every new program starts as an inactive **Draft** and must be **Published** before it can be switched Active (Active/Inactive is a toggle that needs no publish). Any later change to the program or its account types, tiers, rewards, rules, card buckets or streak campaigns marks it as having unpublished changes; each Publish records one version snapshot of the whole program. Edits apply live — Publish is a version stamp, not a staging gate. Full change-history/versioning; delete only while inactive. (1.3.CL) **Built (CR 2026-09-30, P2–P4):** each program gets a **slug** (lowercase letters, digits and hyphens, no underscores; unique within the tenant), editable while the program is a Draft and locked once it is first published. It prefixes the program's reward names. Existing programs get a slug generated from their name, reviewed before deploy. |
| **Account Types (wallets)** | POINTS (decimal precision 0–4 — Spend rules round earned points down to it; optional FIFO expiration with an optional "expiring soon" warning N days before; optional redemption-to-another-wallet; optional points transfer between customers with a daily limit per sender, set on the account type (CR 2026-09-30 addendum A); optionally **the tier-qualifying wallet** — at most one per program, locked while the program is active), CASH (currency from a fixed list — SAR default, AED, KWD, QAR, BHD, OMR, USD, EUR, GBP, TRY — locked after creation; precision; **cash never expires**), STAMP (free-form config). Type is immutable after creation. (1.3.CL) |
| **Tiers** | Point-threshold ladder with lifetime or rolling-window ("periodic") qualification, grace-period demotion, manual ordering. Qualifying model locks once customers are assigned; deletion blocked with assigned customers or an active program. |
| **Rules** | 8 rule types: Spend (rate × amount), Stamp (+1 per transaction), FixedBonus, Redemption (points→wallet debit), Transfer (peer-to-peer, dual-entry under one rule), Reversal (reads the original posting, target inherited not configured), Expiry (scheduled, FIFO/LIFO), ManualAdjustment (operator-driven, reason-coded) — each targeting POINTS/CASH/STAMP per a single compatibility catalog the engine and the admin UI both read (no hardcoded option lists). CASH-targeted rules require a second admin's approval before they can fire (creator cannot self-approve). Conditions are a grouped AND/OR tree (money/number/string/list-typed values, typed per the trigger event's own field schema). Non-stackable (exclusive) rules compete per target wallet (priority-desc, then rule id); stackable rules add on top, resolved independently per wallet — named exclusivity groups and multiplier stacking were retired by 1.3.CL. Limits: per-customer total/daily/per-period, max/min per event, cooldown, max distinct customers, and a rule budget (total or per-period) reserved transactionally at posting time, each breach clamping to the remainder or skipping the rule entirely. Per-rule configuration: rounding, immediate or delayed (held-then-promoted) posting, an optional expiry override, reversibility, a test mode that audits without posting, and an on-award notification. Edits are versioned (a new version per edit, not an in-place mutation) so a posting or reversal always resolves against the rule as it stood at fire time. **Built (CR 2026-09-30, P1):** each burn trigger accepts only its own rule type: `points.transfer` → Transfer, `points.redeem` → Redemption, and `reward.purchase` → none (rewards are configured under Rewards, not rules). Existing rules that break this are disabled, not deleted. Redemption and Transfer rules stay configurable but are **not yet applied** by the engine (see §3). |
| **Card Buckets** | A structured authoring UI for MCC-code / amount-range / country / capture-status / time-window targeted bonus rules (implemented as a specialized fixed-bonus rule under the hood), always stackable, with per-customer daily/lifetime caps. |
| **Streak Campaigns** | "N consecutive qualifying periods" campaigns (day/week/month cadence, timezone-aware, sum-or-count aggregate threshold per period), paying a fixed bonus or linked reward on completion, with restart-or-stop behavior. **Built (CR 2026-09-30, P2–P4):** a linked reward must be an active, approved Streak-completion reward of the same program (checked when the campaign is saved), and a reward in use by an active campaign can't be deactivated or deleted. When the linked reward is Cashback or Tier upgrade, completing the streak now pays it out (see Rewards). |
| **Rewards** | Catalog of redeemable rewards, classified on two independent axes: how a reward is acquired (points purchase / stamp-card completion / streak completion) and what the reward actually is (a registry-validated `RewardType` — points bonus, discount, cashback, free product, gift card, or tier upgrade — each with its own type-specific fields). No longer mapped to an external coupon-system type code (removed 2026-09-17; see `docs/scope-changes/2026-09-17-reward-type-taxonomy.md`). Until CR 2026-09-30, a reward's type-specific details were stored but not acted on (earning or buying a reward only sent a `loyalty.reward.earned` notification); they are now paid out as below.<br>**Built (CR 2026-09-30, P2–P4):**<br>- **How it's acquired:** Purchase with points, or Streak completion. Stamp-card completion is retired.<br>- **What it is:** Cashback, or Tier upgrade. Points bonus, discount, free product and gift card are retired. Tier upgrade can only be earned through Streak completion.<br>- **Retired rewards:** existing rewards that use a retired value are deactivated (kept for history). Stamp cards keep earning stamps, but completions no longer resolve to a reward definition.<br>- **Cashback:** a currency from the same fixed list as CASH wallets, plus a CASH wallet in that currency. It needs a **second admin's approval** (the creator can't approve their own). On earning or purchase, the engine **credits the CASH wallet** through the ledger, and only for a published and active program (a purchase from a draft or paused program is refused before any points are taken).<br>- **Tier upgrade:** the engine **moves the customer up to the target tier** (never down), optionally protected from the nightly downgrade for a set number of days. The target tier must belong to the same program.<br>- **Reward names** start with the program slug (for example `fintech_cashback_50`), and an active reward name is unique within the tenant. Existing names are kept until renamed.<br>- **The Add Reward form** asks for the acquisition first and then offers only the reward types allowed with it. |
| **Configuration history** | Every program/tier mutation is versioned (who/when/what changed, full snapshot), viewable per entity; in addition every Publish records one aggregate snapshot of the program and all its nested configuration, numbered by publish (v1, v2, …). (1.3.CL) |

### 2.3 Event ingestion & processing

- **Ingestion API**: accepts 14 built-in event types — the original 7 (`order.created`,
  `order.refunded`, `cash.added`, `cash.spent`, `points.redeem`, `points.transfer`,
  `reward.purchase`) plus `signup`, `kyc.completed`, `card.transaction`, `remittance`,
  `points.adjusted`, `points.expired` (inbound, published by the expiry job rather than posted
  directly), and `birthdaybonus` (a scheduled synthetic trigger, not caller-published) — plus
  tenant-approved generic event types. Each built-in carries a category (Earn/Burn/Reverse/Adjust),
  source (Behavioural/Scheduled/Operator), cardinality, and field schema, served read-only via
  `GET rules/metadata` for both server-side rule validation and the admin rule builder. Operator
  (`Scheduled`-source) events cannot be posted through the public ingestion endpoint. Rate-limited
  per tenant, always asynchronous (fire-and-forget with a trackable event id).
- **Processing pipeline**: idempotent inbox pattern, guaranteed per-customer ordering, rule
  matching → winner selection (priority-based exclusivity + independent stacking, most rule
  types) or a dedicated dual-entry/inherited-target processor (Transfer/Reversal) → ledger
  posting (immediate, or held for delayed rules and promoted by a nightly job) → stamp-completion
  handling → real-time tier upgrade → outbound event publication — all within one DB transaction
  per triggering event. Budget-capped rules reserve their spend inside that same transaction.
- **Batch/maintenance jobs**: nightly tier downgrade & re-qualification, points FIFO expiration
  (+ advance "expiring soon" warnings, configured per POINTS wallet), streak maintenance (recompute, break-detection, data
  retention), delayed-posting promotion, birthday-bonus evaluation (idempotent per customer per
  year), transactional outbox publishing with retry/backoff, event-log retention.
- **Event Simulator** (admin portal): lets support/ops staff publish a synthetic event and watch
  it move through the pipeline (pending → processed/failed) without a real integration —
  useful for validating a program configuration end-to-end. **Built (CR 2026-09-30, P1):** the
  simulator no longer offers the internally scheduled types (`birthdaybonus`, `points.expired`),
  which the API always rejects. For each event type it shows which fields are required, and which
  values must be sent as strings.

### 2.4 Customer & ledger management

- 360° read-only customer view: balances across all wallets, current tier + progress to next
  tier, tier-change history, paginated transaction ledger — searchable by contact key.
- One deliberate exception to that read-only scope: `POST customers/{ref}/birthday` (MM-DD only)
  registers a customer's birthday so the birthday-bonus rule/job can fire — no other customer
  field is writable from the admin API.
- Append-only ledger as the system of record, partitioned per tenant, with a fixed reason-code
  dictionary (earn, stamp_earn, stamp_reset, cash_load/spend, redemption, refund, expiry,
  transfer, etc.) and idempotency guarantees against duplicate processing.
- Points-to-cash redemption and peer-to-peer points transfer (daily cap, deadlock-safe dual
  locking, business-outcome failure events rather than hard errors). **Built (CR 2026-09-30, P1):**
  redemption, like transfer, reports business outcomes as events — `loyalty.points.redeem_failed`
  for below-minimum or insufficient points (the event is processed, not dead-lettered) and
  `loyalty.points.redeemed` on success. Before this, a failed redemption was dead-lettered with no
  failure event.
- **Built (CR 2026-09-30, P2–P4):** a `reward.purchase` event may identify the reward by `reward_id`
  as well as by `reward_name`.
- Refunds: proportional reversal of a prior order's earn entries, capped so cumulative refunds
  can't exceed the original grant.

### 2.5 Complaints

Lightweight customer-service ticketing: create, list/filter by status and program, inline
status transitions (open → in progress → resolved), status-count summary surfaced on the
dashboard. Not a full ticketing system — no assignment, comments, or attachments.

### 2.6 Dashboard

One tenant-wide overview: program/tier/customer/account counts, total balance, redemption
count, active/completed streaks, complaint breakdown — filterable by program and date range.

### 2.7 Security, multi-tenancy & platform architecture

- **Multi-tenancy**: every tenant-scoped table keyed by an internal GUID, addressed everywhere
  externally (URLs, JWTs, API-key prefixes, RabbitMQ routing, partition names) by an immutable
  slug; cross-tenant access blocked except for `platform_admin`.
- **Auth**: JWT (HMAC-SHA256) for admin-portal sessions; hashed, prefixed API keys for
  server-to-server ingestion; PBKDF2 password hashing throughout. Two roles only:
  `platform_admin` (cross-tenant) and `tenant_admin` (bound to one tenant).
- **Rate limiting**: Redis-backed, per-tenant, fixed-window, shared across API instances.
- **Field-level encryption**: AES-256-GCM for application-config secrets (not customer PII),
  keyed by an out-of-band master key.
- **Auditability**: rule-fire audit trail on every earn event, full config-change versioning,
  append-only ledger — the system is designed to reconstruct "why did this customer get X" after
  the fact.

### 2.8 Admin portal UX

- Multi-tenant-aware shell: tenant switcher (static label for tenant admins; searchable
  switch-tenant dialog for platform admins, with a forced "select a tenant" interstitial before
  any tenant-scoped screen renders for a platform admin with none selected).
- Contextual sidebar sub-navigation that adapts to "inside a program" vs. "inside a platform
  tenant."
- Partial localization scaffolding: English and Turkish string tables exist for navigation,
  auth, and error/interstitial pages (see §3 for what this does *not* cover).

## 3. Explicitly out of scope / known limitations (as-built)

These are gaps found during the code review, not commitments — each needs a Product Owner
decision: accept as out-of-scope, or raise as a Scope Change Request.

- **No self-service account recovery**: no signup, password reset, or MFA flow for admin users;
  accounts are provisioned manually by a platform admin and passwords shared out-of-band.
- **No refresh-token flow** — JWT sessions only.
- **Coarse-grained RBAC**: exactly two roles (`platform_admin`, `tenant_admin`); no read-only or
  per-feature permission levels.
- **No manual balance/account editing** from the admin UI — the customer view is strictly
  read-only; corrections would require direct data operations. `ManualAdjustmentRule` (CR-02)
  does not reopen this: it is driven by an operator-published `points.adjusted` event through the
  normal rule-matching pipeline, not a direct admin-UI edit.
- **CASH-rule approval is an identity check, not a role**: "a different admin than the creator"
  is enforced by comparing `CreatedBy`/`ApprovedBy`, because current RBAC has only
  `platform_admin`/`tenant_admin` with no per-feature permission levels to gate a real
  "approver" role on. A fuller approval workflow (roles, multi-step, notifications) is a future
  Scope Change Request if the Product Owner wants more than this.
- **TIER_POINTS is deferred**: not a wallet kind today (POINTS/CASH/STAMP only). Tier
  qualification/progression stays computed on the fly from POINTS/STAMP ledger history, as
  before — building a real TIER_POINTS wallet would reopen that out-of-scope feature and needs
  its own Scope Change Request.
- **Rule configuration's `posting: "Pending"`** (post-then-confirm-on-settlement) is accepted by
  neither validator nor engine — only `Immediate` and `Delayed` are implemented. `Pending` needs
  a settlement-confirmation event vocabulary this system doesn't have yet.
- **Two acknowledged gaps in the CR-09 engine guarantees**: `OncePerCustomer`/`OncePerPeriod`
  cardinality is enforced by check-then-write, not a DB unique index (a narrow race window under
  true concurrency); and transactional counters cover rule budgets only — per-customer/per-event
  limits outside a budget still read a cache rather than a counter inside the posting
  transaction. Both are flagged, not silently accepted — revisit before this rule engine carries
  adversarial-scale traffic.
- **Complaints is lightweight**: no assignment, ownership, comments, or attachments.
- **Program status is only partly enforced** — the consumer evaluates earn rules, campaigns and
  the birthday bonus only for programs that are **published and active** (1.3.CL; the earlier
  note that `programs.status` was not enforced at all was inaccurate). The per-event redeem,
  transfer and reward-purchase handlers and the ledger jobs (expiry, expiring-soon warnings) still
  do not check program status, so pausing a program does not stop those. One exception since
  CR 2026-09-30: a cashback reward is only sold and paid for a published and active program.
- **Redemption and Transfer rules are not applied (as-built, unchanged by CR 2026-09-30).** They
  can be created, but the engine never fires them: redeem and transfer are driven by the POINTS
  account type's settings. Making them effective (rule-driven parameters, a cash leg for
  Redemption, one posting path per event) is specified in the 2026-09-30 scope change (phase P5)
  but **deferred** to a separate decision.
- **Burn-side money controls not covered by CR 2026-09-30 (as-built gaps, awaiting a decision):**
  - a streak fixed bonus can pay into a CASH wallet without second-admin approval;
  - points-to-cash redemption isn't gated on program status;
  - one admin can set or raise the redemption rate with no approval;
  - the redemption target wallet and a streak's target wallet aren't checked to be the right type
    or to belong to the same program.
- **CASH balances can't be transferred between customers or redeemed (by design, CR 2026-09-30
  addendum A).** Cash is the end of the value chain: it is spent (`cash.spent`) or paid out by
  the tenant's own payment system. Customer-to-customer cash is money transmission (licensing,
  KYC/AML) and stays out of the loyalty platform; a licensed tenant that needs it raises its own
  Scope Change Request (per-tenant flag, KYC gate, money limits, approval, monitoring events).
- **STAMP account-type config has no fixed schema** — accepts arbitrary JSON, unlike POINTS/CASH
  which are structurally validated.
- **Account Types cannot be deleted**, only created and edited.
- **Reports and Settings** appear as feature-flagged placeholders in the portal's sidebar but
  have no implementation behind them.
- **Global search (topbar)** is a visual placeholder, not functional.
- **Partial i18n**: only navigation, auth, and error/interstitial strings are translated
  (en/tr); the majority of admin-portal screens have hard-coded English copy.
- **Card Buckets and Streak Campaigns** are reachable via routing/sidebar but are not surfaced
  as summary cards on the Program Overview page (only Account Types, Tiers, Rules, Rewards,
  History are).

## 4. Technical architecture (for reference, not a deliverable in itself)

- **Backend services**: `Api` (HTTP, Nancy-based modules), `Api.Framework` (tenancy/auth/rate
  limiting/validation), `Consumer` (RabbitMQ worker + all background jobs), `Engine.Framework`
  (shared dispatch abstractions), `RuleEngine` (matching/calculation/tiers/streaks), `Ledger`
  (append-only postings, refunds, expiry, outbox), `Schema` (EF Core entities/migrations),
  `Shared` (constants, events, field-level crypto).
- **Data/infra**: PostgreSQL (tenant-partitioned ledger/event tables; `rule_limit_counters` is
  the durable source of truth for budget/cardinality counters, Redis caches in front of it),
  Redis (rule/campaign cache, rate limiting), RabbitMQ (event transport + dead-lettering).
- **Scheduler additions (CR-08/CR-09/CR-10)**: delayed-posting promotion and birthday-bonus
  evaluation follow the same self-scheduling `BackgroundService` + idempotent-SQL pattern as the
  pre-existing points-expiration and streak-maintenance jobs — no new scheduling mechanism was
  introduced.
- **Frontend**: Angular 22, standalone/zoneless/signals, feature-based module boundaries,
  Tailwind v4 design system, decimal-string money handling.

## 5. Roles & responsibilities

| Role | Responsibility under this SOW |
|---|---|
| Product Owner | Owns business intent; approves this SOW and every future Scope Change Request jointly with the Architect. |
| Architect | Owns technical feasibility/impact assessment; approves scope changes jointly with the PO; reviews implementation diffs before merge. |
| Developers | Implement approved changes; keep `docs/SCOPE_BASELINE.md` and this SOW's affected sections current in the same change. |
| Frontend engineer | Owns `web/` — portal UX, i18n coverage, and the conventions in `web/CLAUDE.md`. |
| Claude Code | Produces impact analyses for proposed scope changes and implements approved ones, per the process in root `CLAUDE.md`. |

## 6. Change control

Any change to the scope described in §2, or any addition, follows the process defined in the
repo-root `CLAUDE.md`: Scope Change Request → Claude Code impact analysis → joint PO+Architect
approval → implementation with docs updated in the same change → Architect review → merge.
This SOW is versioned; a material approved change should bump its version and add a row to the
revision history below.

## 7. Sign-off

| Role | Name | Date | Decision |
|---|---|---|---|
| Product Owner | | | ☐ Approved ☐ Changes requested |
| Architect | | | ☐ Approved ☐ Changes requested |

## Revision history

| Version | Date | Change | By |
|---|---|---|---|
| 1.0 | 2026-09-08 | Initial as-built SOW, authored from codebase in absence of the original Tiqmo SOW | Claude Code |
| 1.1 | 2026-09-17 | Scope Change (approved): Reward model split into Acquisition × RewardType. See `docs/scope-changes/2026-09-17-reward-type-taxonomy.md`. | Claude Code |
| 1.2 | 2026-09-22 | Scope Change (approved, CR-01–CR-11): Rules engine rework — §2.2 Rules, §2.3 event taxonomy/pipeline, §2.4 birthday endpoint, §3 known limitations, §4 architecture. See `docs/scope-changes/2026-09-22-rules-engine-taxonomy.md`. | Claude Code |
| 1.3 | 2026-09-28 | Scope Change (approved, 1.3.CL — program / account type changes): §2.2 Programs (Draft → Publish, Active toggle), Account Types (warning days, tier-qualifying flag, decimals, CASH currency/no expiry), Rules (exclusivity groups and multipliers retired), Configuration history (publish snapshots); §2.3 warnings; §3 program-status limitation corrected. See `docs/scope-changes/2026-09-28-program-account-type-changes.md`. | Claude Code |
| 1.4 (draft) | 2026-10-01 | Scope change 2026-09-30 **approved** 2026-10-01. Phase P1 is built (Rules allowlist, Event Simulator, redemption outcome events — marked "Built"); phases P2–P4 built the same day (marked "Built (CR 2026-09-30, P2–P4)"); P5 deferred. Items: §2.2 Programs (program slug), Rules (burn-trigger rule-type allowlist), Streak Campaigns (reward eligibility), Rewards (Purchase with points / Streak completion × Cashback / Tier upgrade, engine fulfilment, cashback approval, slug-prefixed names); §2.3 Event Simulator; §2.4 redemption outcome events and `reward_id`; §3 burn-rule and burn-side money-control limitations. See `docs/scope-changes/2026-09-30-reward-acquisition-fulfilment.md`. | Claude Code, at the request of Moiz |
| 1.4 (draft) | 2026-10-01 | CR 2026-09-30 addendum A: §2.2 Account Types (points-transfer daily limit); §3 CASH transfer/redeem kept out of scope by design. | Claude Code, at the request of Moiz |
