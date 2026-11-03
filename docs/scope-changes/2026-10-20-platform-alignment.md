# Scope Change Impact Analysis: Platform Alignment with dEngage Infrastructure (Phases 1–3)

**Status:** Approved by Product Owner + Architect — implemented and merged to `main` (PR #8
2026-10-21, PR #9 2026-10-22). The phases were approved one by one against the *Loyalty Platform
Alignment Assessment*, and each phase kept its own implementation record. This document puts
them in the scope-change format after the fact.

**Date:** 2026-10-20 to 2026-10-22 (Phase 1 on 2026-10-20, Phase 2 on 2026-10-20 to 2026-10-21,
Phase 3 on 2026-10-21; merged to `main` on 2026-10-21 and 2026-10-22)
**Author:** Claude Code, at the request of Moiz Ali Afzal
**Basis:** *Loyalty Platform Alignment Assessment* (decisions D1–D18, kept outside the repo).
**Detailed records:** `docs/platform-alignment/phase-1.md`, `phase-2.md`, `phase-3.md`.
**Affects:** `docs/SCOPE_BASELINE.md` (Schema, Consumer, Contract, Shared rows), `CLAUDE.md`,
`.claude/rules/backend*.md`, `docs/DEPLOYMENT.md`, `build.json`, `bitbucket-pipelines.yml`

This document follows the scope-change process in `CLAUDE.md`. It records what changed when loyalty
moved onto the dEngage platform's hosting, configuration, packaging and API conventions, compared
with how loyalty ran as a standalone product. It covers Phase 1 ("Deployable & safe"), Phase 2
("Product-ready") and Phase 3 ("Convergence"). Phase 4 (the dEngage connector) is not covered.

## Approval record

- The assessment's decisions were approved for implementation **phase by phase**. Each phase record
  says so in its **Basis** line.
- **2026-10-20, scope decision:** D15 (outbound webhooks) was **dropped**. Clients are notified only
  through dEngage (the Phase 4 connector). See `phase-2.md`, header.
- **2026-10-21, design decisions:** D8's library move was **not done** (§3.3), and the HTTP hosts use
  `dEngage.Common.AspNetCore` with three overrides (§3.3).
- **Every phase and the decisions above are approved** (confirmed by Moiz Ali Afzal).
  The items in §5 are follow-ups outside Phases 1–3, not conditions on this approval.

## 1. Why this change is being proposed

Loyalty was built as a standalone product. It ran on .NET 8 with its own hosting, its config came from
committed appsettings with localhost fallbacks, it had no Dockerfiles or CI files, and its HTTP API
used Nancy. A single queue served every tenant. To deploy and operate it like the other dEngage
services (same images, pipeline, config mounting, logging, metrics and packages), it had to follow
the platform's conventions. It also needed to keep its own wire contract and the guardrails in
`.claude/rules/00-project-guardrails.md` §2.

## 2. Current state before the change

- **Runtime:** .NET 8, plain ASP.NET Core / generic host, `Logging` section in appsettings.
- **Configuration:** connection strings and the RabbitMQ settings had built-in defaults
  (`localhost`, `guest/guest`), so a missing value failed late or silently. The `ENC(...)` master key
  came only from an environment variable. Rate limits came from appsettings, per environment.
- **Deployment:** no Dockerfiles, no `build.json` and no `bitbucket-pipelines.yml`.
- **API:** 14 Nancy modules in one `Api` host served both the portal (JWT) and client event
  ingestion (`X-Api-Key`).
- **Messaging:** one `q.loyalty` queue processed one event at a time for every tenant, so one
  tenant's burst delayed everyone.
- **Packages:** none. Other dEngage services couldn't reference the event contract or call loyalty
  through a typed client.

## 3. Change (as implemented)

### 3.1 Phase 1: deployable and safe (standalone), 2026-10-20, commit `c67c497`

| Decision | Implemented |
|---|---|
| D1 Platform hosting | .NET 9. `dEngage.Common.Hosting` 6.43.0 in the Api (`DengageWebApplicationBuilder`) and the Consumer (`DengageHostBuilder`). `CONFIG_PATH` / `NLOG_CONFIG_PATH`. Separate `appsettings.logging.json` (NLog) and `appsettings.metrics.json` files. Environment bridge (`ASPNETCORE_ENVIRONMENT` → `WEB_APPLICATION_ENVIRONMENT`). `MarkAsExternal()` on the Api outside Development |
| D2 Connection resolution | Partly done. Connection values come only from each environment's config, and the service fails fast when one is missing. The `db_manager_data` provider moved to Phase 4 (D13) |
| D3 Secrets | Partly done. Secrets come from mounted config files. The `ENC(...)` key comes from `LOYALTY_MASTER_KEY_FILE`, with the env var as a local fallback |
| D9 Topology | Partly done: Dockerfiles for the Api and the Consumer. The `Job.Service` split was left to the event-throughput CR |
| D10 Repository home | `build.json`, `bitbucket-pipelines.yml`, `NuGet.Config` |
| D13 Core/connector boundary | `CoreDependencyBoundaryTests`: only `dEngage.Common.*` (+ transitive `dEngage.SecMan`) allowed |

### 3.2 Phase 2: product-ready (standalone), 2026-10-20 to 2026-10-21, commits `84427e7` … `12e4d38`

| Decision | Implemented |
|---|---|
| D12 Tenant fairness | Tenants listed in `RabbitMq:IsolatedTenants` get their own sequential queue, `q.loyalty.tenant.<slug>`. `q.loyalty` moves their events there (confirmed publish, then ack) |
| D4 Runtime settings | `loyalty_config` (global) and `loyalty_tenant_config` (one row per tenant, no global fallback), read through `ILoyaltyConfig`, managed by SQL. No defaults in code or appsettings. 4 migrations (`…AlignD4`) |
| D3 (rest) | `ISecretProtector`: secret rows in the DB hold `ENC(...)` values |
| Contract package | `dEngage.Loyalty.Contract` (netstandard2.0): event envelope, types and catalog, outbound types, outcome reasons, messaging topology |
| D15 Webhooks | **Dropped** (2026-10-20) |
| D18 | Nothing to build: loyalty emits events, and dEngage sends the messages |

### 3.3 Phase 3: convergence, 2026-10-21, commits `3b2b420` … `51098f9`

| Decision | Implemented |
|---|---|
| D7 Nancy → controllers | 14 controllers, 77 actions. Routes, verbs, guards and bodies are unchanged (the OpenAPI spec regenerated byte-identical). `LoyaltyControllerBase` and `LoyaltyApiMiddleware` replace the Nancy plumbing. Both HTTP hosts run on `dEngage.Common.AspNetCore` with three overrides: its exception filter is removed (loyalty's ProblemDetails `code` is kept), its request/response logger is off (it would log JWTs, API keys and PII), and its Newtonsoft formatters are unused |
| D9 `Public.Api` split | New deployable `dEngage.Loyalty.Public.Api`: the 8 ingestion routes and the status lookup, on the same paths, with API keys only (no JWT, no CORS). The admin Api keeps `simulate`, `types` and the status lookup |
| `Api.Client` package | `dEngage.Loyalty.Api.Client` (netstandard2.0): a typed client for Public.Api with per-tenant API keys (`ILoyaltyApiKeyProvider`) that returns results instead of throwing. It depends only on Contract and `Microsoft.Extensions.Http` |
| D8 Messaging | Settings step only. `BrokerConnectionSettings` reads the platform's `appsettings.messagebroker.json` (`RabbitMQSettings`), with the legacy keys kept for one release. All three hosts fail fast without it. **Library move not done** (decided 2026-10-21): `Common.MessageBroker` (MassTransit) would change the wire contract and lose single-message-in-flight, tenant lanes and the inbox dedupe. `dEngage.Outbox` needs `DbManager` (D13) |
| Observability | `ConsumerTelemetry`: one outcome per delivery, App.Metrics meters, a debug-view section, and one platform `ILogEvent` per delivery (without the message body, which can contain PII) |

## 4. Impact

| Area | Impact |
|---|---|
| Backend (`src/`) | New projects: `Contract`, `Public.Api`, `Api.Client`. Nancy removed. All projects on `net9.0` |
| DB schema / migrations | 4 additive migrations (D4: `loyalty_config`, `loyalty_tenant_config`, seed and backfill). `scripts/loyalty_schema.sql` and `scripts/loyalty_schema_reference.md` updated |
| API / DTOs / `LoyaltySaaSApi.md` | Routes and bodies unchanged. **Breaking for clients:** the 8 ingestion routes moved to Public.Api, and posting one to the admin Api now returns 405. The OpenAPI spec dropped those 8 operations |
| Events / Consumer | Wire format unchanged. New optional tenant-lane queues. New metrics and log events |
| Portal | OpenAPI generator reads controllers and scans Contract. The real-backend e2e spec posts to Public.Api. No UI change |
| Deployment | Three images (`api`, `public.api`, `consumer`). Mounted `appsettings*.json` incl. `appsettings.messagebroker.json`. `LOYALTY_MASTER_KEY_FILE`. **Migrations must be applied before the Phase 2 code**, because the Api has no fallback for `loyalty_config` |
| Behaviour | Local and staging tenants start at 120 ingestion events/min (was 600 / 300). Services refuse to start with missing connection or broker settings |
| Docs | `SCOPE_BASELINE.md` (Schema, Consumer, Contract, Shared rows), `CLAUDE.md`, `.claude/rules/*`, `docs/DEPLOYMENT.md` |

**Contracts (guardrails §2):**
- Tenancy, ledger and money stay unchanged: the tenant lanes keep per-customer order, and decimals
  stay strings. `DecimalStringJsonConverter` moved into Contract so the client and server share it.
- Event `Data` stays snake_case, and inbox dedupe and the outbox are unchanged. Rule versioning,
  CASH approval, time and DSL parity are unaffected.

## 5. Follow-ups (outside Phases 1–3)

Each needs its own decision or CR. None blocks what is implemented.

1. **Balance/wallet lookup on Public.Api.** The assessment lists it, but no such endpoint exists.
   With D15 dropped, it would be the only way for a loyalty-only client to read current state. This
   needs its own CR.
2. **`Job.Service` single runner** (D9 rest). It is still left to the event-throughput CR, whose
   document was withdrawn on 2026-10-21.
3. **Settings not moved into `loyalty_config`:** job schedules, retention / streak windows,
   currencies, JWT / CORS. See `phase-2.md` §2.3.
4. **Committed dev master key** in `src/dEngage.Loyalty.Consumer/Properties/launchSettings.json`.
   Rotate it if it is used outside local development.
5. **Prometheus scraping.** `/metrics` returns 404 on the platform host. Confirm with DevOps how
   dEngage services are scraped.

## 6. Risks

- **Deployment order (D4):** the code fails on ingestion if the four D4 migrations aren't applied
  first.
- **Ingestion clients:** any client still pointing at the admin Api gets 405. The client host name
  must change to Public.Api.
- **Startup network call:** `CommonConstants` opens a UDP socket to 8.8.8.8, so air-gapped on-prem
  installs need a default route.
- **Removing a tenant from `IsolatedTenants`:** its lane queue must be empty first, or events run
  out of order.
- **Not yet run:** `docker build` and the shared Bitbucket pipeline, including package publishing.
  Both were blocked by VPN DNS / missing Bitbucket access at implementation time.

## 7. Verification (as recorded per phase)

| Check | Result |
|---|---|
| `dotnet build dEngage.Loyalty.sln` | ✅ 0 errors (all phases) |
| Engine tests | ✅ 142 (P1) → 146 (P2) → 167 (P3) |
| Integration tests (Docker) | ✅ 325 (P1) → 345 (P2) → 361 (P3). Occasional Testcontainers pipe timeouts passed on rerun |
| OpenAPI spec | ✅ identical after D7. After D9 exactly the 8 ingestion operations were removed |
| Local runs | ✅ Api, Public.Api and Consumer on the platform host against docker-compose |
| `dotnet pack` Contract / Api.Client | ✅ dependencies as intended |
| `docker build`, shared pipeline | ❌ not run |

## 8. Files changed by this record

| File | Change |
|---|---|
| `docs/scope-changes/2026-10-20-platform-alignment.md` | Created: this consolidated record |
| `docs/SCOPE_BASELINE.md` | Changed: revision history row pointing to this record |
| `docs/platform-alignment/phase-1.md`, `phase-2.md`, `phase-3.md` | Changed: status lines say approved and merged |

The full per-phase file lists are in `docs/platform-alignment/phase-1.md` §5,
`phase-2.md` §1.7 / §2.6 / §3.4 and `phase-3.md` §1.5 / §2.4 / §3.4 / §5.3.
