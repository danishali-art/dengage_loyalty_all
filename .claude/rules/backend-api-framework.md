---
paths:
  - "src/dEngage.Loyalty.Api.Framework/**"
---

# dEngage.Loyalty.Api.Framework — API cross-cutting infrastructure

Shared plumbing for the HTTP host: Nancy-on-ASP.NET-Core bootstrap, auth, tenancy, errors, JSON,
pagination, validation, rate limiting, options and messaging. It references **only Schema and
Shared**. It must never reference Api, RuleEngine, Ledger or Consumer.

**Changes here affect every endpoint.** Keep them minimal, backward compatible, and covered by
IntegrationTests (`HostBootSmokeTests` at a minimum).

## Bootstrap (`Bootstrap/`) — fragile, change only with care
- `AddLoyaltyApiFramework(configuration)` is the only entry point that registers framework
  services. Add new framework services here, not in `Api/Program.cs`.
- Modules are registered through `AddNancyModule<T>()`, as **Transient**, both under the concrete
  type and as `INancyModule`, and recorded in `NancyModuleTypeRegistry`.
- `NancyModuleTypeRegistry` is static and **locked** on purpose: parallel xUnit
  `WebApplicationFactory` hosts raced on it. Keep the lock.
- `CompositionRootNancyBootstrapper` feeds module constructor dependencies from ASP.NET Core DI
  into TinyIoC. Nancy's normal extension points are sealed, so read the comments before changing
  anything. Don't "simplify" it back to `DefaultNancyBootstrapper`.
- `AuthenticationPipelineHook` sets the correlation id and resolves a JWT bearer or `X-Api-Key`
  into one `AuthenticatedPrincipal`. It **never rejects** a request itself: routes decide through
  the `TenantScopedModule` guards. Keep that split.

## Modules base (`Modules/TenantScopedModule.cs`)
- `Map*` helpers concatenate the base path themselves, because Nancy's module-path prefix doesn't
  apply to modern `Get(path, handler)`. Keep it that way.
- The guards (`RequireAuthenticated`, `RequireTenantScope`, `RequirePlatformAdmin`,
  `RequireApiKeyPrincipal`) are the security boundary. Changing them needs an explicit
  security review, and tests for platform admin vs tenant admin vs API key vs anonymous.

## Tenancy (`Tenancy/`, `Data/IRepository.cs`)
- `EfRepository<T>` is the generic tenant-filtered repository for every `ITenantScopedEntity`.
  Don't hand-write one repository per entity. Extend the generic one only if **every** aggregate
  needs the change.
- `tenantId` parameters are slugs. The repository resolves them to Guids through
  `ITenantSlugResolver` (backed by `TenantSlugCache`).

## Errors (`ErrorHandling/`)
- `ApiException(status, code, message)` plus its subclasses are the only typed HTTP errors.
  Responses render as ProblemDetails through `ErrorResponseFactory`.
- `DomainErrorTranslator` maps domain `InvalidOperationException` message prefixes to
  404/409/400. When the domain adds a new code, add its prefix to the right list. Unknown codes
  fall back to 400.
- Error `code`s are part of the API contract, and the portal relies on them. Never rename one.

## JSON (`Json/`)
- `JsonConventions.Options` (camelCase, ignore nulls, `DecimalStringJsonConverter`) is the
  **only** REST serializer config. Nancy's own serializer is bypassed on purpose.
- `JsonConventions.EventDataOptions` (**snake_case**) is for event envelope `Data` only. Consumer
  handlers depend on it, and mixing the two up fails silently.
- `DecimalStringJsonConverter` writes decimals as JSON strings and reads both strings and numbers.
  Don't remove it or narrow what it reads.

## Options (`Options/`)
- Every options class is bound with `.ValidateOnStart()` and has an `IValidateOptions<T>`
  validator, so a bad config fails at startup, not on the first request. New options follow the
  same pattern.

## Rate limiting and messaging
- `IRateLimiter` / `RedisRateLimiter` is a fixed-window limiter keyed by `(bucket, tenantId)`. Tests
  use `FakeRateLimiter`.
- `IEventPublisher` / `RabbitMqEventPublisher` publishes to the `MessagingTopology.Exchange`
  constant, with the event type as routing key. Never hard-code exchange or queue names.
