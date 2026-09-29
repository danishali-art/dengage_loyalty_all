# Loyalty Rules Engine — Requirements and SOW Change Log

Version 1.0 · September 2026

---

# Part A — Final requirements for Rules

## A1. Model

A rule has six parts. They resolve in one direction; nothing upstream depends on anything downstream.

```
Trigger event ──► category, source, cardinality, field schema
      │
      ├──► Rule type   (filtered by category + required payload kinds)
      │        │
      │        ├──► Target account (filtered by rule type)
      │        └──► Calculation fields (shape set by rule type)
      │
      ├──► Conditions  (field suggestions from schema, free-text paths allowed)
      └──► Limits      (cardinality locks the lifetime cap)
```

## A2. Event taxonomy

Every event declares four attributes. These drive the whole UI.

| Event | Category | Source | Cardinality |
|---|---|---|---|
| `signup` | Earn | Behavioural | OncePerCustomer |
| `kyc.completed` | Earn | Behavioural | OncePerCustomer |
| `birthdaybonus` | Earn | Scheduled | OncePerPeriod (Yearly) |
| `card.transaction` | Earn | Behavioural | Unlimited |
| `order.created` | Earn | Behavioural | Unlimited |
| `remittance` | Earn | Behavioural | Unlimited |
| `cash.added` | Earn | Behavioural | Unlimited |
| `cash.spent` | Earn | Behavioural | Unlimited |
| `points.redeem` | Burn | Behavioural | Unlimited |
| `reward.purchase` | Burn | Behavioural | Unlimited |
| `points.transfer` | Burn | Behavioural | Unlimited |
| `order.refunded` | Reverse | Behavioural | Unlimited |
| `points.expired` | Adjust | Scheduled | Unlimited |
| `points.adjusted` | Adjust | Operator | Unlimited |

**New constants required:** `points.expired`, `points.adjusted`.

## A3. Rule types

| Rule type | Category | Requires | Valid targets |
|---|---|---|---|
| `FixedBonusRule` | Earn | — | POINTS, CASH, TIER_POINTS |
| `SpendRule` | Earn | money field | POINTS, CASH |
| `StampRule` | Earn | — | STAMPS |
| `RedemptionRule` | Burn | points field | POINTS |
| `TransferRule` | Burn | points + recipient | POINTS |
| `ReversalRule` | Reverse | money + originalEventId | inherited from original posting |
| `ExpiryRule` | Adjust | — | POINTS |
| `ManualAdjustmentRule` | Adjust | operator + reason | any |

**Compatibility predicate**, used identically by the UI filter and the engine matcher:

```
compatible(event, rule) =
      event.category == rule.category
  AND rule.requiredKinds ⊆ kindsOf(event.fields)
```

## A4. Account kinds

| Kind | Precision | Expires | Notes |
|---|---|---|---|
| POINTS | integer | yes, lot-based | Non-monetary liability |
| CASH | 2dp | no | Real money. Approval + budget cap mandatory |
| STAMPS | integer | resets on completion | Not fungible, never transfers |
| TIER_POINTS | integer | period reset | Status only, never burns |

## A5. Conditions

Fixed two-level structure. No arbitrary nesting.

```json
{
  "op": "AND",
  "groups": [
    { "op": "AND", "conditions": [
      { "field": "tx.mcc",    "operator": "in",  "value": { "type": "string[]", "data": ["5411","5541"] } },
      { "field": "tx.amount", "operator": "gte", "value": { "type": "money", "data": 100, "currency": "SAR" } }
    ]},
    { "op": "OR", "conditions": [
      { "field": "profile.segment", "operator": "eq", "value": { "type": "string", "data": "premium" } }
    ]}
  ]
}
```

**Field paths are free text**, with schema-declared paths offered as autocomplete. Undeclared paths are permitted because callers control the payload; they are flagged in the UI, carry `"inferred": true` in the serialised value, and resolve at runtime.

**Namespaces:** `tx.*` payload · `event.*` envelope-derived · `profile.*` caller-supplied snapshot · `agg.*` computed from ledger history · `operator.*` adjust events only.

