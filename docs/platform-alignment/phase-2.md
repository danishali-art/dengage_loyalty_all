# Platform alignment — Phase 2 implementation record

**Status:** in progress on branch `platform-alignment/phase-2` (on top of Phase 1, `c67c497`). Not committed yet.
**Basis:** *Loyalty Platform Alignment Assessment*, Phase 2 = "Product-ready (standalone)": D4 `loyalty_config`, D12 tenant fairness, D18, publish `Contract`. Approved for implementation phase by phase.
**Order agreed:** D12 first, as a small change; the other Phase 2 items follow.
**Scope change 2026-10-20:** D15 webhooks were **dropped**. Clients are notified only the dEngage way (the Phase 4 connector forwards loyalty events into dEngage journeys and campaigns). A loyalty-only client therefore gets no push of loyalty events and reads current state through the Public API. `ISecretProtector` (D3), first needed for webhook signing secrets, moves to D4 (secret `loyalty_config` rows) and Phase 4 (dEngage integration credentials).

## Status

| Item | Status |
|---|---|
| D12 tenant fairness (isolated tenant lanes) | ✅ Done and tested (§1). Committed `84427e7` |
| D4 settings tables (`loyalty_config` global, `loyalty_tenant_config` per tenant) + `ISecretProtector` | ✅ Done and tested (§2). First table committed `798356f`; the rest not committed yet |
| Publish `dEngage.Loyalty.Contract` (event envelope, event types and catalog, outbound event types, outcome reasons, messaging topology) | ✅ Done and tested (§3) |
| D15 webhooks | ❌ Dropped (2026-10-20) |
| D18 loyalty emits events, doesn't send email/SMS | ✅ Nothing to build. With D15 dropped, notifications go through dEngage only |
| NLog + metrics | ✅ Done in Phase 1 |

## 1. D12 — tenant fairness (done)

### 1.1 Problem
`EventConsumerWorker` processes `q.loyalty` one event at a time (`BasicQos(0, 1)`) for every tenant. A burst from one tenant (a backfill, a campaign spike) delays every other tenant's events behind it.

### 1.2 What was built: isolated tenant lanes
- `RabbitMq:IsolatedTenants` (Consumer config) lists tenant slugs. Validated at startup: only `^[a-z0-9_]{1,50}$`, the same rule as tenant creation, because the slug becomes part of a queue name.
- For each listed tenant the Consumer declares `q.loyalty.tenant.<slug>` (same dead-lettering as `q.loyalty` → `q.loyalty.dlq`) and runs a **sequential** worker on its own channel (`TenantLanes`).
- The `q.loyalty` worker doesn't process a listed tenant's events. It **moves** them to the tenant's queue: publish with a broker confirm, then ack the original. That is a broker round-trip instead of a full rule-engine and ledger run, so other tenants are no longer stuck behind that tenant's processing.
- The processing pipeline (`ProcessAsync`), ack/nack/dead-letter behaviour, the Api and the publisher are unchanged.

### 1.3 Why it's safe
| Concern | Answer |
|---|---|
| Per-customer order | All of a tenant's events still pass through exactly one FIFO queue at a time: arrival order on `q.loyalty` is kept when moving, and the lane is sequential |
| Lanes of different tenants in parallel | Every piece of processing state is keyed by tenant (inbox `(tenant, event_id)`, accounts, ledger, limit counters, streaks, Redis keys). The singletons on the event path (`TenantSlugCache`, `RuleSyncService`, rule-type handlers) are stateless or thread-safe. The races R1–R6 in the throughput analysis concern one tenant's events, which stay serial |
| Crash between the confirmed copy and the ack | The event is delivered twice; the second copy is skipped by the `EventInbox` dedupe (shown in the end-to-end check below) |
| Lane queue missing | Lanes are declared and consuming before `q.loyalty` starts |
| Removing a tenant from the list | Its lane queue must be empty first, or its leftover events would run after newer ones. Documented in `TenantLanes` and `.claude/rules/backend-consumer.md` |

