---
paths:
  - "src/dEngage.Loyalty.RuleEngine/**"
---

# dEngage.Loyalty.RuleEngine — evaluation and posting core

Decides what a customer earns or burns for an event, and posts it. Pure domain: **no HTTP, no
RabbitMQ, no reference to Api or Consumer.** It references Schema, Shared and Ledger.

## Pipeline (`RuleEngine.ProcessEventAsync`) — keep the stage order
`IRuleMatcher` (cached rules + campaigns that match the trigger) → `ITierContextLoader` →
the Reversal processor (bypasses winner selection) → `IWinnerSelector` (exclusive
rules compete per target account — named groups and multipliers retired by 1.3.CL — stackables add, limits pre-check, per wallet) → campaign modules (their own
transaction) → `ILedgerPoster` (budget reservation + posting in **one transaction**) →
`ILimitCounterSync` (Redis counters) → `ITierEvaluationService` (a failure never rolls back the earning).
- Stage responsibilities are split across `Processing/I*` interfaces. Put new logic in the right
  stage, and don't grow `RuleEngine.cs` into a god method.
- Callers must invoke the engine exactly once per delivery. The posting, audit and outbox writes
  are deduped (`{eventId}:{ruleId}` keys), and since CR 2026-10-06 R15 a **redelivered** event
  (replayed from the dead-letter queue after a failure, or redelivered after a crash) is recognised
  by `ILedgerPoster.HasPostedAsync`: when its rules already posted or held, winner selection,
  budget reservation and the Redis counters are skipped — campaigns and the tier check still run.
  `ReversalRuleProcessor` skips an entry it already reversed. Anything new with side effects must
  carry its own idempotency key and check it **before** reserving a budget or counting a limit.
- Once-per-customer events (`signup`, `kyc.completed`) pay each rule at most once per customer
  (`OnceOnlyAward`, CR 2026-10-06 D12): `WinnerSelector` skips a rule that already paid (so the
  next exclusive rule can win), `LedgerPoster` re-checks inside its transaction, and the posting
  key is `once:{ruleId}:{contactKey}`, so the unique ledger index forbids a second award.

## Adding a rule type
1. Constant in `Shared/Constants/RuleTypes`.
2. Entry in `Metadata/RuleTypeCatalog`: category, required payload field kinds, valid target
   account kinds, note. **This catalog is the single source of truth.** The API validators and
   the portal rule builder (`GET rules/metadata`) read it, so never duplicate the list elsewhere.
3. `Calculation/<Name>RuleHandler : IRuleTypeHandler` — `RuleType` + `decimal Compute(RuleCalculation, EvaluationEvent)`.
   Keep `Compute` **pure** (no I/O, no clock, no DB) so it can be table-tested.
4. Register it as a singleton `IRuleTypeHandler` in the Consumer `Program.cs`. **An unregistered
   type doesn't fail.** `RuleTypeHandlerRegistry` falls back to `ZeroDeltaHandler`, so the rule
   silently awards 0. A duplicate `RuleType` throws at startup.
5. Add unit tests in Engine.Tests, and update the scope baseline (a new rule type is a scope change).
6. Decide which Configuration / Limits fields the new type uses in `Metadata/RuleFieldCatalog`
   (CR 2026-10-06 Phase 2) — the API validates new rules against it and the portal shows only
   those fields, so a field the code ignores is never offered.
- Rule types that don't fit the single-wallet winner model (dual-entry, inherited target) get a
  dedicated `I<Name>RuleProcessor`, as Reversal does, or are applied by their event handler through
  a narrow seam, as Redemption and Transfer are (`IBurnRuleResolver`, CR 2026-10-05: one rule per
  event, picked by wallet and priority, never by the engine). Explain the reason in the class
  comment.

## Money, rounding, limits
- Everything is `decimal`. Apply rounding exactly where `RuleSettings` / `RuleCalculation` says,
  once, at the documented stage. Never round in two places. Since CR 2026-10-06 Phase 4 that
  stage is `WinnerSelector.ApplyRounding` (after the calculation and Max per event, before the
  limits): the target wallet's decimals, the rule's rounding or the program's `DefaultRounding`.
  Rule type handlers return the unrounded amount.
