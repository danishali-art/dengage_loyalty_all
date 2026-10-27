# Platform alignment — Phase 3 implementation record

**Status:** in progress on branch `platform-alignment/phase-2` (continued there, on top of Phase 2 `12e4d38`).
**Basis:** *Loyalty Platform Alignment Assessment*, Phase 3 = "Convergence": split `Public.Api` from `Api`, Nancy → controllers, messaging → common broker / outbox library, publish `Api.Client` (D7, D8, D9 complete). Approved for implementation phase by phase.
**Order:** controllers first (everything else builds on them), then the `Public.Api` split, the `Api.Client` package, and messaging last.

## Status

| Item | Status |
|---|---|
| D7 Nancy → ASP.NET Core controllers | ✅ Done and tested (§1). Committed `3b2b420` |
| D9 `Public.Api` split | ✅ Done and tested (§2). Committed `da3100c` |
| `dEngage.Loyalty.Api.Client` package | ✅ Done and tested (§3). Committed `85e00c6` |
| D8 messaging | ✅ Settings step done and tested (§4). Library move **not done by decision** (§4.1) |

## 1. D7 — Nancy → ASP.NET Core controllers (done)

### 1.1 What was built
- The 14 Nancy modules became 14 controllers (`<Resource>/<Resource>Controller.cs`, renamed with `git mv` so history follows): 77 actions, the same routes, verbs, auth guards, request bodies and responses.
- `Api.Framework/Controllers/LoyaltyControllerBase` replaces `TenantScopedModule`: the same guards (`RequireAuthenticated`, `RequireTenantScope`, `RequirePlatformAdmin`, `RequireApiKeyPrincipal`) and helpers (`RouteParam`, `RouteGuidParam`, `ReadPageRequest`).
- `Api.Framework/Controllers/LoyaltyApiMiddleware` replaces the Nancy bridge, the bootstrapper's `OnError` hook, the `BeforeRequest` auth hook and the 404/405 status handler: for every `/api` request it sets the correlation id, resolves the JWT or API key into the principal, and turns exceptions, unmatched routes and wrong verbs into the API's ProblemDetails body.
- `JsonResponses` now returns `IActionResult`s that write the body with `JsonConventions.Options` and the exact Content-Type the API always sent (`application/json`, `application/problem+json`).
- Removed: the `Nancy` package, `CompositionRootNancyBootstrapper`, `NancyAspNetCoreMiddleware`, `NancyModuleTypeRegistry`, `AmbientServiceProviderAccessor`, `AuthenticationPipelineHook`, `ApiStatusCodeHandler`, `TenantScopedModule` and `AddNancyModule<T>()`. Controllers are discovered by `MapControllers()`; there's nothing to register per resource any more.
- Vue portal OpenAPI generator (`web/tools/openapi`): reads controllers (`[Route]`, `[HttpGet("…")]`, primary-constructor parameters) instead of Nancy `MapGet(...)` calls. Its unit-test fixtures were converted too.