### 1.4 Not done (and why)
- **Per-tenant concurrency cap.** With one event in flight per queue, each lane is already capped at one; a separate cap only makes sense once events run in parallel within a tenant, which is the throughput change's partitioning (its Phase 2).
- **Setting the list in `loyalty_config`.** Decided against, now that D4 exists: the list stays in the Consumer's config (`RabbitMq:IsolatedTenants`). The lane queues are declared and consumed when the Consumer starts, so a table value would only take effect after a restart anyway, and a row changed by SQL would look live without being so. Changing the list remains a deployment change, with the empty-lane rule above.
- **Automatic isolation** (detecting noisy tenants). Out of scope for a small change.

### 1.5 Relation to the throughput change
The event throughput / bulk scope change (per-customer partitioning) is planned to replace the lanes at its Phase 2 switch-over. That change is not implemented yet, and its document was withdrawn on 2026-10-21 until it is, so the lanes are the current isolation mechanism and this record is the reference for them.

### 1.6 Verification
| Check | Result |
|---|---|
| `dotnet build dEngage.Loyalty.sln` | ✅ 0 errors |
| `dotnet test src/dEngage.Loyalty.Engine.Tests` | ✅ 146/146 (4 new: isolated-tenant config) |
| `dotnet test src/dEngage.Loyalty.IntegrationTests` (Docker) | ✅ 328/328 (3 new, against a real RabbitMQ: order, body, headers kept when moving; a lane dead-letters into `q.loyalty.dlq`; re-declaring is harmless) |
| End to end on the local stack | ✅ Consumer started with `RabbitMq__IsolatedTenants__0=fintech`; an already-processed fintech `order.created` was re-published to `loyalty.events`. Log: "moved … to the lane of tenant fintech", then 17 ms later "skipping duplicate" from the lane worker. No data changed; the empty test lane queue was deleted afterwards |

### 1.7 Files
| File | Change | Why |
|---|---|---|
| `src/dEngage.Loyalty.Consumer/TenantLanes.cs` | new | Lane queue declaration and confirmed forwarding |
| `src/dEngage.Loyalty.Consumer/EventConsumerWorker.cs` | changed | Lanes set up before `q.loyalty`; shared `HandleDeliveryAsync` for both kinds of queue; `ProcessAsync` unchanged |
| `src/dEngage.Loyalty.Consumer/ConsumerConfig.cs` | changed | `IsolatedTenants`, slug validation, wording of the startup error |
| `src/dEngage.Loyalty.Consumer/appsettings.json` | changed | `RabbitMq:IsolatedTenants: []` so the setting is visible |
| `src/dEngage.Loyalty.Shared/Constants/MessagingTopology.cs` | changed | `TenantLaneQueuePrefix`, `TenantLaneQueue(slug)` |
| `src/dEngage.Loyalty.Engine.Tests/ConsumerConfigTests.cs` | changed | Isolated-tenant config tests |
| `src/dEngage.Loyalty.IntegrationTests/E2E/TenantLanesE2ETests.cs` | new | Real-RabbitMQ tests for the lane mechanics |
| `.claude/rules/backend-consumer.md` | changed | Host description; tenant-lanes rules |
| `docs/SCOPE_BASELINE.md` | changed | Consumer row mentions the optional per-tenant queues |
| Event throughput / bulk scope-change document | changed | Revision 2 recorded D12; the document was withdrawn on 2026-10-21 (change not implemented yet) |
| `docs/platform-alignment/phase-2.md` | new | This record |

The assessment itself (*Loyalty Platform Alignment Assessment*, kept outside the repo) was updated on 2026-10-20 for the D15 decision: D3, D15, D18, §5.2–5.3, §6.2, §7.1–7.7, §8 and §9.

## 2. D4 — runtime settings: `loyalty_config` and `loyalty_tenant_config` (done)

**Decisions 2026-10-20:**
- The tables are the **only** source: no defaults in code or appsettings (`RateLimitOptions` and the `RateLimit` appsettings sections were removed). A missing setting fails with `loyalty_config_missing`.
- Two kinds of setting, never mixed (CHECK constraints in both tables), like the platform's `sys.global_config` next to `sys.config_account_*`:
  - **global** settings: one row in `loyalty_config`;
  - **tenant** settings: one row **per tenant** in `loyalty_tenant_config`, with **no global value to fall back to**.