- Budget limits are **reserved inside the posting transaction** (`BudgetReservationService`
  `LockAndGetUsageAsync` → `RecordUsageAsync`), which closes the race window left by the
  `WinnerSelector` pre-check. Don't move reservation outside the transaction, and don't
  remove the pre-check.
- `OnBreach` is `"Clamp"` (reduce the delta) or `"Skip"` (skip the rule), and applies to every
  cap — per-customer total / per day / per period and the budgets — on exclusive and stackable
  rules alike, through the one `WinnerSelector.ClipToLimitAsync` check (CR 2026-10-06 D13). A cap
  is never exceeded; a skipped exclusive rule lets the next exclusive rule try. A rolling
  `RuleBudgetPerPeriod` can't be locked and relies on the pre-check. Keep that documented behaviour.
- Test mode was removed (CR 2026-10-06 D20): a stored `testMode` flag is ignored and the API
  refuses `true`. Delayed posting creates a `HeldPosting` that `DelayedPostingPromotionJob`
  promotes later; both must stay idempotent on the same key.
- The promotion posts `Delta` minus what refunds took back during the hold, never a cancelled row,
  each row in its own transaction under the row lock `RefundService` shares. A release is
  announced like an immediate award (`points.earned` with its own dedupe key, `rule.awarded` when
  the rule notifies) and re-evaluates the tier after commit; rule and program status are
  deliberately not rechecked (CR 2026-10-06 H1–H3, D15).
- `order.refunded` accepts no rule type (`RuleTypeCatalog`, CR 2026-10-06 D22): the built-in
  refund reverses the order, and `RuleEngine` skips a Reversal rule whose trigger no longer accepts
  it, so a not-yet-disabled one can't reverse twice.
- Postings reference `ruleId + ruleVersion` (CR-09). Never post without the version.

## Conditions — two DSLs by design
- `ConditionTree` / `GroupedConditionEvaluator` / `GroupedConditionDsl` → Rules and CardBuckets
  (nested AND/OR, CR-05).
- `ConditionClause` / `ConditionEvaluator` → **Streak Campaigns only** (flat AND). This is not
  drift. Don't merge the two or migrate streaks without a scope change.
- Any change to operators, fields or validation must be mirrored in
  `web/src/app/shared/forms/condition-tree-dsl.ts` (and the streak validators) in the same change,
  with matching table tests.
- Field namespaces are `event.*`, `profile.*` and `agg.*`. `profile.*` values are snapshotted on
  the posting at award time.

## Caches (`Cache/`, `Campaigns/CampaignConfigCacheService`)
- Redis keys are `rules:{tenant}:{program}` and `campaigns:{tenant}:{program}`: separate services
  and separate prefixes on purpose. Keep them apart.
- Freshness comes from `RuleSyncService` (30 s delta) plus explicit `InvalidateAsync`. The 1 h TTL
  is only a safety net. Any write path that changes rules or campaigns must invalidate.
- A corrupt cache payload logs an error and reloads from the DB. Never let a cache failure block
  or zero out earnings.
- Cache loaders filter by tenant Guid and `Status == Active`, and validate the DSL when loading.

## Campaigns (`Campaigns/`)
- New campaign types implement `ICampaignModule`, get their own table and config cache entry, and
  are resolved through `ICampaignModuleRegistry`. Don't add polymorphic shared tables.
- Streak processing is timezone-aware (an explicit IANA id in `StreakConfig`) and uses
  `PeriodCalculator`. Idempotency is anchored on the module's own state (`StreakAppliedEvent`,
  `StreakPeriodState`), not on the earn flow.

## Errors
- Signal business failures with `InvalidOperationException("snake_case_code: detail")`, and make
  sure the API's `DomainErrorTranslator` knows the prefix.
