# Scope Change Impact Analysis: Reward Type Taxonomy

**Status:** Approved 2026-09-17 by Product Owner + Architect — implemented on branch `feature/reward-type-taxonomy`
**Date:** 2026-09-17
**Author:** Claude Code, at the request of danishaliqau@gmail.com
**Affects:** `docs/SOW.md` §2.2 (Rewards row), `docs/SCOPE_BASELINE.md`, `scripts/loyalty_schema_reference.md`

This document is step 2 of the scope-change process defined in `CLAUDE.md`: it analyzes the
impact of a proposed change to the Reward domain, against the current `docs/SCOPE_BASELINE.md`
baseline and the as-built code.

## Approval record

Product Owner + Architect approved the direction in §3 on 2026-09-17 and resolved the open
decisions from §5 as follows:

1. **RewardType value list** — approved as proposed: `PointsBonus`, `Discount`, `Cashback`,
   `FreeProduct`, `GiftCard`, `TierUpgrade`.
2. **`coupon_type` in reward events** — confirmed no live tenant integration depends on it;
   cleared to remove.
3. **Streak Campaign → Reward FK** — **not** hardened. The soft, unenforced jsonb reference
   (`StreakCampaign.Config.reward.reward_definition_id`) stays as-is; this was explicitly
   declined, not deferred.
4. **`RewardLog.RewardType` naming collision** — resolved by renaming the field/column to
   `RewardName` (`reward_log.reward_name`), since it stores the reward's name, not a category.

Implemented on branch `feature/reward-type-taxonomy`; see the diff for the actual change (schema,
migration `RewardTypeTaxonomy`, API, event payloads). The Angular `programs/rewards` list/form
was updated in the same branch: `RewardType` + per-type `typeConfig` fields replace
`externalCouponType`, following the existing discriminated-config pattern from
`programs/account-types` (`reward.model.ts`, `reward-form.dialog.ts`, `rewards-list.page.ts`).

## 1. Why this change is being proposed

Reviewing Rewards as configured inside a Program surfaced three gaps against what an
"enterprise-level" reward catalog needs:

1. Every reward is forced to carry a required **`ExternalCouponType`** field, regardless of how
   the reward is earned or what kind of reward it is — this reads as a bolted-on, one-size-fits-all
   field rather than a considered part of the reward model.
2. There is no concept of **what a reward actually is**. The only classification that exists
   today is `Acquisition` — which describes *how* a customer earns a reward (points purchase,
   stamp completion, streak completion), not *what* they get (a discount, cashback, a free
   product, a gift card, a tier upgrade, ...). A real reward catalog needs both axes.
3. `Acquisition` itself is inconsistently enforced — two of its three values have required
   fields; the third (`streak_completion`) has none, so a streak-driven reward can be defined
   with no distinguishing configuration at all.

Design goal for the proposed model: it must **handle every reward type without special-casing**,
and stay **loosely coupled** — adding a new reward type in the future should mean adding a
registry entry and a validator, not a migration and a set of new required columns.

## 2. Current state (as-built)

### 2.1 `RewardDefinition` entity

`src/dEngage.Loyalty.Schema/Entities/RewardDefinition.cs`, table `reward_definitions`:

| Field | Type | Notes |
|---|---|---|
| `Name`, `DisplayName` | string | required |
| `Acquisition` | string | one of 3 constants — see 2.2 |
| `StampAccountTypeId` | Guid? | required only when `Acquisition = stamp_completion` |
| `PointsPrice`, `PointsAccountTypeId` | decimal? / Guid? | required only when `Acquisition = points_purchase` |
| `ExternalCouponType` | string, ≤255 chars | **always required**, free text, no whitelist |
| `IsActive` | bool | |

Scoped to a `Program` via a required FK (`ProgramId`, cascade delete on Program deletion); `Program`
has no back-collection navigation property — rewards for a program are queried directly by `ProgramId`.

### 2.2 `Acquisition`

Backed by string constants, not a real enum: `src/dEngage.Loyalty.Shared/Constants/RewardAcquisition.cs`
— `stamp_completion`, `points_purchase`, `streak_completion`. Whitelisted on create only
(`RewardsValidators.cs`); immutable after creation (absent from `UpdateRewardRequest`).
Conditional required-field rules exist for `stamp_completion` and `points_purchase`;
**`streak_completion` has none.**

### 2.3 "Coupon type" is not a reward type

There is **no `RewardType` concept anywhere in the code today.** `ExternalCouponType` is not a
category of reward — it's an opaque pass-through string, forwarded as `coupon_type` in the
outbound `loyalty.reward.earned` / `loyalty.reward.purchased` events
(`StampCompletionHandler.cs`, `RewardPurchaseHandler.cs`, `StreakCampaignModule.cs`), presumably
for an external/downstream coupon system to interpret. `docs/SOW.md` §2.2 documents this
verbatim: rewards are "mapped to an external coupon-system type code."