### 2.1 What was built
- **`loyalty_config`** (global): `module_name`, `parameter_name`, `value` / `value_json` (jsonb), `is_secret`, `description`, `updated_at`, `updated_by`; `ux_loyalty_config_key` UNIQUE; `ck_loyalty_config_global_settings` keeps tenant settings out.
- **`loyalty_tenant_config`** (per tenant): the same columns plus `tenant_id` (FK to `tenants`, cascade on delete); `ux_loyalty_tenant_config_key (tenant_id, module_name, parameter_name)` UNIQUE; `ck_loyalty_tenant_config_tenant_settings` allows only `LoyaltyConfigKeys.TenantSettings`. The CHECK expressions are generated from that list, so changing it shows up as a model change.
- **Migrations** (in order):
  - `20261020135214_LoyaltyConfigAlignD4` (committed `798356f`): created `loyalty_config`, then with a `tenant_id` column;
  - `20261020142338_LoyaltyConfigDefaultsAlignD4`: seeded the global rows;
  - `20261020143545_LoyaltyConfigGlobalOnlyAlignD4`: removed `tenant_id` from `loyalty_config`; seeded the rate limit globally;
  - `20261020150844_LoyaltyTenantConfigAlignD4`: created `loyalty_tenant_config`, gave **every existing tenant** its rows (carrying over the environment's current global value), removed the two global rows, then added the global CHECK. `Down` restores the global rows exactly as first seeded, so rolling back further removes them cleanly.
- **Reader** (`Schema/LoyaltyConfig.cs`): `GetAsync<T>(module, parameter, ct)` for global settings; `GetForTenantAsync<T>(module, parameter, tenantId, ct)` for tenant settings — the tenant's row only. Each refuses the other kind. One-minute cache per module (per tenant + module for tenant settings). Missing or blank → `loyalty_config_missing`; unreadable → `loyalty_config_invalid`.
- **Tenant provisioning:** `PlatformAppService.CreateTenantAsync` adds the new tenant's rows (`LoyaltyConfigDefaults.NewTenant`: rate limit 120, extra masked fields `[]`) in the same transaction as the tenant, so a tenant never exists without them. Tenants are only created through this API (the seed scripts and `provision_tenant.sql` don't insert tenants).
- **`LoyaltyConfigDefaults`** (`Schema/LoyaltyConfigDefaults.cs`): `Global` (9 settings, seeded by migration; `EnsureSeededAsync` for databases built without migrations) and `NewTenant` (2 settings, the provisioning template — never read as a fallback). `LoyaltyConfigE2ETests` fails if the migrated global rows differ from `Global` or a new tenant's rows differ from `NewTenant`.
- **`ISecretProtector`** (`Shared/Security`, D3): `is_secret` rows in either table hold `ENC(...)` values and are decrypted on load; a plaintext "secret" is refused.

