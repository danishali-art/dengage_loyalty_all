# Project guardrails — dEngage.Loyalty (always apply)

A multi-tenant loyalty platform that moves real money and points. A wrong change here means a
customer's balance is wrong, or one tenant sees another tenant's data. Apply these rules to
every prompt, whether it is a question, a debugging session, a refactor or a feature, and to
both the backend (`src/`) and the frontend (`web/`). Area-specific rules are in
`.claude/rules/backend.md` and `.claude/rules/frontend.md`.

## 1. Scope comes first
- `docs/SCOPE_BASELINE.md` (backed by `docs/SOW.md`) decides what is in scope. Before building
  anything new, check it has a row there.
- A request that adds, removes or changes a capability (a new endpoint, rule type, reward type,
  entity, screen or behaviour) is a **scope change**. Don't implement it straight away. Instead:
  1. Say it is a scope change and name the baseline row it touches, or say that there isn't one.
  2. Produce an impact analysis first: affected modules, DB migrations, API/DTO breaks,
     frontend impact, risk. Follow the existing format in `docs/scope-changes/YYYY-MM-DD-<slug>.md`.
  3. Wait for the developer to confirm that the Product Owner and Architect approved it, then
     implement.
- Bug fixes, refactors that don't change behaviour, tests and docs are **not** scope changes.
  Still keep them within the task.
- Don't expand scope quietly ("while I'm here..."). Suggest extra work instead of doing it.

## 2. Contracts that must never break
These span the backend, the consumer and the portal. Breaking one fails silently or corrupts data.
| Contract | Rule |
|---|---|
| Tenancy | Every tenant-owned read or write is filtered by tenant. Routes carry the tenant slug, which resolves to a `TenantId` Guid. Never trust a tenant id from a request body. |
| Money and points | `decimal` / `numeric(20,4)` on the server, JSON **strings** on the wire, `DecimalString` in the portal. Never `double`, `float` or JS `number`. |
| Ledger | Entries are only ever added. Never update or delete ledger rows. Fix mistakes with compensating entries. Every posting has a deterministic idempotency key. |
| Events | Event `Data` is **snake_case**. The REST API is **camelCase**. Consumers dedupe through `EventInbox` and publish through the outbox. |
| Rule versioning | Editing a rule inserts a new version. Postings reference `ruleId + ruleVersion`. Program and tier edits write `ConfigVersion` audit rows. |
| CASH rules | Need approval from a second admin. The creator can never approve their own rule. |
| Time | UTC everywhere. Streak timezones are explicit IANA ids. |
| DSL parity | The portal's condition and streak validators mirror the C# engine exactly. |
If a task seems to need breaking one of these, stop and explain why. Don't work around it.

## 3. Match what's already there
- Before writing code, find the nearest existing example and copy its shape. Use `Rewards/` for
  API resources and `features/programs/rewards/` for portal features. Don't invent a new
  pattern, layer, base class or folder layout.
- Keep the change as small as possible. No drive-by renames, reformatting of untouched code, or
  "cleanup" outside the task.
- Don't add NuGet or npm packages, change framework versions, or swap libraries (Nancy, EF Core,
  FluentValidation, Angular CDK, Tailwind, ...) unless explicitly asked.
- Comments explain **why** (a constraint, a contract, an earlier bug), in the existing style.
  Don't narrate the code.

## 4. Files you must not edit or expose
- `.env` holds real credentials. Never print, copy, commit or paste its values anywhere,
  including in answers. Document new keys in `.env.example` with empty values.
- `src/dEngage.Loyalty.Schema/Migrations/*` for migrations that were already applied, and
  `LoyaltyDbContextModelSnapshot.cs`: generate new migrations, never hand-edit these.
- `scripts/loyalty_schema_reference.md` is maintained by hand. Update it with every schema change,
  checked against the EF Core configurations; don't leave it behind the migrations.
- `scripts/*.sql` tenant seeds and backups (`loyalty_db*`, `*_pg_backup_*`) are reference data.
  Don't modify them unless asked.
- `bin/`, `obj/`, `node_modules/`, `.vs/` and `web/dist/` are build output. Never edit them.
- Never run destructive commands against data without explicit confirmation. That includes
  `dotnet ef database drop`, `docker compose down -v`, `DROP`/`TRUNCATE`/`DELETE` without a
  narrow `WHERE`, and deleting files or folders.

## 5. Keep docs in step with code
A change isn't done until the docs that describe it are updated in the same piece of work:
- Behaviour or scope → `docs/SCOPE_BASELINE.md` (and `docs/SOW.md` if it's contractual).
- Endpoints or DTOs → `LoyaltySaaSApi.md` (regenerated 2026-10-02 from
  `src/dEngage.Loyalty.Api/*/*Dtos.cs` and `*Module.cs`). Keep it in step from the code; don't
  document behaviour the code doesn't have.
- DB schema → a new migration plus an updated `scripts/loyalty_schema_reference.md`.
- A changed convention → the relevant `CLAUDE.md` or `.claude/rules/*.md` file.

## 6. No version control right now
The repo currently has **no git** (the history is archived outside the project). So:
- There is no diff to fall back on. Before editing a file, read it fully. Before any large
  rewrite, say which files you will change.
- At the end of every task, list **every file created, changed or deleted**, with a one-line
  reason each. This list is the review record for the Architect.
- For scope-change work, add that file list to the matching `docs/scope-changes/*.md`.
- Don't run `git init` or any other git command unless the developer asks.

## 7. Be honest about results
- Say what you verified (build, tests, manual run) and what you didn't. Never report "done" or
  "fixed" on code that wasn't built or tested.
- If tests fail, show the failure. Never skip, delete or weaken a test, validator, tenant filter
  or approval check to make something pass.
- If a request is ambiguous or conflicts with these rules or the baseline, ask one focused
  question instead of guessing.