### 2.4 Reward ↔ Streak Campaign

Soft and unenforced. `StreakCampaign.Config` (jsonb) may embed `reward.reward_definition_id`
(`StreakConfig.cs`), validated only at the application layer
(`StreakConfig.Validate()`) and resolved by a runtime lookup in
`StreakCampaignModule.CompleteAsync`. There is **no foreign key and no EF navigation property**
between `streak_campaigns` and `reward_definitions`. If the referenced reward is missing or
inactive, the streak still completes and the failure is only a logged warning — no reward is
granted, silently.

`docs/SOW.md` §2.2 documents this relationship at the feature level: Streak Campaigns pay "a
fixed bonus or **linked reward** on completion."

### 2.5 Reward ↔ Card Bucket

**No relationship exists, in either direction, and this document proposes none.** A Card Bucket
is implemented as a `Rule` row tagged `Template = "card_bucket"`
(`src/dEngage.Loyalty.Api/CardBuckets/CardBucketsAppService.cs`) that pays a fixed points bonus
directly — it never references `RewardDefinition` in any form. `docs/SOW.md` §2.2 describes Card
Buckets purely as "a specialized fixed-bonus rule," consistent with this. This document simply
makes the absence of a relationship explicit so it isn't mistaken for a gap.

### 2.6 `RewardLog`

The only DB-enforced FK to `RewardDefinition` in the schema. Also carries a free-text
`RewardType` field — but today this actually stores the reward's **name**, not a category (see
`StampCompletionHandler.cs:75`, `RewardPurchaseHandler.cs:108`). This existing field name is a
naming collision with the taxonomy proposed below and will need to be accounted for during
implementation (see §5).

## 3. Proposed direction

### 3.1 Remove `ExternalCouponType`

Drop it from `RewardDefinition`, its API DTOs/validators, and the `coupon_type` field in the
outbound `RewardEarned`/`RewardPurchased` event payloads.