### 2.2 Settings
| Kind | Module / parameter | Type | Starting value | Read in |
|---|---|---|---|---|
| **Tenant** | `loyalty.api` / `RateLimit.EventIngestionPerMinute` | int | **120** per tenant (the previous production value; appsettings had 600 local / 300 staging / 120 production) | `EventsAppService` |
| **Tenant** | `loyalty.api` / `Pii.ExtraMaskedFields` | string[] | `[]` | `CustomersAppService` → `CustomerPayloadMasker` (additive: can't un-mask built-in fields) |
| Global | `loyalty.api` / `Customer360.MessageWindowBeforeMinutes`, `…AfterMinutes` | int | 1 / 60 | `CustomersAppService` |
| Global | `loyalty.engine` / `RuleCache.TtlMinutes`, `CampaignConfigCache.TtlMinutes` | int | 60 / 60 | `RuleCacheService`, `CampaignConfigCacheService` |
| Global | `loyalty.jobs` / `Outbox.BatchSize`, `Outbox.MaxAttempts`, `Outbox.BackoffMinutes` | int, int, int[] | 100, 8, [1,5,30,120,720] | `OutboxPublisherWorker` (read per batch) |
| Global | `loyalty.jobs` / `DelayedPosting.BatchSize` | int | 500 | `DelayedPostingPromotionJob` |
| Global | `loyalty.jobs` / `EventLogRetention.BatchSize` | int | 10 000 | `EventLogRetentionJob` |

Every call site validates its value (positive sizes and TTLs, non-negative windows) and fails with `loyalty_config_invalid` otherwise.

**Behaviour change:** local and staging tenants now start at **120** events per minute (was 600 / 300 from appsettings). Raise it per tenant:
```sql
UPDATE loyalty_tenant_config SET value = '600', updated_at = now(), updated_by = 'you@dengage.com'
WHERE parameter_name = 'RateLimit.EventIngestionPerMinute'
  AND tenant_id = (SELECT id FROM tenants WHERE slug = 'fintech');
```

### 2.3 Deliberately not moved (yet), and why
| Setting (assessment §5.2) | Why not now |
|---|---|
| Job run times, job intervals, RuleSync intervals | The event-throughput change owns the background jobs (single-runner redesign, its Phase 1 / Q10); moving their schedules now would conflict with that rewrite |
| Event retention (12 months), max streak periods, max condition window | They are tied together (the retention must cover the longest streak window); changing one safely needs cross-checks against existing streak and rule configurations, i.e. a product decision |
| Supported currencies / default currency | Validated statically in several API validators and listed in the portals; a cross-stack change (backend, both portals, `LoyaltySaaSApi.md`) |
| Generic event types, isolated tenants (D12), JWT expiry, CORS | Startup or topology values (queue bindings, token options); they stay in appsettings |
| Cross-pod cache invalidation | There is no application write path (rows are changed with SQL, like `sys.global_config`), so the 1-minute cache is the propagation mechanism |
| Admin API / portal screen for settings | New capability; not in the assessment's D4 |

### 2.4 Deployment note
The Api reads these settings on every event ingestion and has no fallback. **Apply all four migrations before deploying this code** (`dotnet ef database update -p src/dEngage.Loyalty.Schema`, or `scripts/loyalty_schema.sql` for a new database); the last one backfills every existing tenant. Applied to the local development database on 2026-10-20: 9 global rows; 4 tenants × 2 tenant rows; no tenant setting left in `loyalty_config`.

### 2.5 Verification
| Check | Result |
|---|---|
| `dotnet build dEngage.Loyalty.sln`; `dotnet ef migrations has-pending-model-changes` | ✅ 0 errors; model and migrations in sync |
| `dotnet test src/dEngage.Loyalty.Engine.Tests` | ✅ 146/146 |
| `dotnet test src/dEngage.Loyalty.IntegrationTests` (Docker) | ✅ 345/345 |
| `LoyaltyConfigE2ETests` (real Postgres via the migrations, 14 tests) | global value used, missing/blank fails; typed values; unreadable fails; secrets; 1-minute TTL; one global row per setting; migrated global rows equal `Global`; a tenant setting comes only from that tenant's row (another tenant without a row fails — no global fallback); each kind can only be read its own way; CHECK constraints reject misplaced rows and duplicates; deleting a tenant deletes its settings; **creating a tenant through `PlatformAppService` writes `NewTenant`** (verified to fail when that line is removed); existing tenants get the previous global value on migration; rollback keeps changed values |
| `LoyaltyConfigApiTests` (3 tests) | each tenant gets its own ingestion limit (42 vs 120); extra masked fields apply to that tenant only; no global rows for tenant settings |
| `scripts/loyalty_schema.sql` regenerated | ✅ only additions; applied and re-applied on an empty PostgreSQL 16 (9 global rows; a global rate-limit row is rejected by `ck_loyalty_config_global_settings`) |

### 2.6 Files
| File | Change | Why |
|---|---|---|
| `src/dEngage.Loyalty.Shared/Constants/LoyaltyConfigKeys.cs` | new | Names; `TenantSettings` list |
| `src/dEngage.Loyalty.Shared/Security/ISecretProtector.cs` | new | `ISecretProtector` + `SecretCryptoProtector` (D3) |
| `src/dEngage.Loyalty.Schema/Entities/LoyaltyConfigEntry.cs`, `LoyaltyTenantConfigEntry.cs` | new | Entities |
| `src/dEngage.Loyalty.Schema/Configurations/LoyaltyConfigEntryConfiguration.cs`, `LoyaltyTenantConfigEntryConfiguration.cs` | new | Mappings, CHECK constraints |
| `src/dEngage.Loyalty.Schema/LoyaltyConfig.cs` | new | `ILoyaltyConfig` (`GetAsync`, `GetForTenantAsync`), `LoyaltyConfigCache`, `LoyaltyConfigReader` |
| `src/dEngage.Loyalty.Schema/LoyaltyConfigDefaults.cs` | new | `Global`, `NewTenant`, `EnsureSeededAsync`, `NewTenantRows` |
| `src/dEngage.Loyalty.Schema/LoyaltyDbContext.cs` | changed | Two sets |
| `src/dEngage.Loyalty.Schema/Migrations/20261020135214_…`, `20261020142338_…`, `20261020143545_…`, `20261020150844_…` (+ `.Designer.cs`), `LoyaltyDbContextModelSnapshot.cs` | new / changed (generated; seed, backfill and ordering SQL added by hand) | See §2.1 |
| `src/dEngage.Loyalty.Api/Platform/PlatformAppService.cs` | changed | New tenants get their settings |
| `src/dEngage.Loyalty.Api.Framework/Options/RateLimitOptions.cs` | deleted | The limit lives in `loyalty_tenant_config` |
| `src/dEngage.Loyalty.Api/appsettings.json`, `appsettings.Staging.json`, `appsettings.Production.json` | changed | `RateLimit` sections removed |
| `src/dEngage.Loyalty.Api.Framework/Bootstrap/ServiceCollectionExtensions.cs`, `src/dEngage.Loyalty.Consumer/Program.cs` | changed | Register cache, protector, reader |
| `src/dEngage.Loyalty.Api/Events/EventsAppService.cs` | changed | Tenant's ingestion limit |
| `src/dEngage.Loyalty.Api/Customers/CustomersAppService.cs`, `CustomerPayloadMasker.cs` | changed | Tenant's extra masked fields; message window |
| `src/dEngage.Loyalty.RuleEngine/Cache/RuleCacheService.cs`, `Campaigns/CampaignConfigCacheService.cs`, `Processing/DelayedPostingPromotionJob.cs`, `src/dEngage.Loyalty.Ledger/EventLogRetentionJob.cs`, `src/dEngage.Loyalty.Consumer/OutboxPublisherWorker.cs` | changed | Global settings |
| `src/dEngage.Loyalty.IntegrationTests/Fixtures/TenantSettingsSeedingInterceptor.cs` | new | Test hosts give directly inserted tenants their rows |
| `src/dEngage.Loyalty.IntegrationTests/Fixtures/CustomWebApplicationFactory.cs`, `PostgresWebApplicationFactory.cs` | changed | Seed global rows; register the interceptor |
| `src/dEngage.Loyalty.IntegrationTests/Fixtures/DefaultLoyaltyConfig.cs`, `FakeRateLimiter.cs` | new / changed | Seeded values for hand-built services; records the applied limit |
| `src/dEngage.Loyalty.IntegrationTests/E2E/LoyaltyConfigE2ETests.cs`, `Api/LoyaltyConfigApiTests.cs` | new | Tables, reader, provisioning, migration and wiring tests |
| `src/dEngage.Loyalty.IntegrationTests/E2E/RefundPathsCr1006E2ETests.cs`, `Engine/ExpiryOverridePostingCr1006Tests.cs`, `Engine/RuleConfigurationTests.cs`, `Engine/SpendDecimalsCl13Tests.cs` | changed | Pass `DefaultLoyaltyConfig` |
| `scripts/loyalty_schema.sql`, `scripts/loyalty_schema_reference.md` | changed | Regenerated script; Turkish reference (both tables) |
| `docs/SCOPE_BASELINE.md`, `.claude/rules/backend.md`, `docs/platform-alignment/phase-2.md` | changed | Baseline, rules, this record |

## 3. `dEngage.Loyalty.Contract` package (done)

### 3.1 What was built
- New project `src/dEngage.Loyalty.Contract`, **netstandard2.0** (like the platform's other Contract packages, so .NET 6/8/9 services can reference it), no project references, one package dependency (`System.Text.Json` 9.0.9). Namespace `dEngage.Loyalty.Contract`.
- Moved into it with `git mv` (history kept), unchanged apart from the namespace: `EventEnvelope`, `EventTypes` (with the event catalog: `EventDefinition`, `EventFieldSchema`, `EventCategory`, `EventSource`, `EventCardinality`, `EventPeriod`, `EventFieldKind`), `OutboundEventTypes`, `OutcomeReasons`, `MessagingTopology`.
- `IsExternalInit.cs` (internal polyfill) so records and `init` setters compile on netstandard2.0.
- `dEngage.Loyalty.Shared` now references `Contract` (every project gets it through Shared); `GenericEventValidator` stays in Shared (engine-side validation, not contract).
- 81 files: `using dEngage.Loyalty.Shared.Events;` replaced by `using dEngage.Loyalty.Contract;` where needed; 7 fully qualified references updated. No logic changes.
- `build.json` publishes the package (`NugetPackageSettingsList`); `<Version>1.0.0</Version>` in the csproj, as the platform's Contract projects do.
- Vue 3 portal: the OpenAPI generator also scans `src/dEngage.Loyalty.Contract` (`tools/openapi/build-spec.ts`), because API DTOs use `EventCategory` and `EventFieldKind`; `openapi/overrides.ts` source notes point at the new file.

### 3.2 Not included (and why)
| Item | Why not now |
|---|---|
| Public API request/response DTOs | They live in the Api project next to the Nancy modules; moving them belongs with the Public.Api split and `Api.Client` (Phase 3, D7/D9) |
| Typed outbound payload classes | Outbound payloads are built as anonymous objects in the engine today; defining typed classes is a contract design task for the dEngage connector (Phase 4) |
| Moving `Shared/Constants/*` | Statuses, ledger reasons and rule types are DB/engine constants, not what other services consume |

### 3.3 Verification
| Check | Result |
|---|---|
| `dotnet build dEngage.Loyalty.sln` | ✅ 0 errors; only the pre-existing CS1998 warning, plus NU1801 restore warnings while the private feed doesn't resolve on this machine |
| `dotnet test src/dEngage.Loyalty.Engine.Tests` | ✅ 146/146 (includes the D13 boundary test, which now also covers `Contract`) |
| `dotnet test src/dEngage.Loyalty.IntegrationTests` (Docker) | ✅ 336/336 |
| `dotnet pack src/dEngage.Loyalty.Contract -c Release` | ✅ `dEngage.Loyalty.Contract.1.0.0.nupkg`: `lib/netstandard2.0/dEngage.Loyalty.Contract.dll`, one dependency (`System.Text.Json` 9.0.9) |
| Portal: `npm run api:spec`, `npm run api:types`, `npm run api:check` (webVue3) | ✅ identical spec (55 paths, 112 schemas), no content change in the generated files |
| Portal: `npx vitest run` (webVue3) | ✅ 41 files, 228 tests (a run started in parallel with the Docker test suite reported timeouts; run on its own it is clean) |
| Shared pipeline publishing the package | ❌ not run (no Bitbucket access here) |

Windows note: `npm run api:check` reports `openapi.json` / `schema.d.ts` as out of date right after a git checkout on Windows, because git checks them out with CRLF and the generator writes LF. Running `npm run api:spec && npm run api:types` fixes it without any content change.

### 3.4 Files
| File | Change | Why |
|---|---|---|
| `src/dEngage.Loyalty.Contract/dEngage.Loyalty.Contract.csproj` | new | netstandard2.0 package project |
| `src/dEngage.Loyalty.Contract/IsExternalInit.cs` | new | Polyfill for records / `init` |
| `src/dEngage.Loyalty.Contract/EventEnvelope.cs`, `EventTypes.cs`, `OutboundEventTypes.cs`, `OutcomeReasons.cs`, `MessagingTopology.cs` | moved from `Shared/Events` and `Shared/Constants` | Namespace `dEngage.Loyalty.Contract` |
| `src/dEngage.Loyalty.Shared/dEngage.Loyalty.Shared.csproj` | changed | References Contract |
| `dEngage.Loyalty.sln` | changed | New project |
| 81 `.cs` files across `src/` | changed | `using` / qualified names only |
| `build.json` | changed | Publish `dEngage.Loyalty.Contract` |
| `webVue3/tools/openapi/build-spec.ts`, `webVue3/openapi/overrides.ts` | changed | Generator scans Contract; source notes |
| `.claude/rules/backend.md`, `backend-schema-shared.md`, `backend-api.md`, `CLAUDE.md`, `docs/SCOPE_BASELINE.md` | changed | Project graph, Contract rules, new paths |
| `docs/platform-alignment/phase-2.md` | changed | This section |

## 4. Phase 2 summary
All Phase 2 items are done: D12 (tenant lanes), D4 (`loyalty_config` + `ISecretProtector`) and the `Contract` package. D15 webhooks were dropped (2026-10-20); D18 needs no build. Logging and metrics came with Phase 1.
