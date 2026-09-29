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
| Rule or campaign edit doesn't take effect | Stale Redis cache (`IRuleCacheService` / `ICampaignConfigCacheService`), a new rule version still inactive, a CASH rule stuck in `PendingApproval`, or the Consumer not restarted or synced (`RuleSyncService`). |
| Rule doesn't fire | Limits, budget, cooldown or cardinality breached (see `RuleFireAudit` and `rule_limit_counters`), another exclusive rule on the same target account won on priority, test mode, or delayed posting still held. |
| Rule "fires" but awards 0 | Its rule type has no `IRuleTypeHandler` registered in the Consumer `Program.cs`, so the registry fell back to the zero-delta handler. |
| Limits or budget used up about twice as fast for signup, kyc.completed, card.transaction, remittance, points.adjusted or points.expired | Those handlers call `ICampaignEvaluationService` and the worker calls it again, so the engine runs twice. The posting is deduped, but budget reservations and Redis counters aren't. See `backend-consumer.md`. |
| Rule doesn't fire for a new program | It is still a **Draft** (1.3.CL). The consumer only evaluates programs that are published **and** active — publish it, then switch it Active. |
| Redeem / transfer / expiry still happens for an inactive program | Known limitation: only earn rules, campaigns and the birthday bonus are gated on published + active; the per-event redeem/transfer/reward-purchase handlers and the ledger jobs don't check program status (`docs/SOW.md` §3). Don't "fix" it without a scope decision. |
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
