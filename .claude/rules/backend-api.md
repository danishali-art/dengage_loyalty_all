---
paths:
  - "src/dEngage.Loyalty.Api/**"
---

# dEngage.Loyalty.Api — HTTP API host

Admin/configuration REST API plus event ingestion, as ASP.NET Core controllers
(`http://localhost:5173`). **Reference slice to copy: `Rewards/`.**

## One folder per resource, four files
| File | Contains | Rules |
|---|---|---|
| `<Resource>Controller.cs` | `public sealed class <Resource>Controller(...) : LoyaltyControllerBase` | Routes only. No EF or business logic. |
| `<Resource>AppService.cs` | `I<Resource>AppService` + `public sealed class <Resource>AppService(...)` | All business logic. Returns DTOs, never entities. |
| `<Resource>Dtos.cs` | `sealed record` requests/responses | PATCH request fields are nullable, and null means "unchanged". |
| `<Resource>Validators.cs` | `AbstractValidator<T>` per request | Picked up automatically by `AddValidatorsFromAssemblyContaining<Program>()`. |
Some resources add a registry or helper next to these (for example `RewardTypeRegistry.cs`). Keep
such types in the resource folder.

## Registering in `Program.cs`
- `builder.Services.AddScoped<I<Resource>AppService, <Resource>AppService>();`
- Controllers are discovered automatically (`MapControllers()`); there is nothing to register.

## Controller rules
- The class route is `[Route("api/v1/tenants/{tenantId}/...")]` (tenant-scoped) or
  `api/v1/platform/...` / auth, and each action has `[HttpGet("relative")]`-style attributes.
  Action templates must not start with `/` (that would ignore the class route).
- The app service and validators are primary-constructor parameters. Actions take only a
  `CancellationToken`: no `[FromBody]` / `[FromRoute]` / `[FromQuery]` and no `[ApiController]`
  (see `backend-api-framework.md`).
- The Vue portal's OpenAPI generator (`webVue3/tools/openapi`) reads these files: keep the file
  name `<Resource>Controller.cs`, one action attribute per method, and the helpers below. After a
  route or DTO change run `npm run api:spec && npm run api:types` in `webVue3`.
- Every action starts with an auth guard:
  - tenant routes → `var tenantId = RequireTenantScope(RouteParam("tenantId"));`
  - platform routes → `RequirePlatformAdmin();`
  - server-to-server ingestion → `RequireApiKeyPrincipal();`
  - public routes (login, health) only by explicit decision.
- Ids → `RouteGuidParam("<name>Id")`. Lists → `ReadPageRequest()` → `PagedResult<T>`.
- Bodies → `await Request.ReadValidatedJsonBodyAsync(validator, ct)`. Use unvalidated
  `ReadJsonBodyAsync<T>` only for trivial private bodies (for example `SetActiveRequest(bool)`).
- Responses → `JsonResponses.Ok(x)`, `JsonResponses.Ok(x, HttpStatusCode.Created)`,
  `JsonResponses.NoContent()`. Ingestion returns `Accepted(...)`.

## App service rules
- Load tenant data through `IRepository<TEntity>.Query(tenantId, ct)` / `FindAsync`. If you need
  `LoyaltyDbContext` directly (joins, projections), add the tenant filter yourself.
- Check that the parent belongs to the tenant before touching a child (for example
  `RequireProgramAsync(tenantId, programId, ct)`).
- New entities: `Id = Guid.NewGuid()`, `TenantId = await tenantSlugResolver.ResolveAsync(tenantId, ct)`,
  `CreatedAt = DateTime.UtcNow`.
- Throw `NotFoundApiException("Reward")`, `ConflictApiException(code, msg)` or
  `ValidationApiException(msg)`. Let domain `InvalidOperationException`s bubble up, because the
  framework translates them.
- JSON config columns (`jsonb`, for example `TypeConfig`) are `JsonElement` in DTOs and are
  validated by the resource's registry or validator. Keep their inner keys snake_case.

## Mutations with side effects (must not be skipped)
- **Program / Tier edits** → write a `ConfigVersion` audit row through `IConfigVersionService`.
- **Any change to a program or its nested config** (account types, tiers, rewards, rules, card
  buckets, streak campaigns) → call `IProgramChangeTracker.MarkChangedAsync(programId, ct)` right
  before `SaveChangesAsync`, so a published program shows unpublished changes (1.3.CL). Publish
  (`ProgramsAppService.PublishAsync`) writes the aggregate `ProgramPublication` snapshot.
- **Rule edits** → go through `IRuleVersioningService`, which inserts a new version and never
  updates in place. CASH-target rules save as `PendingApproval` and are activated by a
  *different* admin (`PATCH .../{ruleId}/approve`, `CreatedBy != ApprovedBy`).
- **Rule / campaign / card-bucket changes** → invalidate `IRuleCacheService` /
  `ICampaignConfigCacheService` for that `(tenant, program)`.
- **Validate against the catalog:** a rule's type, trigger and target account kind must be checked
  against `RuleTypeCatalog` (RuleEngine) and the event metadata, and a new rule's Configuration /
  Limits fields against `RuleFieldCatalog` (CR 2026-10-06 Phase 2). Don't keep a second list.

## Events (ingestion)
- Publish only through `IEventsAppService.PublishAsync` → `EventEnvelopeFactory` →
  `IEventPublisher`. It enforces, in order:
  1. the per-tenant ingestion rate limit (`RateLimit:EventIngestionPerMinutePerTenant`),
  2. built-in or allow-listed `RabbitMq:GenericEventTypes` only,
  3. **no internally scheduled types** (`EventTypes.IsExternallyPublishable`; none are built in
     since CR 2026-10-05 removed `points.expired` and, in its addendum A, `birthdaybonus`).
  Never bypass these checks.
- The envelope `Data` is serialized with `JsonConventions.EventDataOptions` (**snake_case**).
- The request's `Idempotency-Key` becomes the `EventId` (a random Guid if absent), which the
  Consumer's `EventInbox` dedupes on. Keep passing it through.
- New built-in event types need, in the same change: a `Contract/EventTypes` constant, event
  metadata, a Consumer handler (see `backend-consumer.md`) and an ingestion route or a
  generic-type config.

## Don't
- Don't use MVC controllers, Minimal API endpoints, `[ApiController]` or attribute routing.
- Don't return EF entities, `IQueryable`, or anonymous objects with inconsistent casing.
- Don't read a tenant id from the body or query string.
- Don't catch exceptions just to turn them into 200s or generic 500s.
