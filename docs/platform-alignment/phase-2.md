# Platform alignment — Phase 2 implementation record

**Status:** in progress on branch `platform-alignment/phase-2` (on top of Phase 1, `c67c497`). Not committed yet.
**Basis:** *Loyalty Platform Alignment Assessment*, Phase 2 = "Product-ready (standalone)": D4 `loyalty_config`, D12 tenant fairness, D18, publish `Contract`. Approved for implementation phase by phase.
**Order agreed:** D12 first, as a small change; the other Phase 2 items follow.
**Scope change 2026-10-20:** D15 webhooks were **dropped**. Clients are notified only the dEngage way (the Phase 4 connector forwards loyalty events into dEngage journeys and campaigns). A loyalty-only client therefore gets no push of loyalty events and reads current state through the Public API. `ISecretProtector` (D3), first needed for webhook signing secrets, moves to D4 (secret `loyalty_config` rows) and Phase 4 (dEngage integration credentials).

## Status

| Item | Status |
|---|---|
| D12 tenant fairness (isolated tenant lanes) | ✅ Done and tested (§1). Committed `84427e7` |
| D4 `loyalty_config` table + reader + `ISecretProtector` for secret rows | ✅ Done and tested (§2). Committed `798356f` |
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
- **Setting the list in `loyalty_config`.** D4's table doesn't exist yet; the list is in the Consumer's config for now and can move into `loyalty_config` with D4.
- **Automatic isolation** (detecting noisy tenants). Out of scope for a small change.

### 1.5 Relation to the throughput change
`docs/scope-changes/2026-10-13-event-throughput-partitioning-and-bulk.md` (revision 2) records D12 (§2.7, §4 row A′) and retires the lanes at its Phase 2 switch-over (§11, decision Q12).

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
| `docs/scope-changes/2026-10-13-event-throughput-partitioning-and-bulk.md` | changed | Revision 2 (see its §16–§17) |
| `docs/platform-alignment/phase-2.md` | new | This record |

The assessment itself (*Loyalty Platform Alignment Assessment*, kept outside the repo) was updated on 2026-10-20 for the D15 decision: D3, D15, D18, §5.2–5.3, §6.2, §7.1–7.7, §8 and §9.

## 2. D4 — runtime settings table `loyalty_config` (done)

### 2.1 What was built
- **Table `loyalty_config`** (migration `20261020135214_LoyaltyConfigAlignD4`): `module_name`, `parameter_name`, optional `tenant_id`, `value` / `value_json` (jsonb), `is_secret`, `description`, `updated_at`, `updated_by`. Same shape as the platform's `sys.global_config`, plus the tenant override.
- **Uniqueness:** one global row and at most one row per tenant for each setting, enforced by two partial unique indexes (`ux_loyalty_config_global`, `ux_loyalty_config_tenant`). This works on PostgreSQL 14, which the dEngage platform runs; a single index would need PostgreSQL 15's `NULLS NOT DISTINCT`.
- **Reader** `ILoyaltyConfig.GetAsync(module, parameter, default, tenantId, ct)` (`Schema/LoyaltyConfig.cs`, same pattern as `TenantSlugResolver`): tenant row → global row → the caller's default. Loads a whole module and caches it for 1 minute (`LoyaltyConfigCache`, singleton). A value that can't be read as the requested type throws `loyalty_config_invalid` rather than silently using the default.
- **`ISecretProtector`** (`Shared/Security`, D3): rows with `is_secret` hold an `ENC(...)` value and are decrypted on load (same key and format as config values; key from `LOYALTY_MASTER_KEY_FILE` / `LOYALTY_MASTER_KEY`, only needed once a secret row exists). A row marked secret but stored in plain text is refused (`secret_not_encrypted`).
- **Names:** `Shared/Constants/LoyaltyConfigKeys.cs`.

### 2.2 Settings now read from the table
Every call site keeps today's value as its default, so **behaviour is unchanged until a row is added**.

