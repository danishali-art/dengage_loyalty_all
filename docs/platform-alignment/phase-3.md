# Platform alignment — Phase 3 implementation record

**Status:** in progress on branch `platform-alignment/phase-2` (continued there, on top of Phase 2 `12e4d38`).
**Basis:** *Loyalty Platform Alignment Assessment*, Phase 3 = "Convergence": split `Public.Api` from `Api`, Nancy → controllers, messaging → common broker / outbox library, publish `Api.Client` (D7, D8, D9 complete). Approved for implementation phase by phase.
**Order:** controllers first (everything else builds on them), then the `Public.Api` split, the `Api.Client` package, and messaging last.

## Status

| Item | Status |
|---|---|
| D7 Nancy → ASP.NET Core controllers | ✅ Done and tested (§1). Committed `3b2b420` |
| D9 `Public.Api` split | ✅ Done and tested (§2). Committed `da3100c` |
| `dEngage.Loyalty.Api.Client` package | ✅ Done and tested (§3) |
| D8 messaging → common broker / outbox library | Not started |

## 1. D7 — Nancy → ASP.NET Core controllers (done)

### 1.1 What was built
- The 14 Nancy modules became 14 controllers (`<Resource>/<Resource>Controller.cs`, renamed with `git mv` so history follows): 77 actions, the same routes, verbs, auth guards, request bodies and responses.
- `Api.Framework/Controllers/LoyaltyControllerBase` replaces `TenantScopedModule`: the same guards (`RequireAuthenticated`, `RequireTenantScope`, `RequirePlatformAdmin`, `RequireApiKeyPrincipal`) and helpers (`RouteParam`, `RouteGuidParam`, `ReadPageRequest`).
- `Api.Framework/Controllers/LoyaltyApiMiddleware` replaces the Nancy bridge, the bootstrapper's `OnError` hook, the `BeforeRequest` auth hook and the 404/405 status handler: for every `/api` request it sets the correlation id, resolves the JWT or API key into the principal, and turns exceptions, unmatched routes and wrong verbs into the API's ProblemDetails body.
- `JsonResponses` now returns `IActionResult`s that write the body with `JsonConventions.Options` and the exact Content-Type the API always sent (`application/json`, `application/problem+json`).
- Removed: the `Nancy` package, `CompositionRootNancyBootstrapper`, `NancyAspNetCoreMiddleware`, `NancyModuleTypeRegistry`, `AmbientServiceProviderAccessor`, `AuthenticationPipelineHook`, `ApiStatusCodeHandler`, `TenantScopedModule` and `AddNancyModule<T>()`. Controllers are discovered by `MapControllers()`; there's nothing to register per resource any more.
- Vue portal OpenAPI generator (`webVue3/tools/openapi`): reads controllers (`[Route]`, `[HttpGet("…")]`, primary-constructor parameters) instead of Nancy `MapGet(...)` calls. Its unit-test fixtures were converted too.

### 1.2 Decisions
- **Plain ASP.NET Core controllers, not `dEngage.Common.AspNetCore`'s MVC setup.** `AddDengageCommonAspNetCoreModule` switches MVC to Newtonsoft JSON, adds `JsonStringEnumConverter`, Swagger, and an exception filter that returns `{ "message": … }`. Loyalty's wire contract (decimals as JSON strings, camelCase System.Text.Json, ProblemDetails with `code`) is what both portals parse, so adopting it would be a breaking API change. The platform's hosting (`DengageWebApplicationBuilder`, Phase 1) stays.
- **No `[ApiController]`, no model binding.** Actions take only a `CancellationToken`; bodies are read with `ReadValidatedJsonBodyAsync` and route values with `RouteParam`/`RouteGuidParam`, exactly as the modules did. That keeps malformed-input errors identical, rather than MVC's automatic 400 bodies.

### 1.3 Behaviour kept as it was (noted, not changed)
- A request body that isn't valid JSON returns **500 `internal_error`** (a `JsonException` isn't mapped), as it did under Nancy. Mapping it to 400 `invalid_request` is a small follow-up, deliberately left out of a pure framework swap.
- `HEAD`/automatic `OPTIONS` for GET routes, which Nancy answered implicitly, aren't provided. Neither portal uses them; CORS preflight is handled by the CORS middleware outside Development.