**Operators by type**
- money, number: `gte lte between eq neq`
- string: `eq neq in not_in starts_with`
- undeclared: full set plus `exists`, `is_null`

**Missing-field semantics (must be fixed and documented):** a field absent from the payload evaluates `false` for positive operators and `true` for `not_in` / `neq` / `is_null`.

**UI invariants**
1. At least one group; last group cannot be deleted.
2. At least one condition per group; last condition cannot be deleted.
3. Adding a group seeds it with one condition.
4. Changing the trigger resets the condition tree.
5. Empty field path or empty value blocks save.

## A6. Stacking

**Definition:** within one event, for each exclusivity group, at most one non-stackable rule fires. Stackable rules belong to no group and always fire when matched.

**Resolution algorithm**

```
1. Partition matched rules into exclusive[] and stackable[]
2. Group exclusive[] by exclusivityGroup
   per group: sort by priority DESC, then ruleId ASC → winner; discard rest
3. base[] = group winners
4. Calculate each base rule
5. Split stackable[] into multiplier[] and additive[]
6. Per wallet:
     total = sum(base for wallet)
     total = total × product(multipliers for wallet)
     total = total + sum(additives for wallet)
7. Apply caps, round once per account kind, post
```

**Rules**
- Multipliers apply to the base subtotal only, never to other stackables.
- Multipliers compose multiplicatively (×2 and ×1.5 = ×3).
- A multiplier with no base in its wallet is a no-op.
- Multipliers must be stackable; an exclusive multiplier is invalid.
- Stackable rules serialise `exclusivityGroup: null`.
- Tiebreak is deterministic: equal priority resolves to lowest ruleId.
- Wallets resolve independently.

**Default for v1:** exclusivity group defaults to the target account name, giving one exclusive rule per wallet per event, with the escape hatch available when two independent competitions are needed in one wallet.

**Worked reference case** — 500 SAR grocery transaction, grocery 3% (priority 200) beats base 1% (priority 100) in group `earn-rate`; monthly bonus 200 wins group `one-off`; weekend ×2 and birthday +50 stack; coffee stamp wins group `stamp-card`.
Result: `((15 + 200) × 2) + 50 = 480 points, 1 stamp`.

## A7. Limits

| Limit | Enforcement key |
|---|---|
| Per customer, total | `(ruleId, customerRef)` |
| Per customer, per period | `(ruleId, customerRef, periodKey)` |
| Max per event | clamp at calculation |
| Minimum event amount | evaluated pre-calculation |
| Cooldown hours | last posting timestamp |
| Max customers | `(ruleId)` distinct customers |
| Rule budget, total | `(ruleId)` |
| Rule budget, per period | `(ruleId, periodKey)` |

**Period:** Day · Week · Month · Year
**Reset window:** Calendar (midnight, program timezone) or Rolling (trailing window)
**On breach:** Clamp to remaining headroom, or Skip

**Cardinality locks**
- `OncePerCustomer` → per-customer total forced to 1, field disabled, enforced by unique index.
- `OncePerPeriod` → guarded on `(customerRef, ruleId, periodKey)`, field disabled.

## A8. Configuration

| Setting | Values | Default |
|---|---|---|
| Rounding | inherit / down / nearest / up | inherit from program |
| Posting | Immediate / Pending / Delayed | Immediate |
| Hold period | days | 0, required when Delayed |
| Expiry override | days | inherit from program |
| Reversible | yes / no | yes |
| Test mode | on / off | off |
| Notify on award | on / off | off |

**Inheritance order:** program default → account kind → rule override. Inherited values display greyed with their source.

## A9. Validation

**Blocking**
- Empty rule name
- Non-stackable rule with no exclusivity group
- Unsatisfiable condition group (`field gte X` AND `field lte Y` where X > Y)
- CASH target with no budget cap
- `cash.added` targeting CASH (earning loop)
- Empty condition field path or value
- Delayed posting with no hold period

**Warning**
- Stackable earn rule with no budget cap
- Earn rule on `card.transaction` with no `tx.status` check
- Earn rule with no cap of any kind
- Undeclared condition field paths, listed by name
- Spend rate above 50%
- Test mode enabled

