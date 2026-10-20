# CLAUDE.md — dEngage.Loyalty (repo root)

Multi-tenant loyalty platform: .NET backend under `src/` + Angular admin portal under `web/`
(see `web/CLAUDE.md` for frontend-specific conventions). A Vue 3 rewrite of the portal is being
built in phases beside it under `webVue3/` (see `webVue3/CLAUDE.md`; scope change
`docs/scope-changes/2026-10-12-vue3-portal-rewrite.md`). `web/` stays the live portal until parity
is signed off.

## Scope governance — read this before implementing a scope change

`docs/SCOPE_BASELINE.md` is the single source of truth for what is in scope, backed by
`docs/SOW.md` (an as-built Statement of Work authored from the codebase — the original client
SOW was unrecoverable in this environment). Both need Product Owner + Architect sign-off before
they're authoritative.

**Rule: no scope-change PR merges without updating `docs/SCOPE_BASELINE.md` (and
`LoyaltySaaSApi.md` / `scripts/loyalty_schema_reference.md` where relevant) in the same
change.** Docs drift is what this rule exists to prevent — treat a code change that isn't
reflected in these docs as incomplete, not done.

The process for a scope change:
1. Change is raised as a **Scope Change Request** in Jira.
2. Claude Code produces an **impact analysis** (affected modules, migrations, breaking API
   changes, risk) against `docs/SCOPE_BASELINE.md` and the current code — analysis only, no
   implementation yet.
3. Product Owner + Architect jointly approve before implementation starts.
4. Claude Code implements on a feature branch and updates the docs above in the same branch.
5. Architect reviews the diff before merge.

## Backend architecture

- `src/dEngage.Loyalty.Api` — HTTP API (Nancy-based modules), one folder per resource:
  AccountTypes, Auth, CardBuckets, ConfigVersions, Customers, Dashboard, Events,
  Platform, Programs, Rewards, Rules, StreakCampaigns, Tiers.
- `src/dEngage.Loyalty.Api.Framework` — cross-cutting API concerns: auth, tenancy, rate
  limiting, pagination, validation, error handling.
- `src/dEngage.Loyalty.Consumer` — background worker consuming events off RabbitMQ.
- `src/dEngage.Loyalty.Engine.Framework` — shared engine abstractions/bootstrap/consumers.
- `src/dEngage.Loyalty.RuleEngine` — the core evaluation engine: rule matching/calculation
  (`Calculation/`), tier evaluation, streak campaigns (`Campaigns/Streak/`), limits caching,
  ledger posting (`Processing/`).
- `src/dEngage.Loyalty.Ledger` — append-only balance ledger.
- `src/dEngage.Loyalty.Schema` — EF Core entities, configurations, migrations (Postgres).
- `src/dEngage.Loyalty.Shared` — constants, shared events, security/crypto helpers.
- `src/dEngage.Loyalty.CryptoCli`, `TestCli` — operational/dev CLIs.
- Tests: `dEngage.Loyalty.Engine.Tests` (unit), `dEngage.Loyalty.IntegrationTests` (API/E2E,
  see `Fixtures/`).

Infra (local): Postgres, Redis, RabbitMQ via `docker-compose.yml`. `.\run.ps1` boots infra +
Api (`http://localhost:5173/swagger`) + Consumer + web (`http://localhost:4200`) each in their
own window. The Consumer also listens on `http://localhost:5174` (platform `/health-check` and
metrics only).

Runtime: .NET 9 on the dEngage platform host (`dEngage.Common.Hosting`): `CONFIG_PATH` config
layering, NLog, Prometheus metrics. Images: `src/dEngage.Loyalty.{Api,Consumer}/Dockerfile`
(repo root as build context), CI via `build.json` + `bitbucket-pipelines.yml`. Conventions and
pitfalls (environment bridge, `MarkAsExternal`, fail-fast config, the D13 dependency boundary,
restoring off the VPN) are in `.claude/rules/backend.md` → "Hosting and configuration". The
decisions behind this are recorded in `docs/platform-alignment/`.

## Secrets

`.env` holds real local credentials and is gitignored — never commit it. `.env.example`
documents the required keys with empty values. Likewise `src/dEngage.Loyalty.Api/appsettings.Development.json`
(plaintext JWT signing key and connection strings) is gitignored: on a fresh clone, copy
`appsettings.Development.example.json` next to it and fill in the values. Rotate `ANTHROPIC_API_KEY` if it was ever
committed anywhere before this repo was git-initialized.

Deployed environments don't use any committed settings with secrets: they mount their
`appsettings*.json` (including connection strings and the JWT key) into `CONFIG_PATH`, and if a
value uses `ENC(...)` they provide the key as a mounted file named by `LOYALTY_MASTER_KEY_FILE`.
The base `appsettings.json` files carry no connection values, so a missing mounted value stops
the service at startup instead of falling back to localhost.

## Related docs

- `docs/SOW.md` — as-built Statement of Work; the scope contract pending PO+Architect sign-off.
- `docs/SCOPE_BASELINE.md` — locked scope, reconciled against `docs/SOW.md`.
- `LoyaltySaaSApi.md` — API contract (referenced by `web/CLAUDE.md`; recreate/regenerate from
  the `Api` controllers/DTOs if not recovered from prior history).
- `scripts/loyalty_schema_reference.md` — DB schema reference (Turkish), maintained by hand: update
  it in the same change as each migration, checked against the EF Core configurations.
- `web/CLAUDE.md` — Angular portal conventions.
- `webVue3/CLAUDE.md` — Vue 3 portal (in progress): architecture, commands, phase status. Rules in
  `.claude/rules/frontend-vue.md`.
- `webVue3/openapi/openapi.json` — machine-readable API contract **generated from the C# source**
  (`npm run api:spec` in `webVue3/`); the Vue portal's TypeScript types come from it.