This is a genuine **scope reduction** against the current SOW wording ("mapped to an external
coupon-system type code") — not a refactor. It requires explicit Product Owner confirmation, not
just an engineering judgment call (see Risks, §6).

### 3.2 Introduce a first-class `RewardType`, orthogonal to `Acquisition`

- **`RewardType`** = *what* the reward is. **`Acquisition`** = *how* it's earned. These are
  independent axes and both are needed for an enterprise-grade catalog (e.g. a `Discount` reward
  could be earned via `points_purchase` or `streak_completion`).
- To satisfy "handle all types of rewards" without hard-coding a closed list, `RewardType` is
  proposed as a **string code validated against a registry**, not a database-level C# `enum` —
  the same pattern this codebase already uses for `Rule.Template`. Adding a new reward type
  becomes: register a type code + a validator for its `TypeConfig` shape. No migration, no new
  required columns, no changes to `RewardDefinition`'s shape.
- **Candidate initial set** (for PO/Architect discussion — not final): `PointsBonus`,
  `Discount`, `Cashback`, `FreeProduct`, `GiftCard`, `TierUpgrade`. The registry is designed so
  this list can grow later (e.g. `Voucher`, `ExperienceReward`) without another schema change.
- Each type's own fields live in a `TypeConfig` jsonb payload (§3.4), validated by whichever
  validator is registered for that type code. Core Reward code — the entity, EF configuration,
  and the generic CRUD in `RewardsAppService` — stays type-agnostic and never branches on a
  hardcoded list of types.

Illustrative (not final) per-type field shapes, to ground the registry idea:

| RewardType | Example `TypeConfig` fields |
|---|---|
| `Discount` | `discountKind` (percentage/fixed), `value`, `minPurchaseAmount?`, `maxDiscountAmount?` |
| `Cashback` | `amount`, `currency` |
| `FreeProduct` | `productSku`, `quantity` |
| `GiftCard` | `value`, `currency` |
| `TierUpgrade` | `targetTierId`, `durationDays?` |
| `PointsBonus` | `amount`, `accountTypeId` |

### 3.3 Formalize `Acquisition`

Unlike `RewardType`, `Acquisition` describes a small, stable set of engine-level triggers
(points purchase / stamp completion / streak completion) — this can be a proper closed C# enum
with an EF value conversion. Add the currently-missing conditional validation for
`streak_completion` (today: no required fields at all).

### 3.4 Schema approach for type-specific fields

A single `TypeConfig` jsonb column on `RewardDefinition`, mirroring the existing
`StreakCampaign.Config` pattern already proven in this codebase — not a wide table of nullable
columns, and not EF TPH inheritance. Both alternatives require a migration per new reward type,
which directly conflicts with the loose-coupling goal.

**Loose-coupling boundary:** consumers that need to *act* on a reward (grant it, display it,
hand it to a downstream system) should depend only on `RewardType` (string) + `TypeConfig`
(opaque jsonb) — never a switch statement enumerating every known type spread across modules. A
single type-registry/strategy seam per bounded context (API-layer validation; and, if/when
consumers need to act on type-specific payloads, RuleEngine/Consumer) keeps new types from
rippling through `StampCompletionHandler`, `RewardPurchaseHandler`, and `StreakCampaignModule`
individually.

This has a real limit, worth stating plainly: a type whose *fulfillment* needs synchronous
engine logic (e.g. `FreeProduct` needing an inventory check, `GiftCard` needing a call to an
issuing provider) cannot be fully generic — the registry isolates *validation and shape* per
type, but fulfillment logic still needs a handler per type wherever it actually executes.
"Loosely coupled" here means adding a type is additive (new registry entry), not that every type
requires zero new code anywhere.

### 3.5 Streak Campaign linkage — open decision, not committed

Flagging for the Architect: today a missing/deactivated `RewardDefinition` referenced by a
Streak Campaign fails silently (logged warning, streak still completes, no reward granted).
Hardening `StreakCampaign.Config.reward.reward_definition_id` into a real FK would make this a
loud, enforced failure instead. Not proposed as committed work — surfaced because "fields should
reflect respective details" naturally extends to this existing gap.

### 3.6 Card Buckets — no change

Confirmed as documentation-only: this analysis states plainly that Card Buckets and Rewards are,
and remain, unrelated. No code or schema change proposed here.

## 4. Blast radius

- **Schema** — `RewardDefinition` entity + `RewardDefinitionConfiguration` + new migration: drop
  `external_coupon_type`, add `reward_type` + `type_config` (jsonb). Existing rows need a
  backfill plan — no safe default `RewardType` can be inferred without business input per row.
- **API** — `src/dEngage.Loyalty.Api/Rewards/{RewardsDtos,RewardsValidators,RewardsAppService,RewardsModule}.cs`:
  remove `ExternalCouponType`, add `RewardType` + `TypeConfig`, add per-type and per-acquisition
  validation (including new `streak_completion` rules).
- **Event payloads / downstream integrations** — `StampCompletionHandler.cs`,
  `RewardPurchaseHandler.cs`, `StreakCampaignModule.cs` currently emit `coupon_type`. Removing it
  is a **breaking event-contract change** for any external consumer of
  `loyalty.reward.earned` / `loyalty.reward.purchased`. Requires explicit PO confirmation that no
  tenant integration currently depends on it.
- **`RewardAcquisition` → enum conversion** touches every consumer: `StampCompletionHandler.cs`,
  `RewardPurchaseHandler.cs`, `StreakCampaignModule.cs`, `RewardsValidators.cs`,
  `RewardDefinitionConfiguration.cs` (raw SQL filter string), `TestCli/RewardTest.cs`.
- **`RewardLog.RewardType` naming collision** — this existing field stores the reward's *name*
  today, not a category; introducing a new `RewardDefinition.RewardType` taxonomy field needs a
  deliberate decision on how (or whether) `RewardLog.RewardType` is renamed/repurposed to avoid
  confusion between the two.
- **Frontend** — `web/` reward list/form (`programs/rewards`, per `docs/SCOPE_BASELINE.md`
  frontend row) needs corresponding UI changes. Out of scope for this backend-focused analysis,
  flagged as follow-on work for the Architect.
- **Docs** — `docs/SCOPE_BASELINE.md`, `docs/SOW.md` §2.2, and `scripts/loyalty_schema_reference.md`
  all currently describe the "external coupon-system type code" behavior and must be updated in
  the same change if/when this is implemented, per `CLAUDE.md`.

## 5. Open decisions requiring explicit PO/Architect sign-off

1. Final `RewardType` value list (§3.2 candidates are a starting point, not a spec).
2. Confirmation that no live tenant integration depends on `coupon_type` in reward events before
   it's removed.
3. Whether to harden the Streak Campaign → Reward link into a real FK (§3.5).
4. How to resolve the `RewardLog.RewardType` naming collision (§4).

## 6. Risks

- Removing `ExternalCouponType` may break a live downstream coupon-system integration for some
  tenant — this is a business risk, not just a technical one.
- The `RewardType` taxonomy is proposed, not confirmed.
- Hardening `streak_completion` validation could reject data patterns silently tolerated today —
  recommend auditing existing `reward_definitions` rows with `acquisition = 'streak_completion'`
  before enforcing new rules.
- Pre-existing, unrelated to this change but worth flagging alongside it: the unique index
  `ux_reward_definitions_active_stamp` is keyed on `(tenant_id, stamp_account_type_id)`, not
  `program_id` — two programs sharing a stamp `AccountType` could collide.

## 7. Next step

Per `CLAUDE.md`: raise a Jira Scope Change Request referencing this document. Implementation
does not begin until Product Owner + Architect jointly approve the direction in §3 and resolve
the open decisions in §5.
