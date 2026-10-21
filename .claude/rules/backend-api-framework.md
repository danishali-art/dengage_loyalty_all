---
paths:
  - "src/dEngage.Loyalty.Api.Framework/**"
---

# dEngage.Loyalty.Api.Framework — API cross-cutting infrastructure

Shared plumbing for the HTTP host: ASP.NET Core controller base and middleware, auth, tenancy,
errors, JSON, pagination, validation, rate limiting, options and messaging. It references **only
Schema and Shared**. It must never reference Api, RuleEngine, Ledger or Consumer.

**Changes here affect every endpoint.** Keep them minimal, backward compatible, and covered by
IntegrationTests (`HostBootSmokeTests` at a minimum).

## Bootstrap (`Bootstrap/`)
- `AddLoyaltyApiFramework(configuration)` is the only entry point that registers framework
  services, including MVC through the platform's `AddDengageCommonAspNetCoreModule`. Add new
  framework services here, not in `Program.cs`.
- `UseLoyaltyApi(corsPolicy)` builds the whole pipeline on the platform's
  `UseExternalApiDengageCommonDefaults` and puts `LoyaltyApiMiddleware` after routing. Don't call
  `MapControllers()` or map endpoints separately.
- Keep the three deviations from the platform defaults: the platform's `GeneralExceptionFilter`
  removed (its `{ message }` body would replace the API's ProblemDetails), the request/response
  logger off (it logs headers and bodies: JWTs, API keys, passwords, PII), and Swagger only via
  `SwaggerSettings:IsActive`. `PlatformAspNetCoreApiTests` guards the first and CORS on errors.

## Middleware and controller base (`Controllers/`) — the security boundary
- `LoyaltyApiMiddleware` runs for every `/api` request. It sets the correlation id and resolves a
  JWT bearer or `X-Api-Key` into one `AuthenticatedPrincipal`, but **never rejects** a request
  itself: actions decide through the `LoyaltyControllerBase` guards. Keep that split. It also turns
  every exception, unmatched route (404) and wrong verb (405) into a ProblemDetails body.
- `LoyaltyControllerBase` is deliberately not `[ApiController]` and actions take no bound
  parameters (only `CancellationToken`): bodies are read with `ReadValidatedJsonBodyAsync` and
  route values with `RouteParam` / `RouteGuidParam`, so bad input gets the API's own error bodies
  instead of MVC's automatic 400s. Keep it that way.
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
  **only** REST serializer config. MVC's output formatters are bypassed on purpose: responses go
  through `JsonResponses` (`SerializedResult`), which also keeps the Content-Type exactly
  `application/json` / `application/problem+json`.
- `JsonConventions.EventDataOptions` (**snake_case**) is for event envelope `Data` only. Consumer
  handlers depend on it, and mixing the two up fails silently.
- `DecimalStringJsonConverter` writes decimals as JSON strings and reads both strings and numbers.
  Don't remove it or narrow what it reads.

## Options (`Options/`)
- Every options class is bound with `.ValidateOnStart()` and has an `IValidateOptions<T>`
  validator, so a bad config fails at startup, not on the first request. New options follow the
  same pattern.

## Event ingestion (`Events/`)
- `EventsAppService` and the event request validators are shared by Public.Api (ingestion) and
  the admin Api (simulate, types, status), so they live here and are registered by
  `AddLoyaltyApiFramework`. A new event request validator must be added to that list: each host
  only scans its own assembly for validators.
- The request/response records are the public contract and live in `dEngage.Loyalty.Contract`.
- `AddLoyaltyJwtAuthentication` is separate on purpose: Public.Api doesn't call it.

## Rate limiting and messaging
- `IRateLimiter` / `RedisRateLimiter` is a fixed-window limiter keyed by `(bucket, tenantId)`. Tests
  use `FakeRateLimiter`.
- `IEventPublisher` / `RabbitMqEventPublisher` publishes to the `MessagingTopology.Exchange`
  constant, with the event type as routing key. Never hard-code exchange or queue names.
