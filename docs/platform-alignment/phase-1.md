# Platform alignment — Phase 1 implementation record

**Status:** implemented on branch `platform-alignment/phase-1` (not committed or merged yet). For Architect review.
**Date:** 2026-10-20
**Basis:** *Loyalty Platform Alignment Assessment* (decisions D1–D18, approved for implementation phase by phase). Phase 1 = "Deployable & safe (standalone)".
**Type:** infrastructure and hosting. No new capability, endpoint, entity or migration, so no `docs/SCOPE_BASELINE.md` change.

## 1. What Phase 1 covers

| Decision | Done in this phase | Notes |
|---|---|---|
| D1 Platform hosting stack | ✅ .NET 9; `dEngage.Common.Hosting` 6.43.0 in Api (`DengageWebApplicationBuilder`) and Consumer (`DengageHostBuilder`); `CONFIG_PATH` / `NLOG_CONFIG_PATH`; split `appsettings.logging.json` (NLog) and `appsettings.metrics.json` | See §2 for the host's defaults we had to handle |
| D2 Connection resolution | ✅ partly: connection values come only from the environment's config (local `appsettings.Development.json`, deployed mounted files); fail fast when missing | The optional `db_manager_data` provider needs `dEngage.DbManager`, which D13 keeps out of the core. It moves to the dEngage connector (Phase 4) |
| D3 Secrets | ✅ partly: deployed secrets as mounted config files; `ENC(...)` key from a mounted key file (`LOYALTY_MASTER_KEY_FILE`), env var kept as local fallback | `ENC(...)` support was **kept**, not removed: the committed Consumer development settings use it and every developer's local setup depends on it. `ISecretProtector` for tenant secrets in the DB is added in Phase 2 with its first user (webhooks) |
| D9 Topology (partial) | ✅ Dockerfiles for the two existing services | The `Job.Service` single-runner split is **left to** `docs/scope-changes/2026-10-13-event-throughput-partitioning-and-bulk.md` (its Phase 1 owns "one instance per nightly job"), as agreed |
| D10 Repository home | ✅ files only: `build.json`, `bitbucket-pipelines.yml`, `NuGet.Config` | Moving the repository to Bitbucket is an admin action outside this change |
| D13 Core/connector boundary | ✅ `CoreDependencyBoundaryTests` | Allow-list: only `dEngage.Common.*` (+ `dEngage.SecMan`, transitive via `Common.Shared`) |
| Other Phase 1 fixes | ✅ Consumer fail-fast config (no localhost/postgres/guest fallbacks); one Redis key name (`RedisConnectionString`, legacy `Redis` still read); `/health/ready` no longer returns exception text | |

## 2. Platform host behaviour found during implementation

These are properties of `dEngage.Common.Hosting` 6.43.0 that the assessment didn't cover:

1. **Internal endpoints on every app.** The host adds `/health-check`, `/restart` (stops the process, no authentication) and debug-view (prints configuration and environment variables when `Debug`/`DebugView` is on). They are active while `CommonConstants.InternalApp` is true, which is the default. Platform external APIs switch them off with `CommonConstants.MarkAsExternal()` outside Development (`dEngage.Common.AspNetCore` → `UseExternalApiDengageCommonDefaults`). **The Api now does the same**; `PlatformHostingTests` guards it (verified to fail when the call is removed). The Consumer keeps them, being cluster-internal.
2. **Environment name.** `DengageWebApplicationBuilder` reads only `WEB_APPLICATION_ENVIRONMENT` and defaults to Production. `Program.cs` fills it from `--environment` / `ASPNETCORE_ENVIRONMENT` / `DOTNET_ENVIRONMENT`. Without this bridge the integration tests ran as Production (178 failures).
3. **Metrics file is mandatory.** The host loads `appsettings.metrics.json` with `optional: false`; both services ship one (metrics disabled by default, as on other platform services).
4. **Startup network call.** `CommonConstants`' static constructor opens a UDP socket towards 8.8.8.8 to find the local IP. That needs a default route. Worth knowing for air-gapped on-prem installs (assessment 7.5 model B/C).
5. **Transitive `dEngage.SecMan`.** `Common.Hosting` → `Common.Utilities` → `Utilities.LogEvent` → `Common.Shared` → `dEngage.SecMan`. Nothing in loyalty calls it; the boundary test allows it explicitly. The assessment's 7.4 allow-list should mention this.

