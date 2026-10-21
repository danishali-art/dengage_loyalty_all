---
paths:
  - "src/**/*.cs"
  - "src/**/*.csproj"
  - "src/**/appsettings*.json"
  - "dEngage.Loyalty.sln"
  - "docker-compose.yml"
  - "scripts/**"
---

# Backend — shared rules (every .NET project)

Per-project rules are in their own files and load when you touch that project:
`backend-api.md`, `backend-api-framework.md`, `backend-consumer.md`, `backend-rule-engine.md`,
`backend-engine-framework.md`, `backend-ledger.md`, `backend-schema-shared.md`, `backend-tests.md`.

## Stack (don't swap without a Scope Change Request)
- .NET 9 across all projects (the `Contract` package targets netstandard2.0). ASP.NET Core controllers for HTTP (Nancy until platform alignment D7). EF Core 9 + Npgsql 9
  (Postgres). StackExchange.Redis. RabbitMQ.Client 6. FluentValidation 11.
  UUIDNext for database-friendly Guids. The Api and Consumer run on the dEngage platform host
  (`dEngage.Common.Hosting`, see "Hosting and configuration" below).
- Tests use xUnit, FluentAssertions, Moq, Testcontainers and `Microsoft.AspNetCore.Mvc.Testing`.
- Don't add NuGet packages or change versions without being asked. Match the versions already
  pinned in the other csprojs (Microsoft.* 9.0.13, Npgsql 9.0.4, dEngage.Common.* 6.43.0).
- `NuGet.Config` adds the private feed `nugetint.dengage.com`, which only resolves on the dEngage
  VPN. Off the VPN (or if Windows asks the home router before the VPN DNS), restore with
  `dotnet restore dEngage.Loyalty.sln --ignore-failed-sources`; the dEngage packages then come
  from the local NuGet cache.

## Hosting and configuration (platform alignment Phase 1)
- **Hosts:** the Api uses `DengageWebApplicationBuilder`, the Consumer `DengageHostBuilder`. Both
  read every `appsettings*.json` from the folder in `CONFIG_PATH` (deployed environments mount
  their files there) or, when unset, from next to the binary. Files: `appsettings.json`,
  `appsettings.{Environment}.json`, `appsettings.logging.json` (NLog), `appsettings.metrics.json`
  (required by the host). Environment variables override files.
- **Environment:** the platform builder only reads `WEB_APPLICATION_ENVIRONMENT`; `Program.cs`
  fills it from `--environment` / `ASPNETCORE_ENVIRONMENT` / `DOTNET_ENVIRONMENT`. Keep that
  bridge, or `WebApplicationFactory`'s `UseEnvironment("Testing")` silently becomes Production.
- **Platform endpoints:** the host adds `/health-check`, `/restart` and debug-view. The Api calls
  `CommonConstants.MarkAsExternal()` outside Development, which switches `/restart` and
  debug-view off (guarded by `PlatformHostingTests`). The Consumer listens on `:5174` for health
  and metrics only; keep that port cluster-internal.
- **Fail fast:** no hard-coded connection defaults. `ApiOptions`/`ConsumerConfig.FromConfiguration`
  reject missing `ConnectionString`, `RedisConnectionString` (Consumer still accepts the legacy
  `Redis` key) and RabbitMQ host/user/password at startup. Connection values live only in
  `appsettings.Development.json` (local) or the mounted files, never in the base `appsettings.json`.
- **RabbitMQ connection (alignment D8):** `BrokerConnectionSettings` (Shared) is the only reader,
  for the Api, Public.Api and Consumer. It takes the platform's `appsettings.messagebroker.json`
  section `RabbitMQSettings` (`RabbitMQHost`, `RabbitMQPort`, `RabbitMQUsername`,
  `RabbitMQPassword`, `VirtualHost`, `UseSsl`) when `RabbitMQHost` is set, else the legacy
  `RabbitMq:Host/Port/User/Password` keys (local dev files still use these) — never a mix. Loyalty's
  own messaging keys (`RabbitMq:GenericEventTypes`, `RabbitMq:IsolatedTenants`) stay under
  `RabbitMq`. The transport stays loyalty's own RabbitMQ.Client code; the platform's MassTransit
  broker library is deliberately not used (wire contract, ordering, tenant lanes).
- **Secrets:** deployed environments mount secrets as config files under `CONFIG_PATH`. `ENC(...)`
  values still work; the key comes from the file in `LOYALTY_MASTER_KEY_FILE` (mounted secret),
  else from `LOYALTY_MASTER_KEY` (local development).
