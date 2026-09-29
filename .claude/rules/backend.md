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
- .NET 8 across all 12 projects. Nancy 2 (hosted in ASP.NET Core) for HTTP. EF Core 8 + Npgsql
  (Postgres). StackExchange.Redis. RabbitMQ.Client 6. FluentValidation 11.
  UUIDNext for database-friendly Guids.
- Tests use xUnit, FluentAssertions, Moq, Testcontainers and `Microsoft.AspNetCore.Mvc.Testing`.
- Don't add NuGet packages or change versions without being asked. Match the versions already
  pinned in the other csprojs.

## Project reference graph (actual — keep it acyclic)
```
Shared            → (nothing)
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
- **Constants:** event types, rule types, statuses, reasons and topology names live in
  `dEngage.Loyalty.Shared` (`EventTypes`, `RuleTypes`, `LedgerReason`, `MessagingTopology`, ...).
  Never hard-code those strings anywhere else.
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