## 3. Other findings

- **EF Core 9 test-factory change.** `AddDbContext` now also registers `IDbContextOptionsConfiguration<T>`; the test factories must remove it before swapping providers (172 failures until fixed).
- **EF Relational version.** Npgsql's EF provider only asks for Relational ≥ 9.0.1; pinned to 9.0.13 in Schema so all projects share one assembly.
- **Committed master key.** `src/dEngage.Loyalty.Consumer/Properties/launchSettings.json` contains `LOYALTY_MASTER_KEY` in plain text, next to `ENC(...)` values in the committed Consumer development settings. If that key is used anywhere other than local development it should be rotated. Moving it out of the committed file changes every developer's setup, so it's left for a team decision.
- **Visual Studio after the upgrade.** A solution that was open while the projects were still net8.0 keeps its old evaluation and re-restores them as net8.0 (errors NU1202 / NETSDK1005). Close and reopen the solution once. VS 2022 17.14 supports the 9.0.3xx SDK.
- **`/swagger` 404.** The launch profiles open `/swagger`, but the Api has no Swagger (also on `main`). Unchanged here.
- **Private NuGet feed off-VPN.** `nugetint.dengage.com` only resolves through the VPN DNS (10.97.0.2). If Windows asks the home router first, restore fails with NU1301; `--ignore-failed-sources` works when the dEngage packages are already cached.

## 4. Verification

| Check | Result |
|---|---|
| `dotnet build dEngage.Loyalty.sln` | ✅ 0 errors, 1 warning (pre-existing CS1998 in `RuleVersioningServiceTests`) |
| `dotnet test src/dEngage.Loyalty.Engine.Tests` | ✅ 142/142 (132 before + 10 new) |
| `dotnet test src/dEngage.Loyalty.IntegrationTests` (Docker) | ✅ 325/325 (322 before + 3 new). One earlier run had 2 Testcontainers Docker-pipe timeouts that passed on re-run |
| Consumer, real run against local docker-compose | ✅ starts on the platform host, decrypts `ENC(...)` dev values, loads rules, listens on `q.loyalty`; `/health-check` on :5174 |
| Api, real run against local docker-compose | ✅ `/health`, `/health/ready` (postgres/redis/rabbitmq ok), platform `/health-check`, `/api/v1/ping` |
| `dotnet publish -c Release` (Api, Consumer) | ✅ |
| `docker build` | ❌ **not run**: the base image registry and the NuGet feed are behind the VPN DNS issue above |
| Shared Bitbucket pipeline with this `build.json` | ❌ **not run**; to confirm the shared pipeline handles projects under `src/` (platform repos keep projects at the repo root) |

## 5. Files