| Module / parameter | Type | Default | Tenant override | Read in |
|---|---|---|---|---|
| `loyalty.api` / `RateLimit.EventIngestionPerMinute` | int | appsettings `RateLimit:EventIngestionPerMinutePerTenant` (600 / 300 / 120) | yes | `EventsAppService` |
| `loyalty.api` / `Pii.ExtraMaskedFields` | string[] | none | yes | `CustomersAppService` → `CustomerPayloadMasker` (additive: can't un-mask built-in fields) |
| `loyalty.api` / `Customer360.MessageWindowBeforeMinutes`, `…AfterMinutes` | int | 1 / 60 | yes | `CustomersAppService` |
| `loyalty.engine` / `RuleCache.TtlMinutes`, `CampaignConfigCache.TtlMinutes` | int | 60 / 60 | no | `RuleCacheService`, `CampaignConfigCacheService` |
| `loyalty.jobs` / `Outbox.BatchSize`, `Outbox.MaxAttempts`, `Outbox.BackoffMinutes` | int, int, int[] | 100, 8, [1,5,30,120,720] | no | `OutboxPublisherWorker` (read per batch) |
| `loyalty.jobs` / `DelayedPosting.BatchSize` | int | 500 | no | `DelayedPostingPromotionJob` |
| `loyalty.jobs` / `EventLogRetention.BatchSize` | int | 10 000 | no | `EventLogRetentionJob` |

Example (raise one tenant's ingestion limit):
```sql
INSERT INTO loyalty_config (module_name, parameter_name, tenant_id, value, description, updated_by)
SELECT 'loyalty.api', 'RateLimit.EventIngestionPerMinute', id, '2000', 'Black Friday', 'ops@dengage.com'
FROM tenants WHERE slug = 'starbucks';
```

### 2.3 Deliberately not moved (yet), and why
| Setting (assessment §5.2) | Why not now |
|---|---|
| Job run times, job intervals, RuleSync intervals | The event-throughput change owns the background jobs (single-runner redesign, its Phase 1 / Q10); moving their schedules now would conflict with that rewrite |
| Event retention (12 months), max streak periods, max condition window | They are tied together (the retention must cover the longest streak window); changing one safely needs cross-checks against existing streak and rule configurations, i.e. a product decision |
| Supported currencies / default currency | Validated statically in several API validators and listed in the portals; a cross-stack change (backend, both portals, `LoyaltySaaSApi.md`) |
| Generic event types, JWT expiry, CORS | Startup or topology values (queue bindings, token options); they stay in appsettings |
| Cross-pod cache invalidation | There is no application write path (rows are changed with SQL, like `sys.global_config`), so the 1-minute cache is the propagation mechanism. Add a broadcast when an admin screen or API starts writing rows |
| Admin API / portal screen for settings | New capability; not in the assessment's D4 |

### 2.4 Deployment note
The Api reads `loyalty_config` on every event ingestion. **Apply the migration before deploying this code** (`dotnet ef database update -p src/dEngage.Loyalty.Schema`, or `scripts/loyalty_schema.sql` for a new database), otherwise ingestion fails on the missing table. This includes local development databases.

### 2.5 Verification
| Check | Result |
|---|---|
| `dotnet build dEngage.Loyalty.sln` | ✅ 0 errors, 0 warnings |
| `dotnet test src/dEngage.Loyalty.Engine.Tests` | ✅ 146/146 |
| `dotnet test src/dEngage.Loyalty.IntegrationTests` (Docker) | ✅ 336/336 (8 new) |
| New: `LoyaltyConfigE2ETests` (real Postgres via the migration) | tenant → global → default precedence; typed values (int, bool, string, string[], int[]); unreadable value throws; secret row decrypted, plaintext secret refused; change seen only after the 1-minute TTL; both uniqueness rules |
| New: `LoyaltyConfigApiTests` | a tenant row sets that tenant's ingestion limit (42) while another tenant keeps the default (600); a tenant's extra masked field is masked for it only, built-in masking unchanged |
| Migration review | Creates only `loyalty_config` and its two indexes; `Down` drops only that table; the model snapshot was in sync before |
| `scripts/loyalty_schema.sql` regenerated (`dotnet ef migrations script --idempotent`) | ✅ Applied to an empty PostgreSQL 16 container (48 migrations, table + indexes present) and re-applied idempotently. Besides the new table, EF 9 now wraps the script in one transaction and stamps 9.0.13 in the history inserts |
| Not covered by a dedicated test | The outbox, cache-TTL and job batch-size wiring (they read the defaults in the existing tests, which all pass) |

### 2.6 Files
| File | Change | Why |
|---|---|---|
| `src/dEngage.Loyalty.Shared/Constants/LoyaltyConfigKeys.cs` | new | Module and parameter names |
| `src/dEngage.Loyalty.Shared/Security/ISecretProtector.cs` | new | `ISecretProtector` + `SecretCryptoProtector` (D3) |
| `src/dEngage.Loyalty.Schema/Entities/LoyaltyConfigEntry.cs` | new | Entity |
| `src/dEngage.Loyalty.Schema/Configurations/LoyaltyConfigEntryConfiguration.cs` | new | Mapping, partial unique indexes |
| `src/dEngage.Loyalty.Schema/LoyaltyConfig.cs` | new | `ILoyaltyConfig`, `LoyaltyConfigCache`, `LoyaltyConfigReader` |
| `src/dEngage.Loyalty.Schema/LoyaltyDbContext.cs` | changed | `LoyaltyConfig` set |
| `src/dEngage.Loyalty.Schema/Migrations/20261020135214_LoyaltyConfigAlignD4.cs` (+ `.Designer.cs`) | new (generated) | Migration |
| `src/dEngage.Loyalty.Schema/Migrations/LoyaltyDbContextModelSnapshot.cs` | changed (generated) | New table; EF product version 9.0.13 |
| `src/dEngage.Loyalty.Api.Framework/Bootstrap/ServiceCollectionExtensions.cs` | changed | Register cache, protector, reader (Api) |
| `src/dEngage.Loyalty.Consumer/Program.cs` | changed | Same registrations (Consumer) |
| `src/dEngage.Loyalty.Api/Events/EventsAppService.cs` | changed | Per-tenant ingestion limit |
| `src/dEngage.Loyalty.Api/Customers/CustomersAppService.cs` | changed | Extra masked fields; message window |
| `src/dEngage.Loyalty.Api/Customers/CustomerPayloadMasker.cs` | changed | Additive extra field names |
| `src/dEngage.Loyalty.RuleEngine/Cache/RuleCacheService.cs` | changed | Cache TTL |
| `src/dEngage.Loyalty.RuleEngine/Campaigns/CampaignConfigCacheService.cs` | changed | Cache TTL |
| `src/dEngage.Loyalty.RuleEngine/Processing/DelayedPostingPromotionJob.cs` | changed | Batch size |
| `src/dEngage.Loyalty.Ledger/EventLogRetentionJob.cs` | changed | Batch size |
| `src/dEngage.Loyalty.Consumer/OutboxPublisherWorker.cs` | changed | Batch size, attempts, backoff |
| `src/dEngage.Loyalty.IntegrationTests/Fixtures/DefaultLoyaltyConfig.cs` | new | Test double: every setting at its default |
| `src/dEngage.Loyalty.IntegrationTests/Fixtures/FakeRateLimiter.cs` | changed | Records the limit it was asked to apply |
| `src/dEngage.Loyalty.IntegrationTests/E2E/LoyaltyConfigE2ETests.cs` | new | Reader and table tests (real Postgres) |
| `src/dEngage.Loyalty.IntegrationTests/Api/LoyaltyConfigApiTests.cs` | new | Wiring tests through the Api |
| `src/dEngage.Loyalty.IntegrationTests/E2E/RefundPathsCr1006E2ETests.cs`, `Engine/ExpiryOverridePostingCr1006Tests.cs`, `Engine/RuleConfigurationTests.cs`, `Engine/SpendDecimalsCl13Tests.cs` | changed | Pass `DefaultLoyaltyConfig` to the constructors that gained a parameter |
| `scripts/loyalty_schema.sql` | regenerated | New installs get `loyalty_config` |
| `scripts/loyalty_schema_reference.md` | changed | `loyalty_config` section (Turkish), latest migration id |
| `docs/SCOPE_BASELINE.md` | changed | Schema and Shared rows |
| `.claude/rules/backend.md` | changed | How to add a runtime setting |
| `docs/platform-alignment/phase-2.md` | changed | This section |

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
