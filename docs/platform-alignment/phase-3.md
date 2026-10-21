# Platform alignment — Phase 3 implementation record

**Status:** in progress on branch `platform-alignment/phase-2` (continued there, on top of Phase 2 `12e4d38`).
**Basis:** *Loyalty Platform Alignment Assessment*, Phase 3 = "Convergence": split `Public.Api` from `Api`, Nancy → controllers, messaging → common broker / outbox library, publish `Api.Client` (D7, D8, D9 complete). Approved for implementation phase by phase.
**Order:** controllers first (everything else builds on them), then the `Public.Api` split, the `Api.Client` package, and messaging last.

## Status

| Item | Status |
|---|---|
| D7 Nancy → ASP.NET Core controllers | ✅ Done and tested (§1) |
| D9 `Public.Api` split | Not started |
| `dEngage.Loyalty.Api.Client` package | Not started |
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
