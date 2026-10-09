# Workflow and debugging — dEngage.Loyalty (always apply)

## Every task: understand → plan → change → verify → report
1. **Understand.** Restate the goal in one line. Work out whether it is a bug fix, a
   refactor, a doc change or a scope change (see `00-project-guardrails.md` §1). Read the code
   involved, and read what calls it, before proposing anything.
2. **Plan.** For anything beyond a one-file fix, list the files you'll touch across both sides of
   the stack. A change to the API shape is never backend-only (see "Cross-stack changes" below).
3. **Change.** Follow the existing pattern and keep the change minimal.
4. **Verify.** Run the checks below for every area you touched.
5. **Report.** Say what changed, list the files, say what you verified and flag any open risks.

## Verification commands
| Area | Command (from the repo root unless noted) |
|---|---|
| Backend build | `dotnet build dEngage.Loyalty.sln` |
| Engine unit tests | `dotnet test src/dEngage.Loyalty.Engine.Tests` |
| API / E2E tests (Docker needed for Testcontainers) | `dotnet test src/dEngage.Loyalty.IntegrationTests` |
| New migration | `dotnet ef migrations add <Name>CrNN -p src/dEngage.Loyalty.Schema` (design-time factory lives in Schema) |
| Portal lint / test / build (from `web/`) | `npx ng lint` · `npm test` · `npx ng build --configuration development` |
| Portal format (from `web/`) | `npx prettier --write <changed files>` |
| Full local stack | `.\run.ps1` (infra, Api `:5173/swagger`, Consumer, web `:4200`) |
If you can't run a check (for example Docker is down), say so. Don't skip it silently.

## Cross-stack changes (API or data shape)
Changing a field, enum value, endpoint or validation rule means updating all of these together:
1. Backend: `src/dEngage.Loyalty.Api/<Resource>/` — Dtos, Validators, AppService, and the Module
   if the route changes.
2. Entity, configuration and migration in `src/dEngage.Loyalty.Schema` if it is persisted.
3. Engine or consumer handlers if the field travels on an event (in **snake_case**).
4. Portal: `web/src/app/features/<feature>/<name>.model.ts`, the service, the form and list
   pages, and the i18n keys in `en.json` and `tr.json`.
5. Enum-like registries on both sides, for example `RewardTypeRegistry.cs` ↔ the `RewardType` union.
6. Tests on both sides, plus `LoyaltySaaSApi.md` and `docs/SCOPE_BASELINE.md`.
Treat existing fields as frozen. Prefer adding new fields over renaming or removing old ones.
Call out any breaking change explicitly.

## Debugging: find the root cause, don't hide the symptom
- **Reproduce first.** Get the failing request, event or test, plus the exact error or log line.
  Use Swagger (`:5173/swagger`) or `src/dEngage.Loyalty.Api/LoyaltySaaS.Api.http` for API calls,
  and the portal's Event Simulator or `TestCli` for events.
- **Find which process fails.** The Api (HTTP, validation, admin CRUD), the Consumer (event
  handling, ledger postings, background jobs) and the portal are separate processes with separate
  logs. An accepted event with no ledger effect is a **Consumer** problem, not an API problem.
- **Fix the cause, then add a test** that fails without the fix. Put it in `Engine.Tests` for
  engine logic and `IntegrationTests` for API or persistence behaviour.
- **Never "fix" by:**
  - removing a tenant filter or approval check
  - loosening a validator
  - catching and swallowing exceptions
  - changing money to `double`/`number`
  - editing ledger rows
  - deleting `EventInbox` rows to force reprocessing in shared data
  - disabling or deleting a test