## A10. Engine guarantees

1. **Idempotency** — unique on `(tenantId, eventId)`. Caller supplies `eventId`, unique per business event, never generated server-side.
2. **Cardinality** — unique index, not check-then-write.
3. **Counters** — incremented in the same database transaction as the posting.
4. **Budget reservation** — reserve, post, confirm; or document the overshoot tolerance.
5. **Reversal releases counters** consumed by the original posting.
6. **Reversal reads history**, not current config. Runs even when the original rule is deactivated.
7. **Rule versioning** — edits create a new version with an effective date. Postings reference `ruleId + ruleVersion`.
8. **Ledger** — append-only, double-entry. Reversals are compensating entries, never deletes.
9. **Resolution audit** — per event, persist winners with their group, losers with the winning ruleId, multipliers in application order, and the running total at each step.
10. **Scheduled triggers** — synthetic events keyed deterministically, e.g. `birthday:{customerRef}:{year}`. Leap-day policy documented.

## A11. Customer data boundary

The engine stores no customer profile. It holds an opaque `customerRef`, account balances, ledger postings and counters.

- `customerRef` must be the caller's immutable internal id, never an email or phone number.
- `profile.*` condition values arrive in the event payload and are persisted with the posting as the value at award time.
- `agg.*` values are computed from the engine's own posting history.
- Birthday scheduling requires a minimal registration endpoint accepting `customerRef` + `MM-DD` only, or caller-pushed events.

---

# Part B — SOW Change Log

Paste into the SOW as a change-control annex. Section references marked `[§]` need mapping to your existing numbering.

## CR-01 · Event taxonomy extension

**Baseline.** 7 system events plus 5 generic events, flat, uncategorised.
**Change.** Add `category`, `source`, `cardinality` and `period` attributes to every event definition. Add two events: `points.expired`, `points.adjusted`.
**Impact.** `[§]` Event Catalogue, `[§]` Data Model. New migration on the event definition table.
**Rationale.** Category drives rule-type filtering and posting sign. Without the two new events, expiry and manual correction bypass the rules engine and the ledger has untraceable entries.

## CR-02 · Rule type expansion

**Baseline.** 3 rule types: `FixedBonusRule`, `SpendRule`, `StampRule`.
**Change.** Add 5: `RedemptionRule`, `TransferRule`, `ReversalRule`, `ExpiryRule`, `ManualAdjustmentRule`.
**Impact.** `[§]` Rules Engine, `[§]` Ledger Service. Burn and reverse paths move from application code into the rules engine.
**Rationale.** 4 of 11 baseline events had no valid rule type. Users selecting them reached an unusable form.

## CR-03 · Metadata-driven field binding

**Baseline.** Trigger event and rule type independently selectable.
**Change.** Rule type filtered by event category and required payload kinds. Target account filtered by rule type. Single compatibility predicate shared by UI and engine.
**Impact.** `[§]` Admin UI, `[§]` Rules Engine.
**Rationale.** Prevents invalid combinations at configuration time rather than at runtime. One source of truth eliminates UI/engine drift.

## CR-04 · Account kind constraints

**Baseline.** Target account free choice between Points and Cash.
**Change.** Four account kinds with per-kind precision, expiry and transfer semantics. Target filtered by rule type. CASH targets require approval workflow and budget cap.
**Impact.** `[§]` Account Model, `[§]` Approval Workflow (new).
**Rationale.** CASH is a real-money liability. A misconfigured rate is a financial loss, not a points adjustment.

## CR-05 · Grouped conditions with AND/OR

**Baseline.** Flat condition list, all ANDed, free-text field names, untyped string values.
**Change.** Two-level structure with selectable operators at both levels. Typed values with currency on money fields. Schema autocomplete with free-text paths retained. Expanded operator set. Defined missing-field semantics.
**Impact.** `[§]` Admin UI, `[§]` Rules Engine, `[§]` Data Model. Migration required for existing conditions: wrap each rule's flat list in a single AND group under an AND root.
**Rationale.** OR at group level is required for common campaign logic. Typed values remove the currency ambiguity in cross-border scenarios.