- **Runtime settings (D4):** the tables are the **only** source — no defaults in code or
  appsettings — and the two kinds never mix (CHECK constraints in both tables):
  - *global* settings: one row in `loyalty_config`, read with `ILoyaltyConfig.GetAsync<T>(module, parameter, ct)`;
  - *tenant* settings (`LoyaltyConfigKeys.TenantSettings`): one row per tenant in
    `loyalty_tenant_config`, read with `GetForTenantAsync<T>(module, parameter, tenantGuid, ct)`.
    There is **no global fallback**; `PlatformAppService.CreateTenantAsync` writes a new tenant's rows
    (`LoyaltyConfigDefaults.NewTenant`) in the tenant's transaction.
  A missing row fails with `loyalty_config_missing`. Adding a setting needs, in the same change:
  the key in `LoyaltyConfigKeys`, an entry in `LoyaltyConfigDefaults` (`Global` or `NewTenant`), and
  a migration — a seed row for a global setting (copy `LoyaltyConfigDefaultsAlignD4`), or the
  `TenantSettings` list change plus a backfill of existing tenants for a tenant setting (copy
  `LoyaltyTenantConfigAlignD4`). Validate values at the call site. Rows are managed with SQL and
  cached for 1 minute. Startup/infrastructure values (connections, keys, topology) stay in
  appsettings. Tests: Sqlite hosts seed global rows with `LoyaltyConfigDefaults.EnsureSeededAsync`
  and give tenants their rows through `TenantSettingsSeedingInterceptor`; hand-built services get
  `DefaultLoyaltyConfig` (IntegrationTests/Fixtures).
- **Dependency boundary (D13):** core projects may depend only on `dEngage.Common.*` packages.
  `dEngage.Framework`, `Parameters`, `Models`, `DbManager`/`ConManager` and other platform
  libraries are allowed only in the future dEngage connector projects. `CoreDependencyBoundaryTests`
  checks the resolved dependency closure.
- **Images:** `src/dEngage.Loyalty.{Api,Consumer}/Dockerfile`, built with the repo root as context
  (`.dockerignore` keeps `.env`, `appsettings.Development.json`, launch settings and the portals
  out). `build.json` + `bitbucket-pipelines.yml` hook into the shared dEngage pipeline.

## Project reference graph (actual — keep it acyclic)
```
Contract          → (nothing; netstandard2.0, published as a NuGet package)
Shared            → Contract
Schema            → Shared
Engine.Framework  → Schema, Shared
Ledger            → Schema, Shared, Engine.Framework
RuleEngine        → Schema, Shared, Ledger
Api.Framework     → Schema, Shared
Api               → Api.Framework, RuleEngine, Ledger, Schema, Shared
Consumer          → RuleEngine, Ledger, Engine.Framework, Schema, Shared
CryptoCli → Shared · TestCli → Consumer, Ledger, RuleEngine, Schema, Shared
```
- Never add a reference that points "up" this graph (for example Schema → anything, Ledger →
  RuleEngine, Engine.Framework → Ledger, or anything → Api/Consumer). When a lower project needs
  something from a higher one, add an interface in the lower project and implement it higher up
  (the existing examples are `IEventPublisherSink` and `ITenantScopedEntity`).
- Hosts (`Api`, `Consumer`) are composition roots. DI wiring lives in their `Program.cs`, or in a
  framework `AddLoyalty*Framework()` extension.

## Cross-cutting conventions
- **Async:** every I/O method is `async Task`, takes a `CancellationToken ct` and passes it
  through. No `.Result` or `.Wait()`.
- **Style:** file-scoped namespaces, primary constructors for services, `sealed` by default,
  `sealed record` for DTOs and messages.
- **Money and points:** `decimal`, stored as `numeric(20,4)`. Never `double`/`float`. Format with
  `CultureInfo.InvariantCulture`.
- **Time:** always UTC (`DateTime.UtcNow`, `.ToUniversalTime()`). `IClock` exists in
  Engine.Framework. Prefer it for new time-dependent logic you want to unit-test, but don't
  mass-refactor existing `DateTime.UtcNow` calls.
- **Ids:** `Guid` primary keys. Engine-side new ids use `IIdGenerator` (Postgres-friendly UUIDs).
- **Tenancy:** the external tenant id is a string **slug**. Tables store an internal `TenantId`
  Guid. Convert with `ITenantSlugResolver`. Every query on tenant-owned data filters by tenant.
- **Constants:** wire names (event types, outbound event types, outcome reasons, topology names)
  live in `dEngage.Loyalty.Contract` (`EventTypes`, `OutboundEventTypes`, `OutcomeReasons`,
  `MessagingTopology`); engine and DB constants (rule types, statuses, reasons) in
  `dEngage.Loyalty.Shared` (`RuleTypes`, `LedgerReason`, ...). Never hard-code those strings anywhere else.
- **Domain errors:** below the API, fail with `InvalidOperationException("snake_case_code: detail")`.
  The API maps the prefix to an HTTP status. Don't throw HTTP types from domain code.
- **Config and secrets:** config values can carry `ENC(...)` tokens resolved by
  `SecretCrypto` at startup (encrypt them with `CryptoCli`). Never put plaintext secrets in
  `appsettings*.json`. Local credentials go in `.env`.
- **Logging:** use structured templates (`"... {Tenant} {Rule}"`), never string interpolation in
  log calls. Never log secrets, API keys, JWTs or full PII payloads.
- **Comments:** explain **why** (a contract, a race, an earlier bug, a CR reference such as
  `CR-09`), in the existing style.

## Definition of done (backend)
`dotnet build dEngage.Loyalty.sln` is clean, the relevant `dotnet test` projects pass (see
`backend-tests.md`), and docs are updated (`00-project-guardrails.md` §5). List every changed file.
