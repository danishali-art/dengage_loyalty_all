# Platform alignment — Phase 2 implementation record

**Status:** in progress on branch `platform-alignment/phase-2` (on top of Phase 1, `c67c497`). Not committed yet.
**Basis:** *Loyalty Platform Alignment Assessment*, Phase 2 = "Product-ready (standalone)": D4 `loyalty_config`, D12 tenant fairness, D18, publish `Contract`. Approved for implementation phase by phase.
**Order agreed:** D12 first, as a small change; the other Phase 2 items follow.
**Scope change 2026-10-20:** D15 webhooks were **dropped**. Clients are notified only the dEngage way (the Phase 4 connector forwards loyalty events into dEngage journeys and campaigns). A loyalty-only client therefore gets no push of loyalty events and reads current state through the Public API. `ISecretProtector` (D3), first needed for webhook signing secrets, moves to D4 (secret `loyalty_config` rows) and Phase 4 (dEngage integration credentials).

## Status

| Item | Status |
|---|---|
| D12 tenant fairness (isolated tenant lanes) | ✅ Done and tested (§1). Not committed |
| D4 `loyalty_config` table + reader, cross-pod cache invalidation, `ISecretProtector` for secret rows | ⏳ To do |
| Publish `dEngage.Loyalty.Contract` (inbound/outbound event types, messaging topology, public API DTOs) | ⏳ To do |
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