## CR-06 · Stacking resolution

**Baseline.** Boolean `Stackable` checkbox, no defined resolution order.
**Change.** `Stackable` retained, paired with `exclusivityGroup`. Formal resolution algorithm per A6. Deterministic tiebreak. Per-wallet independent resolution. Multiplier composition rules.
**Impact.** `[§]` Rules Engine, `[§]` Audit Log.
**Rationale.** The baseline boolean could not express "one base earn rule, but all boosters stack". Undefined ordering made awards non-reproducible, which breaks reversal.

## CR-07 · Limits expansion

**Baseline.** Per customer total, per customer per day.
**Change.** Eight limit types with configurable period, reset window and breach behaviour. Cardinality-locked fields. Enforcement keys specified per limit.
**Impact.** `[§]` Rules Engine, `[§]` Data Model. New counters table.
**Rationale.** Budget caps are the control that bounds financial exposure. Rolling vs calendar reset gives different answers and must be explicit.

## CR-08 · Rule configuration

**Baseline.** None.
**Change.** Rounding, posting mode, hold period, expiry override, reversibility, test mode, notification. Three-level inheritance.
**Impact.** `[§]` Rules Engine, `[§]` Program Settings (new), `[§]` Notification Service.
**Rationale.** Delayed posting prevents most reversals from being needed. Test mode allows safe rule launch.

## CR-09 · Engine guarantees

**Baseline.** Not specified.
**Change.** The ten guarantees in A10: idempotency, cardinality enforcement, transactional counters, budget reservation, counter release on reversal, historical reversal reads, rule versioning, append-only ledger, resolution audit, deterministic scheduling.
**Impact.** `[§]` Rules Engine, `[§]` Ledger Service, `[§]` Scheduler (new), `[§]` Non-functional Requirements.
**Rationale.** At-least-once delivery without idempotency produces duplicate awards. Without versioning, historical awards cannot be explained or audited.

## CR-10 · Customer data boundary

**Baseline.** Implicit assumption of profile access.
**Change.** Engine holds `customerRef` only. Profile values arrive per event. Scheduling registration endpoint accepts `MM-DD` only.
**Impact.** `[§]` Integration Contract, `[§]` API Specification, `[§]` Data Privacy.
**Rationale.** Keeps the engine outside PII scope and makes multi-tenancy clean. The contract requirement that `customerRef` be immutable must be explicit, or lifetime caps break silently.

## CR-11 · Admin UI rebuild

**Baseline.** Static form: Basics, Calculation, flat Conditions, Limits.
**Change.** Metadata-driven cascade with derivation rail, dynamic calculation card, grouped condition builder with add/remove, expanded limits, configuration card, live validation with blocking and warning tiers, serialised rule preview.
**Impact.** `[§]` Admin UI. Reference implementation delivered as `loyalty-rule-builder.html`.
**Rationale.** Configuration errors in a loyalty engine cost money. Surfacing constraints at configuration time is cheaper than detecting them in production.

## Dependencies

```
CR-01 ─► CR-02 ─► CR-03 ─► CR-11
  └────► CR-04 ─► CR-11
CR-05 ─► CR-11
CR-06 ─► CR-09
CR-07 ─► CR-09 ─► CR-11
CR-08 ─► CR-11
CR-10 ─► CR-09
```

CR-01 is the root. CR-09 should land before any production traffic.

## Migration notes

1. Backfill event definitions with category, source and cardinality before deploying the UI.
2. Wrap existing flat condition lists in a single AND group under an AND root.
3. Backfill `exclusivityGroup` on existing non-stackable rules with the target account name, preserving current behaviour.
4. Version all existing rules to v1 with an effective date matching their creation date.
5. Deploy idempotency constraints before the new engine, so any replay during cutover is rejected rather than double-posted.

## Out of scope

Tier qualification and tier progression rules. Reward catalogue management. Partner and coalition earning. Points purchase. Fraud detection. These require separate change requests.