## Known pitfalls in this project (check these first)
| Symptom | Likely cause |
|---|---|
| Event accepted and marked processed, but no ledger entry and no error | Event `Data` sent in camelCase. Handlers read snake_case with `TryGetProperty` and silently miss. Use `JsonConventions.EventDataOptions`. |
| 404 on a resource that exists | Tenant slug vs Guid mix-up, or the resource belongs to another tenant or program (checks like `RequireProgramAsync`). Also check the `tenantId` segment `ApiClient` injects. |
| Re-sending an event does nothing | `EventInbox` dedupe on the event id is working as intended. Send a new event id. |
| A replayed (dead-lettered) event logs "already posted (redelivery) — rules not re-applied" | Intended (CR 2026-10-06 R15): its rules posted in the first delivery, which then failed later. The replay doesn't pay, reserve or count again; campaigns and the tier check still run. |
| Rule or campaign edit doesn't take effect | Stale Redis cache (`IRuleCacheService` / `ICampaignConfigCacheService`), a new rule version still inactive, a CASH rule stuck in `PendingApproval`, or the Consumer not restarted or synced (`RuleSyncService`). |
| Rule doesn't fire | Limits, budget, cooldown or cardinality breached (see `RuleFireAudit` and `rule_limit_counters`), another exclusive rule on the same target account won on priority, or delayed posting still held. On `signup` / `kyc.completed`: the rule already paid this customer once (CR 2026-10-06 D12 — the Customer 360 event drawer shows "bonus already received"). |
| An award is smaller than the rule's amount, or a lower-priority exclusive rule paid instead | A per-customer cap or budget was nearly used up: every cap trims to what's left (On breach = Clamp) or skips the rule (Skip), on exclusive and stackable rules alike (CR 2026-10-06 D13). |
| Rule create returns 400 `field_not_applicable` | Intended (CR 2026-10-06 Phase 2): the rule sets a Configuration / Limits field that doesn't apply to its trigger and type (see `applicableFields` in `GET rules/metadata`). Leave it unset. Existing rules are not checked on edit. |
| A rule that was in test mode is disabled, or `testMode: true` returns 400 `test_mode_removed` | Intended (CR 2026-10-06 D20/D21): test mode was removed; earn rules that had it on were disabled at deploy so they don't start paying unnoticed. Switch them on deliberately. |
| Rule "fires" but awards 0 | Its rule type has no `IRuleTypeHandler` registered in the Consumer `Program.cs`, so the registry fell back to the zero-delta handler. |
| Limits or budget used up about twice as fast for one event type | The engine runs twice for it: its handler calls `ICampaignEvaluationService` and the type isn't in `HandlerEvaluatedEvents`, so the worker calls it again. Fixed for the built-in types by CR 2026-10-06 D18; `HandlerEvaluatedEventsTests` catches a new handler that forgets. See `backend-consumer.md`. |
| A Reversal rule can't be created on `order.refunded`, or an existing one returns 409 `rule_type_retired` | Intended (CR 2026-10-06 D22): the built-in refund already reverses the order, and a Reversal rule there reversed it twice. Existing ones were disabled by migration. Reversal rules remain available for tenant-defined events. |
| A refunded order's held (Delayed) points vanish from "pending", or post smaller than the award | Intended (CR 2026-10-06 H1): a refund during the hold takes the held points back (`held_posting_refunds`); fully refunded holds are cancelled and never posted. |
| `cash.spent` refused with `insufficient_balance` and no rule fired | Intended (CR 2026-10-06 D23): reported with `loyalty.cash.spend_failed`, inbox processed; a refused spend earns nothing. |
| Rule doesn't fire for a new program | It is still a **Draft** (1.3.CL). The consumer only evaluates programs that are published **and** active — publish it, then switch it Active. |
| Redeem / transfer / purchase / cash event refused with `program_not_live` | Intended (CR 2026-10-05 item 5): `points.redeem`, `points.transfer`, `reward.purchase` (any reward type), `cash.added` and `cash.spent` are refused for a program that isn't published + active — publish it and switch it Active. |
| Expiry / tier downgrade / refund still happens for an inactive program | Known limitation: refund reversals (on purpose) and the ledger and tier jobs don't check program status (`docs/SOW.md` §3; CR 2026-10-05 O14 skipped). Don't "fix" it without a scope decision. |
| Redeem / transfer fails with `no_rule` or `rule_limit_reached` | Since CR 2026-10-05 redeem and transfer **always** need a rule — the wallet's own `redemption` / `transfer` settings only pre-fill new rules. Check for an **Active** RedemptionRule / TransferRule whose target is the event's `source_account_type_id` (a redeem rule waits in `pending_approval` until a second admin approves it; rules saved before the CR were disabled at deploy), its conditions and active window, and its cooldown / max customers / budget. |
| A reward is bought or earned but no cash is credited / no tier changes | The reward is still **pending approval** (cashback needs a second admin — `reward_not_approved` in the inbox error, or the streak grants nothing), the program isn't published + active (cashback only), the cash wallet / target tier is gone, the program has no tier-qualifying account, or the customer is already at or above the target tier (`outcome: already_at_or_above` on `loyalty.reward.earned`). All fulfilment goes through `IRewardFulfilmentService` (CR 2026-09-30). |
| Amounts slightly off or rounded in the portal | A decimal parsed into a JS `number` somewhere. It must stay a `DecimalString`. |
| API returns 400 with a `snake_case_code` | A domain `InvalidOperationException` went through `DomainErrorTranslator`. A new code may need its prefix mapped. |
| Portal shows no data but the API works | The API or Consumer isn't running, or the proxy target changed (`web/proxy.conf.json` → `:5173`). **There is no API mock layer.** `useApiMock` exists in the environments but nothing reads it, despite what `web/CLAUDE.md` says. |
| Integration tests fail on startup | Docker isn't running, so the Testcontainers for Postgres, Redis and RabbitMQ can't start. |

## How to answer developer prompts
- Ground every answer in this codebase: cite files as clickable paths and follow the existing
  patterns. Don't give generic framework advice that conflicts with them (for example suggesting
  MVC controllers, NgRx, Material or `HttpClient` in features).
- For "how do I…" questions, point to the existing example to copy before writing new code.
- If a prompt asks for something these rules forbid, say which rule it breaks and offer the
  compliant alternative.