### 1.4 Verification
| Check | Result |
|---|---|
| `dotnet build dEngage.Loyalty.sln` | ✅ 0 errors |
| Integration tests (API tests drive every route over HTTP) | ✅ 345/345 (199 API + 146 end-to-end) |
| Engine tests | ✅ 155/155 |
| OpenAPI spec regenerated from the controllers (`npm run api:spec && npm run api:types`) | ✅ `openapi.json` (55 paths, 112 schemas) and `schema.d.ts` **identical** to the committed files, so routes, parameters, bodies and responses didn't change; `npm run api:check` passes |
| Portal: `vue-tsc -b`, ESLint, Vitest | ✅ 0 errors; 228/228 tests (incl. the generator's 8) |
| Local run (`--launch-profile http`) | ✅ `/api/v1/ping` 200 `application/json`; unauthenticated tenant route 401, unknown route 404, wrong verb 405, empty login 400 — all `application/problem+json` with the usual body |

Windows note: the first full test run had 26 end-to-end failures, all `TimeoutException` connecting to Docker's named pipe within 1 ms (many Testcontainers classes starting at once); rerunning them passed 146/146. Same flakiness as in Phase 2.

### 1.5 Files
| File | Change | Purpose |
|---|---|---|
| `src/dEngage.Loyalty.Api/*/<Resource>Controller.cs` (14) | renamed from `<Resource>Module.cs`, rewritten | Controllers |
| `src/dEngage.Loyalty.Api/Program.cs` | changed | `AddNancyModule` calls removed; `MapControllers()` |
| `src/dEngage.Loyalty.Api/HealthChecks.cs` | comment | |
| `src/dEngage.Loyalty.Api.Framework/Controllers/LoyaltyControllerBase.cs`, `LoyaltyApiMiddleware.cs` | new | Guards/helpers; auth, errors, 404/405 |
| `src/dEngage.Loyalty.Api.Framework/Json/JsonResponses.cs`, `ErrorHandling/ErrorResponseFactory.cs`, `Validation/ValidationExtensions.cs` | changed | `IActionResult` / `HttpRequest` versions; exception mapping moved into `ErrorResponseFactory.FromException` |
| `src/dEngage.Loyalty.Api.Framework/Bootstrap/ServiceCollectionExtensions.cs` | changed | `AddControllers()`; `UseLoyaltyApiFramework()` adds the middleware |
| `src/dEngage.Loyalty.Api.Framework/Bootstrap/{CompositionRootNancyBootstrapper,NancyAspNetCoreMiddleware,NancyModuleTypeRegistry,AmbientServiceProviderAccessor,AuthenticationPipelineHook}.cs`, `ErrorHandling/NotFoundStatusCodeHandler.cs`, `Modules/TenantScopedModule.cs` | deleted | Nancy plumbing |
| `src/dEngage.Loyalty.Api.Framework/dEngage.Loyalty.Api.Framework.csproj` | changed | `Nancy` package removed |
| `src/dEngage.Loyalty.IntegrationTests/**` | comments | Nancy wording |
| `webVue3/tools/openapi/{csharp-parse,build-spec,build-spec.spec}.ts`, `webVue3/openapi/overrides.ts` | changed | Generator reads controllers; `excludeControllers` |
| `webVue3/src/**`, `web/src/app/core/**` | comments | `…Module.cs` → `…Controller.cs`, "Nancy API" → "loyalty API" |
| `CLAUDE.md`, `.claude/rules/*.md` | changed | How to add a route now |

## 2. D9 — `Public.Api` split (done)

### 2.1 What was built
- New deployable **`dEngage.Loyalty.Public.Api`**: the 8 API-key ingestion routes (`order-created`, `order-refunded`, `cash-added`, `cash-spent`, `points-redeem`, `points-transfer`, `reward-purchase`, `generic`) and the event status lookup, on the **same paths** as before (`/api/v1/tenants/{tenantId}/events/...`). A deployment sends client systems to it by host name. Platform hosting, NLog/metrics files, health checks (`/health`, `/health/ready`), Dockerfile, `build.json` image `dengage.loyalty.public.api`, local launch profile `http://localhost:5175` (`https` 7143).
- **API keys only.** JWT registration moved out of `AddLoyaltyApiFramework` into `AddLoyaltyJwtAuthentication`, which only the admin Api calls. Without it the middleware ignores a bearer token, so Public.Api has no JWT signing key and never accepts a portal token. No CORS (server-to-server).
- **Admin Api** keeps the portal's event routes: `simulate` (admin JWT), `types`, and the status lookup (the simulator polls it). The 8 ingestion routes are gone from it; posting to one there now returns 405 `method_not_allowed`.
- **Shared code:** `EventsAppService` and the event validators moved to `Api.Framework/Events/` (registered by `AddLoyaltyApiFramework`); the request/response records moved to `dEngage.Loyalty.Contract` (`PublicApiEvents.cs`) as the public contract, ready for the `Api.Client` package. `HealthChecks` moved to `Api.Framework/Bootstrap/` for both hosts.
- `run.ps1` starts Public.Api too; both portals' opt-in real-backend Playwright specs (`customers.e2e.spec.ts`) post their event to Public.Api (`PUBLIC_API_URL`, default `http://localhost:5175`).

### 2.2 Not done (and why)
- **Balance / wallet lookup on Public.Api.** The assessment lists it for Public.Api, but no such API-key endpoint exists today, so there was nothing to move. Adding one is new public API surface (which fields, per contact key, rate limits), so it needs its own scope decision. With webhooks dropped (D15), it is how a loyalty-only client would read current state.
- **`Job.Service`** (the scheduled jobs on a single runner): left to the event-throughput change, as agreed for Phase 1.

### 2.3 Verification
| Check | Result |
|---|---|
| `dotnet build dEngage.Loyalty.sln` | ✅ 0 errors |
| Integration tests | ✅ 350/350 (345 + 5 new `PublicApiSplitApiTests`: ingestion and status by API key on Public.Api; a portal token is ignored there (401); Public.Api serves no admin routes (404) and no `simulate` (405); the admin Api refuses ingestion (405); Public.Api health) |
| Engine tests (incl. the D13 boundary test, which covers the new project automatically) | ✅ 155/155 |
| OpenAPI spec regenerated | ✅ Exactly the 8 ingestion operations and their 8 request schemas removed (47 paths, 104 schemas); every remaining operation byte-identical. `npm run api:check` passes |
| Portal: `vue-tsc -b`, Vitest | ✅ 0 errors; 228/228 (one run had 2 intermittent failures; three runs since passed) |
| Local run (`--launch-profile http`, local Postgres/Redis/RabbitMQ) | ✅ `/health/ready` all ok without any JWT configuration; ingestion without a key or with an unknown key 401; `/programs` 404 |

### 2.4 Files
| File | Change | Purpose |
|---|---|---|
| `src/dEngage.Loyalty.Public.Api/**` | new | Host: `Program.cs`, `Events/EventsController.cs`, appsettings (`.json`, `.logging`, `.metrics`, `.Staging`, `.Production`, `.Development.example`), launch profile, Dockerfile |
| `src/dEngage.Loyalty.Api/Events/EventsController.cs` | changed | Only `simulate`, `types`, status |
| `src/dEngage.Loyalty.Api/Program.cs` | changed | `AddLoyaltyJwtAuthentication`; events service now registered by the framework |
| `src/dEngage.Loyalty.Contract/PublicApiEvents.cs` | moved from `Api/Events/EventsDtos.cs` | Public request/response bodies |
| `src/dEngage.Loyalty.Api.Framework/Events/EventsAppService.cs`, `EventsValidators.cs` | moved from `Api/Events/` | Shared ingestion service |
| `src/dEngage.Loyalty.Api.Framework/Bootstrap/HealthChecks.cs` | moved from `Api/` | Shared health checks |
| `src/dEngage.Loyalty.Api.Framework/Bootstrap/ServiceCollectionExtensions.cs` | changed | Events service and validators; `AddLoyaltyJwtAuthentication` split out |
| `src/dEngage.Loyalty.Api.Framework/Controllers/LoyaltyApiMiddleware.cs` | changed | Bearer token only where JWT is registered |
| `src/dEngage.Loyalty.IntegrationTests/Fixtures/PublicApiWebApplicationFactory.cs` | new | Public.Api test host sharing the Api factory's DB, publisher and rate limiter (`CustomWebApplicationFactory.PublicApi`) |
| `src/dEngage.Loyalty.IntegrationTests/Api/PublicApiSplitApiTests.cs` | new | Split tests |
| `src/dEngage.Loyalty.IntegrationTests/Api/{PointsTransferRoute,LoyaltyConfig}ApiTests.cs` and 2 others | changed | Post through Public.Api; namespaces |
| `dEngage.Loyalty.sln`, `build.json`, `.gitignore`, `run.ps1` | changed | New project, image, local dev settings file, local stack |
| `webVue3/openapi/openapi.json`, `webVue3/src/shared/api/generated/schema.d.ts` | regenerated | Ingestion routes no longer on the admin Api |
| `web/e2e/customers.e2e.spec.ts`, `webVue3/e2e/customers.e2e.spec.ts`, `webVue3/playwright.config.ts` | changed | Event posted to Public.Api |
| `CLAUDE.md`, `.claude/rules/{backend,backend-api,backend-api-framework,01-workflow-and-debugging}.md` | changed | Two HTTP hosts |

## 3. `dEngage.Loyalty.Api.Client` package (done)

### 3.1 What was built
- **`dEngage.Loyalty.Api.Client`** (netstandard2.0, version 1.0.0, published through `build.json` like `Contract`): a typed `HttpClient` for **Public.Api** — `ILoyaltyApiClient` with one `Publish…Async` per ingestion route plus `GetEventStatusAsync`.
- **Platform shape:** registered with `services.AddLoyaltyApiClient(publicApiBaseUrl, …)` (like `AddCouponApiClient`), returning the `IHttpClientBuilder` so callers can add handlers. A base URL with a path prefix works.
- **Per-tenant API keys:** `ILoyaltyApiKeyProvider` (the Phase 4 connector serves many tenants, each with its own key); `StaticLoyaltyApiKeyProvider` for a fixed list. A tenant without a key fails before any request is sent.
- **Results, not exceptions, for HTTP errors** (as the platform's `ApiClientHttpResponse`): `LoyaltyApiResult<T>` with the status, the body, or `LoyaltyApiProblem` (the API's error body: `code`, `title`, `traceId`, per-field `errors`). A non-API error body (for example a proxy's 502 page) becomes code `http_<status>`.
- **One wire format:** `DecimalStringJsonConverter` moved from `Api.Framework` into `Contract`, so the server and the client share it (camelCase, nulls omitted, decimals as JSON strings).

### 3.2 Decisions
- **No `dEngage.Common.Shared` dependency** (unlike the coupon client): it would pull the platform's SecMan and Newtonsoft into every consumer. The package depends on `dEngage.Loyalty.Contract` and `Microsoft.Extensions.Http` only (checked in the packed `.nuspec`).
- **Public.Api only.** The admin Api is authenticated by portal logins (JWT); a service-to-service way in is part of Phase 4 (D6), so there's nothing for a client to call there yet. The balance/wallet lookup (§2.2) would be added to this client once it exists.

### 3.3 Verification
| Check | Result |
|---|---|
| `dotnet build dEngage.Loyalty.sln`; `dotnet pack` | ✅ 0 errors, 0 warnings; `.nuspec` dependencies: `dEngage.Loyalty.Contract` 1.0.0, `Microsoft.Extensions.Http` 9.0.9 |
| `LoyaltyApiClientTests` (7) against the real Public.Api host | ✅ publish with idempotency key (= event id), decimal sent as `"10.5"`; validation failure → `invalid_request` with field errors and trace id; unknown key → 401 `unauthorized`; unknown event → 404; missing key → fails before any request; registration keeps the base path, escapes the tenant, sends the headers and the API's JSON; a non-API 502 body → `http_502` |
| Integration / engine tests | ✅ 357/357, 155/155 |
| `npm run api:check` | ✅ unchanged (the converter move doesn't affect the spec) |

### 3.4 Files
| File | Change | Purpose |
|---|---|---|
| `src/dEngage.Loyalty.Api.Client/**` | new | `ILoyaltyApiClient`/`LoyaltyApiClient`, `LoyaltyApiResult`/`LoyaltyApiProblem`, `ILoyaltyApiKeyProvider`, `LoyaltyApiClientModule`, `IsExternalInit` polyfill |
| `src/dEngage.Loyalty.Contract/DecimalStringJsonConverter.cs` | moved from `Api.Framework/Json/` | Shared wire format |
| `src/dEngage.Loyalty.Api.Framework/Json/JsonConventions.cs` | changed | Uses the Contract converter |
| `src/dEngage.Loyalty.IntegrationTests/Api/LoyaltyApiClientTests.cs`, test `.csproj` | new / changed | Client tests |
| `dEngage.Loyalty.sln`, `build.json` | changed | New project; NuGet package |