| File | Change | Why |
|---|---|---|
| `NuGet.Config` | new | Private dEngage feed (same as platform repos) |
| `.dockerignore` | new | Keep `.env`, dev settings, launch settings, portals and build output out of image builds |
| `build.json` | new | Shared pipeline: two images (`dengage.loyalty.api`, `dengage.loyalty.consumer`) |
| `bitbucket-pipelines.yml` | new | Shared dEngage pipeline hook (copied from platform repos) |
| `src/dEngage.Loyalty.Api/Dockerfile` | new | Platform image pattern (noble 9.0 base, `dengageuser` 1566) |
| `src/dEngage.Loyalty.Consumer/Dockerfile` | new | Same |
| `src/dEngage.Loyalty.Api/appsettings.logging.json` | new | NLog config (console; environments add Graylog/Seq in their mounted file) |
| `src/dEngage.Loyalty.Api/appsettings.metrics.json` | new | Required by the platform host |
| `src/dEngage.Loyalty.Consumer/appsettings.logging.json` | new | Same as Api |
| `src/dEngage.Loyalty.Consumer/appsettings.metrics.json` | new | Same as Api |
| `src/*/*.csproj` (12) | changed | `net8.0` → `net9.0`; Microsoft.* 8.x → 9.0.13; Npgsql 8.x → 9.0.4 |
| `src/dEngage.Loyalty.Schema/dEngage.Loyalty.Schema.csproj` | changed | Also pins `Microsoft.EntityFrameworkCore.Relational` 9.0.13 |
| `src/dEngage.Loyalty.Api/dEngage.Loyalty.Api.csproj` | changed | Also adds `dEngage.Common.Hosting` 6.43.0 |
| `src/dEngage.Loyalty.Consumer/dEngage.Loyalty.Consumer.csproj` | changed | Also adds `dEngage.Common.Hosting` 6.43.0 and the ASP.NET Core framework reference |
| `src/dEngage.Loyalty.Api/Program.cs` | changed | Platform host, environment bridge, NLog bootstrap, `IAppSettings`, `MarkAsExternal()`, NLog flush on exit |
| `src/dEngage.Loyalty.Api/appsettings.json` | changed | `Logging` moved to `appsettings.logging.json`. No Kestrel endpoint: one here would override the local launch profiles (the `https` profile stopped listening on :7142). Containers use :8080 unless the mounted appsettings sets one |
| `src/dEngage.Loyalty.Api/HealthChecks.cs` | changed | `/health/ready` logs exceptions instead of returning their text |
| `src/dEngage.Loyalty.Consumer/Program.cs` | changed | Platform host; registrations kept verbatim inside `ConfigureServices`; `ConsumerConfig.FromConfiguration` |
| `src/dEngage.Loyalty.Consumer/ConsumerConfig.cs` | changed | Fail-fast `FromConfiguration`; no hard-coded defaults; legacy `Redis` key fallback |
| `src/dEngage.Loyalty.Consumer/appsettings.json` | changed | No connection values; `Logging` moved out; Kestrel endpoint `:5174` |
| `src/dEngage.Loyalty.Consumer/appsettings.Development.json` | changed | `Redis` → `RedisConnectionString` (values unchanged) |
| `src/dEngage.Loyalty.Shared/Security/SecretCrypto.cs` | changed | Key from `LOYALTY_MASTER_KEY_FILE` first, then `LOYALTY_MASTER_KEY` |
| `src/dEngage.Loyalty.IntegrationTests/Fixtures/CustomWebApplicationFactory.cs` | changed | Remove EF Core 9's `IDbContextOptionsConfiguration<T>` before swapping providers |
| `src/dEngage.Loyalty.IntegrationTests/Fixtures/PostgresWebApplicationFactory.cs` | changed | Same |
| `src/dEngage.Loyalty.IntegrationTests/Api/PlatformHostingTests.cs` | new | Platform `/health-check` works; `/restart` and debug-view are off outside Development |
| `src/dEngage.Loyalty.Engine.Tests/SecretCryptoKeyLoadingTests.cs` | new | Key file wins, env fallback, clear errors, round trip |
| `src/dEngage.Loyalty.Engine.Tests/ConsumerConfigTests.cs` | new | Fail-fast config, legacy Redis key, port handling |
| `src/dEngage.Loyalty.Engine.Tests/CoreDependencyBoundaryTests.cs` | new | D13 dependency allow-list over the resolved closure |
| `.claude/rules/backend.md` | changed | Stack versions; new "Hosting and configuration" conventions |
| `CLAUDE.md` | changed | Runtime/hosting summary; deployed-secrets note |
| `.env.example` | changed | `CONFIG_PATH`, `NLOG_CONFIG_PATH`, `LOYALTY_MASTER_KEY_FILE`, `LOYALTY_MASTER_KEY` (empty) |
| `docs/platform-alignment/phase-1.md` | new | This record |

Not touched: the two staged scope-change documents already on `main` (`2026-10-05-rule-config-limits-by-trigger-test-plan.md`, `2026-10-13-event-throughput-partitioning-and-bulk.md`).