### 1.2 Decisions
- **On `dEngage.Common.AspNetCore` (revised 2026-10-21 at the user's request; first committed on plain ASP.NET Core MVC).** Both HTTP hosts register `AddDengageCommonAspNetCoreModule` and build their pipeline with `UseExternalApiDengageCommonDefaults` (forwarded headers, trace id, log events, controller debug view, Swagger, CORS, controllers; platform internal endpoints off outside Development), through `UseLoyaltyApi`. Three things are deliberately changed on it, because the defaults would break the wire contract or leak secrets:
  - **Its `GeneralExceptionFilter` is removed.** It answers errors with `{ "message": … }`; loyalty's ProblemDetails body (with the `code` the portal and `Api.Client` switch on) is written by `LoyaltyApiMiddleware`, which runs after routing (`preAuthenticationAction`).
  - **Its request/response logger is off** (`useRequestResponseLogger: false`). It logs every header and the first 1 KB of every body: portal JWTs, raw API keys (also in the create-key response), login passwords and customer data would land in Graylog/Seq. The log-event middleware (path, method, IP, timing) stays on.
  - **Its Newtonsoft formatters are never used**: requests are read and responses written by `JsonResponses` with `JsonConventions` (System.Text.Json, decimals as strings), so the bodies are unchanged.
  - Swagger is served only when `SwaggerSettings:IsActive` is `true` (the platform's default is off), for example in a local `appsettings.Development.json`.
  - While wiring this, a bug from the first controller commit was fixed: the middleware's error path called `Response.Clear()`, which also dropped the CORS headers, so a browser couldn't read error bodies cross-origin.
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
| `web/tools/openapi/{csharp-parse,build-spec,build-spec.spec}.ts`, `web/openapi/overrides.ts` | changed | Generator reads controllers; `excludeControllers` |
| `web/src/**`, `web/src/shared/**` | comments | `…Module.cs` → `…Controller.cs`, "Nancy API" → "loyalty API" |
| `CLAUDE.md`, `.claude/rules/*.md` | changed | How to add a route now |

## 2. D9 — `Public.Api` split (done)

### 2.1 What was built
- New deployable **`dEngage.Loyalty.Public.Api`**: the 8 API-key ingestion routes (`order-created`, `order-refunded`, `cash-added`, `cash-spent`, `points-redeem`, `points-transfer`, `reward-purchase`, `generic`) and the event status lookup, on the **same paths** as before (`/api/v1/tenants/{tenantId}/events/...`). A deployment sends client systems to it by host name. Platform hosting, NLog/metrics files, health checks (`/health`, `/health/ready`), Dockerfile, `build.json` image `dengage.loyalty.public.api`, local launch profile `http://localhost:5175` (`https` 7143).
- **API keys only.** JWT registration moved out of `AddLoyaltyApiFramework` into `AddLoyaltyJwtAuthentication`, which only the admin Api calls. Without it the middleware ignores a bearer token, so Public.Api has no JWT signing key and never accepts a portal token. No CORS (server-to-server).
- **Admin Api** keeps the portal's event routes: `simulate` (admin JWT), `types`, and the status lookup (the simulator polls it). The 8 ingestion routes are gone from it; posting to one there now returns 405 `method_not_allowed`.
- **Shared code:** `EventsAppService` and the event validators moved to `Api.Framework/Events/` (registered by `AddLoyaltyApiFramework`); the request/response records moved to `dEngage.Loyalty.Contract` (`PublicApiEvents.cs`) as the public contract, ready for the `Api.Client` package. `HealthChecks` moved to `Api.Framework/Bootstrap/` for both hosts.
- `run.ps1` starts Public.Api too; the portal's opt-in real-backend Playwright spec (`customers.e2e.spec.ts`) post their event to Public.Api (`PUBLIC_API_URL`, default `http://localhost:5175`).

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
| `web/openapi/openapi.json`, `web/src/shared/api/generated/schema.d.ts` | regenerated | Ingestion routes no longer on the admin Api |
| `web/e2e/customers.e2e.spec.ts`, `web/e2e/customers.e2e.spec.ts`, `web/playwright.config.ts` | changed | Event posted to Public.Api |
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

## 4. D8 — messaging (settings step done; library move not done, by decision)

### 4.1 Why the library move was not done (decided 2026-10-21)
D8 recommended moving to `dEngage.Common.MessageBroker` or `dEngage.Messaging.RabbitMq` + `dEngage.Outbox` after a first settings step. On inspection none of them fits:
- **`dEngage.Common.MessageBroker`** is MassTransit 8.1.3: its own message envelope, an exchange per message type, `_error` / `_skipped` queues, retry and defer filters. Loyalty's wire contract (published in `Contract`) is a raw JSON `EventEnvelope` on the direct exchange `loyalty.events`, routed by event type, plus `loyalty.outbound` for the connector. Adopting it changes that contract for every publisher and consumer, and hands over the guarantees loyalty depends on: one message in flight per queue (`BasicQos(0,1)`), tenant lanes with a confirmed forward before the ack (D12), the inbox-based de-duplication and the DLQ topology.
- **`dEngage.Outbox`** depends on `dEngage.DbManager` (not allowed in the core, D13) and on an inapp-specific table and channel enum.
- **`dEngage.Messaging.RabbitMq`** is a single publisher class on RabbitMQ.Client 7; using it means upgrading loyalty from 6.8 for no functional gain.

So the transport stays loyalty's own RabbitMQ.Client code, and only the **configuration** converges with the platform.

### 4.2 What was built
- **`BrokerConnectionSettings`** (`Shared/Messaging/`): the single reader of the RabbitMQ connection for the Api, Public.Api and the Consumer. It reads the platform's `appsettings.messagebroker.json` section `RabbitMQSettings` (`RabbitMQHost`, `RabbitMQPort`, `RabbitMQUsername`, `RabbitMQPassword`, `VirtualHost`, `UseSsl`) when `RabbitMQHost` is set — the file both platform builders already load from `CONFIG_PATH` — and otherwise the legacy `RabbitMq:Host/Port/User/Password` keys, kept for one release. The two are never mixed. `VirtualHost` and `UseSsl` are new to loyalty.
- **Fail fast in the HTTP hosts too.** `RabbitMqOptions` defaulted to `localhost` and `guest/guest`, so the Api started "fine" without broker settings. Now the Api and Public.Api refuse to start, naming the missing keys, as the Consumer already did. `RabbitMqOptions` keeps only `GenericEventTypes`.
- **Templates:** each service has an `appsettings.messagebroker.json` in the platform format with empty values (ops mount the real one); the broker keys were removed from the Api / Public.Api Staging and Production templates. Local `appsettings.Development.json` files keep the legacy keys and keep working.

### 4.3 Verification
| Check | Result |
|---|---|
| `BrokerConnectionSettingsTests` (8) | ✅ platform section read as the platform writes it (string port, vhost, SSL); platform wins and is never mixed with the legacy keys; legacy keys with defaults; nothing configured names both ways; invalid port / SSL reported; Consumer builds its connection factory from the platform section and fails on an incomplete one |
| `ConsumerConfigTests` (existing) | ✅ unchanged behaviour on the legacy keys, same error messages |
| `HostBootSmokeTests` | ✅ new: the Api refuses to start without broker settings |
| Engine / integration tests | ✅ 163/163, 358/358 |
| Local run | ✅ Public.Api with `RabbitMQSettings__*` (platform section): `/health/ready` rabbitmq ok; Consumer on the legacy keys of the dev file: outbox publisher connected |

## 5. Consumer observability, the dEngage way (follow-up, 2026-10-21)

From the comparison with `panel-back-end` / `dengage.schedular.api` and the platform's message consumers (`dEngage.Common.MessageBroker`'s `ConsumerEventLoggerFilter`, consumer debug view, consumer meters), item #2 of the "what's needed to be dEngage-style" list.

### 5.1 What was built
- **`ConsumerTelemetry`** (Consumer, singleton): every delivery from `q.loyalty` or a tenant lane ends in exactly one `DeliveryOutcome` — `Processed`, `Duplicate`, `MovedToLane`, `Failed` (nacked to the DLQ) or `Unreadable` (not an envelope; nacked to the DLQ). It feeds:
  - **App.Metrics** (Prometheus, exported when `appsettings.metrics.json` enables metrics): meter `loyalty_consumer_events` tagged `outcome`, `event_type`, `tenant`; timer `loyalty_consumer_processing` by `event_type` (deliveries that ran the engine); meter `loyalty_outbox_events` tagged `outcome` (`published` by `event_type`, `retry`, `failed`).
  - **Debug view** ("Loyalty consumer" section on the platform's `/debug-view`, when `Debug`/`DebugView` is on): counts per outcome with the platform's total/success/error badges, per-queue last delivery, the last error, outbox counts, isolated tenants, generic event types, and the broker host/port/vhost/source (never the password).
- **One platform log event per delivery** (`ILogEvent`, written as `{@EventData}` like the platform's consumers): `consumerName`, `queueAddress`, `messageName`/`messageType`, `messageId`, `tenant`, `outcome`, `messageRedelivered`, `timeSpentOnConsumer`, and the exception on failure. It replaces the old "✓ event" info line and the separate error line. **Unlike the platform filter, the message body is not logged**: event data can carry customer details, and the inbox keeps the payload.
- `ProcessAsync` now returns the outcome (`Processed` / `Duplicate`); the Consumer registers the platform utilities (`AddCommonUtilities`) for `ILogEvent`.

### 5.2 Verification
| Check | Result |
|---|---|
| `ConsumerTelemetryTests` (4) | ✅ meters by outcome / event type / tenant, timer only for engine runs, per-queue summary and last error, outbox counts, debug-view badges and content without the broker password |
| `ConsumerEvaluatesOnceCr1006Tests` | ✅ extended: first delivery `Processed`, redelivery `Duplicate` |
| Engine / integration tests | ✅ 167/167, 361/361 |
| Local run (real RabbitMQ; one already-processed event re-published, so no data change) | ✅ log event written with `outcome: Duplicate`; `/debug-view` shows the "Loyalty consumer" section with the duplicate, no password |
| Prometheus endpoint locally | ⚠️ `/metrics` and `/metrics-text` return 404 on **both** the Consumer and the admin Api with metrics enabled — the platform host's endpoint exposure, not this change. Meter values are verified in-process (tests). How Prometheus scrapes dEngage services in the cluster should be confirmed with DevOps |

### 5.3 Files
| File | Change | Purpose |
|---|---|---|
| `src/dEngage.Loyalty.Consumer/ConsumerTelemetry.cs` | new | `DeliveryOutcome`, `ConsumerTelemetry`, `ConsumerDebugViewSectionProvider` |
| `src/dEngage.Loyalty.Consumer/EventConsumerWorker.cs` | changed | Outcome per delivery, telemetry, log event |
| `src/dEngage.Loyalty.Consumer/OutboxPublisherWorker.cs` | changed | Outbox meters / summary |
| `src/dEngage.Loyalty.Consumer/Program.cs` | changed | `AddCommonUtilities`, telemetry, debug-view section |
| `src/dEngage.Loyalty.Engine.Tests/ConsumerTelemetryTests.cs`, `ConsumerEvaluatesOnceCr1006Tests.cs` | new / changed | Tests |
| `.claude/rules/backend-consumer.md` | changed | Pipeline step 7 |
