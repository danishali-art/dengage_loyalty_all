# CLAUDE.md — dEngage.Loyalty (repo root)

Multi-tenant loyalty platform: .NET backend under `src/` + Angular admin portal under `web/`
(see `web/CLAUDE.md` for frontend-specific conventions).

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
  AccountTypes, Auth, CardBuckets, Complaints, ConfigVersions, Customers, Dashboard, Events,
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
own window.

## Secrets

`.env` holds real local credentials and is gitignored — never commit it. `.env.example`
documents the required keys with empty values. Likewise `src/dEngage.Loyalty.Api/appsettings.Development.json`
(plaintext JWT signing key and connection strings) is gitignored: on a fresh clone, copy
`appsettings.Development.example.json` next to it and fill in the values. Rotate `ANTHROPIC_API_KEY` if it was ever
committed anywhere before this repo was git-initialized.

## Related docs

- `docs/SOW.md` — as-built Statement of Work; the scope contract pending PO+Architect sign-off.
- `docs/SCOPE_BASELINE.md` — locked scope, reconciled against `docs/SOW.md`.
- `LoyaltySaaSApi.md` — API contract (referenced by `web/CLAUDE.md`; recreate/regenerate from
  the `Api` controllers/DTOs if not recovered from prior history).
- `scripts/loyalty_schema_reference.md` — DB schema reference (Turkish), maintained by hand: update
  it in the same change as each migration, checked against the EF Core configurations.
- `web/CLAUDE.md` — Angular portal conventions.
