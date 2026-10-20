# Scope Change Impact Analysis: Event Throughput (Partitioned Processing) and Bulk Ingestion

**Status:** **Draft, analysis only.** Nothing is implemented. Needs Product Owner + Architect approval, and a Jira Scope Change Request for each phase (§11), before any code changes.
**Date:** 2026-10-13 (revision 1); revised 2026-10-20 (revision 2, see §2.7 and §17)
**Author:** Claude Code, at the request of Moiz
**Type:** Scope change. It changes non-functional scope (throughput, ordering model, horizontal scaling) and adds a capability (bulk ingestion) that has no row in `docs/SCOPE_BASELINE.md`. It also contains bug fixes that are worth doing on their own (Phase 1).
**Basis:** code read on 2026-10-13 on branch `1.8-rule-confg-limits-by-trigger` (after `4ebddcf`). Revision 2 re-checked every code reference on `platform-alignment/phase-2` (after platform alignment Phase 1, `c67c497`, and the D12 tenant lanes). Nothing was load-tested. No load test exists, so the throughput figures in §8 are a model to validate in Phase 0, not measurements.
**Target:** a client with about **60 million members**, where events must not queue up behind each other.

| # | Change | Type | Phase |
|---|---|---|---|
| 1 | Baseline load test, metrics and query plans | **Test / tooling only** | 0 |
| 2 | API publisher confirms; atomic inbox claim; race-free account and streak upserts; transient-error retry; one instance per nightly job; remove per-event full-ledger scans | **Bug fixes / hardening.** No behaviour change at today's load | 1 |
| 3 | Partitioned, parallel event processing with per-customer ordering, horizontal Consumer scaling, and a per-customer DB fence | **Scope change** (NFR + ordering model) | 2 |
| 4 | Bulk event API (`POST …/events/bulk`) and a separate bulk lane | **Scope change** (new capability) | 3 |
| 5 | Bulk file import (async jobs) and opening-balance migration | **Scope change** (new capability) | 4 |
| 6 | Scale-out of shared state: counter tables in place of ledger scans, hot-budget sharding, DB scaling | **Scope change** (NFR) | 5 |

---

## 0. Summary

Today the platform handles **one event at a time per queue**. All tenants share `q.loyalty`, with one consumer and a prefetch of 1. Since platform alignment D12 (§2.7), a tenant listed in `RabbitMq:IsolatedTenants` gets its own queue and worker, so its bursts no longer delay the others, but every tenant is still processed one event at a time. Total throughput is therefore `1 ÷ (time to process one event)`, whatever the hardware. Starting a second Consumer instance doesn't help; it breaks the ordering guarantees the code relies on (§2.5). Separately, the production API rate limit is **120 events per minute per tenant** (2/s), and several per-event queries get slower as the ledger grows. So the system slows down as a tenant succeeds.

The fix is not "process everything in parallel". The money logic needs **per-customer** order and mutual exclusion; it does not need global order. The recommended design:

1. **Partition** the event stream by `hash(tenant + contact_key)` into a fixed number of partitions (e.g. 128). Each partition is processed strictly in order by exactly one worker, and different partitions run in parallel across any number of Consumer instances. A customer's events keep their order, and unrelated customers no longer wait for each other.
2. Add a **per-customer database fence** and **atomic idempotency claims**, so correctness doesn't depend on the routing being perfect.
3. Remove the **per-event full-history queries** and **hot rows** that would otherwise make the database the new single lane.
4. Add **bulk ingestion** in three forms: a batch API for live traffic, an async file import for millions of rows, and a set-based opening-balance load for migrating 60M existing members.

Every step is **idempotent** (deterministic keys checked by unique indexes) and **race-free** (row locks or unique constraints, never "check, then write" without a lock). §7 shows each concern with its mechanism.

---

## 1. Why

- **Throughput ceiling.** `EventConsumerWorker` declares one queue and calls `BasicQos(0, 1, false)` ([EventConsumerWorker.cs:63-79](../../src/dEngage.Loyalty.Consumer/EventConsumerWorker.cs#L63-L79)). If one `order.created` takes 40 ms end to end, the shared queue processes at most 25 events/s, and so does each isolated tenant's queue. A delay for one tenant delays every tenant that isn't isolated (D12, §2.7).
- **No horizontal scale.** A second Consumer instance would compete for the same queue. That breaks per-customer ordering, exposes the check-then-write races in §2.5, and runs every nightly job twice (§2.6).
- **Front-door limit.** Production allows `EventIngestionPerMinutePerTenant = 120` ([appsettings.Production.json:23](../../src/dEngage.Loyalty.Api/appsettings.Production.json#L23)). A 60M-member tenant sends orders of magnitude more.
- **Latency grows with data.** Several checks run per event and scan a rule's entire ledger history (§2.4). Processing time per event goes up as the tenant grows, which lowers the ceiling further.
- **No bulk path.** There is no batch endpoint or file import. `docs/SCOPE_BASELINE.md` has no bulk row. Onboarding 60M existing members, or backfilling history, would take 60M HTTP calls at 2/s.

---

## 2. Current state (as-built)

### 2.1 Pipeline
API → `loyalty.events` (direct) → single `q.loyalty` → one consumer, prefetch 1 → inbox → `event_log` → handler → campaign evaluation → rule engine (match → context → reversal → redelivery guard → winners → campaigns → one posting transaction → Redis counters → tier) → inbox `processed` → ack. Outbound goes through `outbox_events` → `OutboxPublisherWorker` → `loyalty.outbound`. Since D12: when the event's tenant is in `RabbitMq:IsolatedTenants`, the `q.loyalty` worker doesn't process it; it moves it (confirmed publish, then ack) to `q.loyalty.tenant.<slug>`, where that tenant's own sequential worker runs the same pipeline. The full diagram was produced and checked against the code on 2026-10-13.

### 2.2 Throughput and scaling findings
| # | Finding | Where |
|---|---|---|
| F1 | One queue, prefetch 1: serial order across all tenants and customers on `q.loyalty`. Since D12 an isolated tenant has its own serial queue; within any tenant it is still one event at a time | `EventConsumerWorker.cs:63-109` |
| F2 | Production ingestion limit 120/min/tenant (staging 300, default 600) | `appsettings.Production.json:23`, `RateLimitOptions.cs:5` |
| F3 | API publishes **without publisher confirms**, so a 202 can be returned for a message the broker never stored. It also opens a channel and re-declares the exchange on every publish | [RabbitMqEventPublisher.cs:17-36](../../src/dEngage.Loyalty.Api.Framework/Messaging/RabbitMqEventPublisher.cs#L17-L36) |
| F4 | No retry for transient errors: a deadlock, lock timeout or Redis blip sends the event straight to the DLQ. Under concurrency these become routine | `EventConsumerWorker.cs:147-151` |
| F5 | Every Consumer instance runs every background job (tier downgrade, expiration, delayed promotion, streak maintenance, expiring detector, event-log retention). Nothing ensures only one instance runs a job | [Program.cs:142-150](../../src/dEngage.Loyalty.Consumer/Program.cs#L142-L150). Platform alignment left this item to this change (alignment D9) |
| F6 | One outbox publisher today. With several, `SKIP LOCKED` batches can publish one customer's outbound events out of order | `OutboxPublisherWorker.cs:106-113` |

### 2.3 Hot rows
| # | Finding | Where |
|---|---|---|
| H1 | Rule budgets are reserved on **one `rule_limit_counters` row per rule (and period)**, locked `FOR UPDATE` for the rest of the posting transaction. With parallel processing, every posting for a popular budgeted rule queues on that row | [BudgetReservationService.cs:20-51](../../src/dEngage.Loyalty.RuleEngine/Processing/BudgetReservationService.cs#L20-L51) |

### 2.4 Per-event queries whose cost grows with history
| # | Finding | Where |
|---|---|---|
| Q1 | Each budget reservation first computes a "seed" by **loading every ledger delta of the rule into memory** (`GetRuleBudgetUsedAsync` → `ToListAsync` → client-side sum). It does this even when the counter row already exists, and the `ON CONFLICT DO NOTHING` then discards the result | `BudgetReservationService.cs:26-35`, [RuleLimitEvaluator.cs:48-58](../../src/dEngage.Loyalty.RuleEngine/Processing/RuleLimitEvaluator.cs#L48-L58) |
| Q2 | The WinnerSelector budget pre-check runs the same full load | `WinnerSelector.cs:291-305` |
| Q3 | Max customers runs `COUNT(DISTINCT contact_key)` over the rule's ledger rows on every event | `RuleLimitEvaluator.cs:28-43` |
| Q4 | Cooldown and per-period customer usage query `ledger_entries` by `rule_id` and `contact_key`. There is **no index on `rule_id`**. The ledger indexes are `(tenant, idempotency_key)`, `(tenant, account, created_at)` and `(tenant, source_event_id)` | `RuleLimitEvaluator.cs:14-26, 61-69`; [LedgerEntryConfiguration.cs:35-43](../../src/dEngage.Loyalty.Schema/Configurations/LedgerEntryConfiguration.cs#L35-L43) |
| Q5 | The tier re-check sums ledger rows by `customer_account_id` **without a `tenant_id` filter**, so Postgres can't prune to the tenant's partition (to verify with `EXPLAIN` in Phase 0) | [TierEvaluationService.cs:143-160](../../src/dEngage.Loyalty.RuleEngine/TierEvaluationService.cs#L143-L160) |

### 2.5 Code that is only correct because processing is serial
| # | Today | Fails under concurrency as | Where |
|---|---|---|---|
| R1 | Inbox: `FirstOrDefault`, then `Add` | Two workers given the same event both insert. One gets a PK violation, which dead-letters a good event | `EventConsumerWorker.cs:161-185` |
| R2 | `UpsertAccountAsync`: `FirstOrDefault`, then `Add` | Two first events for a new customer hit the unique index, and one is dead-lettered. A transfer's receiver is the realistic case, because it is in another partition | [LedgerService.cs:131-160](../../src/dEngage.Loyalty.Ledger/LedgerService.cs#L131-L160) |
| R3 | Streak period state and progress are read-modify-write. The code comment says "Consumer is sequential (BasicQos 0,1) — plain read-modify-write is safe here" | Lost update on the same customer's streak | [StreakCampaignModule.cs:54-70, 98-115](../../src/dEngage.Loyalty.RuleEngine/Campaigns/Streak/StreakCampaignModule.cs#L54-L70) |
| R4 | Max customers is check-then-write (Q3) | Two new customers both pass when one slot is left: the cap is exceeded | `RuleLimitEvaluator.cs:28-43` |
| R5 | A rolling `RuleBudgetPerPeriod` relies on the pre-check only (documented) | Overspend once events run in parallel | `.claude/rules/backend-rule-engine.md` |
| R6 | Per-customer caps live in Redis: read in WinnerSelector, incremented after commit | Two events for the same customer both read the old value. This is safe only if a customer's events are serialized | `WinnerSelector.cs:261-310`, `LimitCounterSync.cs` |
| R7 | `order.refunded` carries **no `contact_key`**, and only works if it arrives after its `order.created` | Can't be routed to the customer's partition. If it runs before its order: `original_entries_not_found` → DLQ | `OrderRefundedHandler.cs`, `RefundService.cs:84` |
| R8 | SOW §3 already lists two gaps "under true concurrency": once-per-period cardinality by check-then-write, and per-customer limits read from a cache instead of a counter | — | `docs/SOW.md:204-209` |

Already race-safe, and they stay that way: ledger postings (unique `(tenant, idempotency_key)`), once-only awards (`once:{rule}:{contact}` key), outbox (unique dedup key), cash, redeem, transfer and purchase balance checks (`SELECT … FOR UPDATE`, transfers lock both wallets in ordinal order), held-posting refund versus promotion (shared row lock), and the budget total (row lock; only the hot-row cost, H1).

### 2.6 Bulk
No batch endpoint, no file import, no opening-balance load. `EventsModule` has one route per event type plus `/generic` and `/simulate` ([EventsModule.cs:28-99](../../src/dEngage.Loyalty.Api/Events/EventsModule.cs#L28-L99)). **No row in `docs/SCOPE_BASELINE.md`**: this is new scope.

### 2.7 Changes since revision 1 (platform alignment, 2026-10-20)
| Change | Where | Effect on this analysis |
|---|---|---|
| .NET 9 and the dEngage platform host (`dEngage.Common.Hosting`) for Api and Consumer; config from `CONFIG_PATH`; NLog; App.Metrics/Prometheus | Platform alignment Phase 1 (`c67c497`, `docs/platform-alignment/phase-1.md`) | The Consumer now runs a small Kestrel endpoint (`:5174`) with `/health-check` and Prometheus metrics. Phase 0's metrics (queue depth, lane latency) can be exported there instead of adding a new metrics stack |
| Consumer configuration fails fast (`ConsumerConfig.FromConfiguration`); one Redis key name with the Api | Phase 1 | New settings from this change (partition count, instance ordinal, retry policy) should go through the same fail-fast reader |
| **Tenant lanes (D12):** `RabbitMq:IsolatedTenants` lists tenant slugs; each gets `q.loyalty.tenant.<slug>` and its own sequential worker. The `q.loyalty` worker moves their events there in arrival order (publisher confirm before ack); the Api and publisher are unchanged | `TenantLanes.cs`, `EventConsumerWorker.cs`, `ConsumerConfig.cs` (platform alignment Phase 2, `docs/platform-alignment/phase-2.md`) | Removes cross-tenant head-of-line blocking for the listed tenants only. It does **not** raise a single tenant's throughput, so the 60M-member target still needs this change (Phases 1–2). It is the first parallelism across tenants: every piece of processing state was checked to be keyed by tenant, and the R1–R6 races here concern **one tenant's** events, which D12 still processes serially. When Phase 2 lands, the lanes are subsumed by partitions (§4 row A′, §11) |
| Platform alignment proposed `dEngage.Common.RecurringJob.Distributed` (the platform's single-runner job library) for F5 | Alignment D9/D13 | An alternative to the advisory lock in §5.9 (decision Q10) |

---

## 3. What actually has to be serialized

| State | Scope of serialization needed |
|---|---|
| Wallet balance, wallet creation, held postings | per customer (per wallet) |
| Streak progress, tier, per-customer caps, cooldown, once-only | per customer |
| Event order (refund after order, spend after load) | per customer |
| Transfer | two customers. Already handled with row locks in ordinal order |
| Rule budget (total, period), max customers | **per rule, across customers.** Needs locks or counters, not ordering |
| Rules, programs, tiers config | read-only during processing (cached) |
| Order across different customers or tenants | **not needed anywhere** |

**Conclusion:** the right unit of ordering is **`(tenant, contact_key)`**. Everything shared across customers is per-rule state, and that needs atomic counters, not a global queue.

---

## 4. Approaches for parallel processing

| | Approach | How it works | Pros | Cons | Verdict |
|---|---|---|---|---|---|
| A | **In-process keyed lanes** | One Consumer, prefetch e.g. 500. Each delivery is hashed by `(tenant, contact_key)` to one of L in-memory lanes, each processed sequentially; ack per message | Smallest change, no topology change; ~L× throughput | Still one instance: a single point of failure and a hard ceiling. Unacked buffer is lost on crash (redelivered, so safe but slow) | Stop-gap only |
| A′ | **Isolated tenant lanes** (implemented, D12) | Listed tenants get their own queue and sequential worker; the shared worker moves their events there | Removes one tenant's bursts from everyone else's path; no publisher or API change; small | Doesn't speed up any single tenant; one Consumer instance; the list is manual | Done as a fairness step. Subsumed by B |
| B | **Partitioned RabbitMQ queues** (recommended now) | API computes `p = hash(tenant, contact_key) mod N` and publishes with routing key `p.{p}` to N quorum queues `q.loyalty.p000…`, each with `x-single-active-consumer`. Each Consumer instance owns a fixed subset of partitions (instance i of M takes `p mod M == i`) and processes each partition in order | Per-customer order, horizontal scale up to N lanes, no new infrastructure or package (RabbitMQ 3.13 already in use), DLX stays as-is | Partitions are assigned statically, so losing an instance means its partitions wait until it restarts (e.g. a StatefulSet pod). Changing N needs a drain-and-switch | **Recommended** |
| B′ | Consistent-hash exchange plugin | Broker hashes the routing key onto bound queues | Broker does the hashing | Adding a queue remaps keys, which breaks per-customer order during the change. Distribution is weight-based | Not recommended |
| C | **RabbitMQ Super Streams + single active consumer** | Partitioned streams. The broker spreads partitions across consumers of the same name and fails over automatically | Automatic rebalancing, replay from any offset, high throughput, same broker | Needs the `RabbitMQ.Stream.Client` NuGet package (a new package needs approval) and a different consume and ack model | Strong next step if B's static assignment becomes an operational burden |
| D | **Kafka** | Key-partitioned topics, consumer groups, retention and replay | The industry standard at this scale: rebalancing, replay, compaction | New infrastructure and operations; the dEngage platform standard is RabbitMQ | Consider above sustained ~20k events/s, or if long replay is required |
| E | Competing consumers + DB locks only | Any worker takes any event; `pg_advisory_xact_lock(hash(tenant, contact))` gives mutual exclusion | Simple and elastic | Mutual exclusion **without order**: a refund can run before its order. Lock waits hold DB connections | Not on its own. Used **inside** B as a safety fence (§5.3) |

**Recommendation:** **B + the E fence** now, with the partition function written so it doesn't depend on the transport. Moving to C or D later then changes only the transport adapter, not the engine.

---

## 5. Recommended design (Phases 1–2)

### 5.1 Partition key
- `key = tenantSlug + "|" + contact_key`; `partition = FNV-1a-64(key) mod N`. It is implemented once in `dEngage.Loyalty.Shared` and used by the API, the bulk importer and the TestCli. **Never `string.GetHashCode()`**: it is randomized per process in .NET, so two processes would route the same customer differently.
- **N fixed at 128** (proposed). That is above any foreseeable number of concurrent lanes; scale by adding Consumer instances, not partitions.
- Events without `contact_key`:
  - `order.refunded`: add `contact_key` as a **new optional field** (additive, non-breaking). When it is missing, the API looks up the original order's inbox row by its primary key `(tenant, original_event_id)` and takes `contact_key` from there. If the original isn't found → see §5.7.
  - `points.transfer` → routed by the **sender**. The receiver's wallet is protected by the existing ordinal row locks plus the fence.
  - Generic events without `contact_key` → `hash(tenant + eventId)`. They have no customer state.
- **Hot customer** (one contact with very high volume): serialized by design, and isolated from everyone else. This is acceptable because the state is per customer.

### 5.2 Topology and consumption
- New exchange `loyalty.events.p` (direct). Queues `q.loyalty.p000…p127`: quorum, durable, `x-single-active-consumer = true`, `x-dead-letter-exchange = loyalty.events.dlx`. The DLQ stays `q.loyalty.dlq`. All names go into `MessagingTopology`.
- Each Consumer instance reads `Consumer:Instance` / `Consumer:InstanceCount` (e.g. a StatefulSet ordinal) and subscribes only to its own partitions. Single active consumer is the fence against a misconfiguration where two instances claim the same partition: only one consumes.
- Per partition: one channel, **prefetch 10–20 to hide network latency, processed strictly one after another**, and acked in order. Each partition has its own DI scope per event (as today).
- Lanes per instance = partitions owned. Example: 128 partitions on 8 instances gives 16 lanes each.

### 5.3 Per-customer fence (defense in depth)
At the start of every transaction that changes customer state (handler transactions, `LedgerPoster`, streak, tier, refund), take `pg_advisory_xact_lock(<64-bit hash of tenant|contact>)`. It is released automatically at commit or rollback, it works through PgBouncer in transaction mode, and it costs nothing when uncontended. If routing is ever wrong (during a rebalance, a misconfiguration, or a bulk lane racing a live lane, §6.3), two events of one customer still can't interleave. Transfers lock sender and receiver **in ordinal order**, like the existing row locks.

### 5.4 Atomic inbox claim (fixes R1)
```sql
INSERT INTO event_inbox (tenant_id, event_id, …, status, claimed_by, lease_until, attempts)
VALUES (…, 'processing', @worker, now() + interval '5 minutes', 1)
ON CONFLICT (tenant_id, event_id) DO UPDATE
   SET status = 'processing', claimed_by = @worker, lease_until = EXCLUDED.lease_until,
       attempts = event_inbox.attempts + 1
 WHERE event_inbox.status IN ('pending','failed')
    OR (event_inbox.status = 'processing' AND event_inbox.lease_until < now())
RETURNING status;
```
No row returned → the event is `processed`, or another worker holds a live lease → ack and skip. New columns: `claimed_by`, `lease_until`, `attempts` (migration + `loyalty_schema_reference.md`). New status value `processing`; Customer 360 and the event-status endpoint must display it (portal i18n).

### 5.5 Race-free writes (fixes R2–R6)
- **R2** `UpsertAccountAsync` → `INSERT … ON CONFLICT (tenant_id, contact_key, account_type_id) DO NOTHING`, then `SELECT` (and `FOR UPDATE` where a balance check follows).
- **R3** Streak → `INSERT … ON CONFLICT DO NOTHING`, then `SELECT … FOR UPDATE` on the period-state and progress rows (verify their unique indexes). Remove the "sequential" comment.
- **R4** Max customers → new table `rule_customers (tenant_id, rule_id, contact_key)`, unique. Inside the posting transaction: `INSERT … ON CONFLICT DO NOTHING RETURNING`. Only a **new** row takes a slot from a `rule_limit_counters` row of type `max_customers`, locked `FOR UPDATE`. That is one locked statement instead of the `COUNT(DISTINCT)` scan.
- **R5/R6 and SOW gaps** → new table `rule_customer_usage (tenant_id, rule_id, contact_key, total, period_key, period_total, day, day_total, last_awarded_at)`, updated in the posting transaction under the fence. It becomes the source of truth for per-customer total, day and period caps and for cooldown. Redis `limit:*` stays as a read-through cache only. The rolling period budget moves to bucketed counters (one row per day, summed over the window under lock).

### 5.6 Remove history scans (fixes Q1–Q5)
- Q1: check whether the counter row exists first; compute the seed **only on first creation**, and in SQL (`SUM(ABS(delta))`), never by loading rows into memory.
- Q2: the pre-check reads the counter row (no lock), not the ledger.
- Q3/Q4: replaced by §5.5. In the meantime, index `ledger_entries (tenant_id, rule_id, contact_key, created_at)`.
- Q5: add the `tenant_id` predicate. Better: keep a running `qualifying_points` / `qualifying_period_start` on the qualifying `customer_accounts` row, updated in the posting transaction (the nightly tier job reconciles it).

### 5.7 Retries and poison messages (fixes F4, R7)
- **Transient errors** (Npgsql `40001` serialization, `40P01` deadlock, `55P03` lock not available, connection drops, Redis timeouts): retry **in the lane** up to 3 times with jittered backoff (100 ms → 1 s). Retrying in place preserves the customer's order. Only then nack → DLQ.
- **Permanent errors** (bad payload, unknown reward): DLQ immediately, as today.
- **Refund before its order** (R7): treat it as transient for a bounded window. Send it to `q.loyalty.retry` with `x-message-ttl` = 30 s, dead-lettering back to its partition, at most 10 attempts, then DLQ.
- **Optional strict mode (decision Q5):** when a customer's event is dead-lettered, park that customer's later events until it is replayed. Today later events simply continue, and that stays the default.

### 5.8 API ingestion (fixes F2, F3)
- Publisher confirms: a pooled confirm-mode channel; topology declared once at startup. **Only return 202 after the broker confirms**; otherwise 503, and the client retries with the same `Idempotency-Key`. `Microsoft.Extensions.ObjectPool` ships with ASP.NET Core, so no new package is expected (verify).
- Rate limit: raise it per tenant to a contracted rate, and **count events, not requests**, so bulk calls are metered fairly. The limit is a commercial and operational setting, not a fix.
- `Idempotency-Key`: strongly recommended on every event; **required** on money-moving types and on every bulk item (decision Q4). Without it, a client retry after a timeout produces a second event.

### 5.9 Background jobs (fixes F5)
Each job takes `pg_try_advisory_lock(<job id>)` on a dedicated, non-pooled connection before running; instances that don't get it skip the run. Alternatives: move the jobs into a separate `Jobs` host with one replica, or use the dEngage platform's `dEngage.Common.RecurringJob.Distributed` (single runner across the cluster, Postgres lock), which platform alignment D9/D13 allow in the core. Confirm that library can keep its lock in the loyalty database before choosing it. Jobs that change balances (expiration, promotion, downgrade) also take the per-customer fence (§5.3) for each customer they touch, so they can't interleave with that customer's live events.

### 5.10 Outbound ordering (fixes F6)
Add `partition` to `outbox_events` (same function as §5.1). Each publisher instance publishes only its own partitions, in `id` order, so outbound events per customer stay in order. Also add a per-customer `seq` to outbound payloads (additive), so downstream systems can detect gaps and duplicates. Publishing in larger batches with several partitions in parallel raises outbound throughput.

---

## 6. Bulk ingestion (Phases 3–4)

"Bulk" covers three different needs. They get three different mechanisms.

### 6.1 B1: Bulk event API (live traffic, up to 1,000 events per call)
- `POST /api/v1/tenants/{tenantId}/events/bulk`
  ```json
  { "events": [ { "event_type": "order.created", "idempotency_key": "ord-123", "data": { "contact_key": "…", "amount": "120.50" } } ] }
  ```
- Each item is validated with the **existing per-type validator** (and the generic-type allow-list and scheduled-type rule). Invalid items are rejected individually; valid items are still accepted.
- **`idempotency_key` is required per item**, and `eventId = idempotency_key`. Retrying the whole batch is safe, because the inbox dedupes items that were already accepted.
- All valid items are published on one confirm-mode channel, each to its own partition. The call returns **202** only for confirmed items:
  `{ "accepted": 998, "rejected": 2, "items": [ { "index": 0, "eventId": "ord-123", "status": "accepted" }, { "index": 5, "status": "rejected", "error": "…" } ] }`
- Limits: at most 1,000 items and 5 MB per call (decision Q7). The rate limiter counts items.
- Tenant: always taken from the route; a tenant inside an item is ignored or rejected (tenancy contract).

### 6.2 B2: Bulk file import (millions of rows, asynchronous)
- `POST …/imports` (CSV or NDJSON; multipart upload, or a reference to object storage) → creates `bulk_import_jobs` (id, tenant, event_type, file_ref, file_sha256, status, total_rows, published_rows, failed_rows, checkpoint_row, created_by, timestamps). Also `bulk_import_errors (job_id, row_no, error)`.
- **Idempotent job creation:** unique `(tenant, file_sha256)` plus the request's `Idempotency-Key`. Uploading the same file twice returns the existing job.
- **Worker:** claims a job with `FOR UPDATE SKIP LOCKED` and streams the file. Each row is validated, and `eventId = row.idempotency_key ?? "bulk:{jobId}:{rowNo}"` (deterministic). Rows are published to the **bulk lane** with confirms, in chunks of 5,000. `checkpoint_row` is committed **after** the chunk's confirms.
  - Crash → resume from the checkpoint. Re-published rows are deduped by the inbox, so nothing is applied twice.
- **Back-pressure:** the worker pauses while the bulk lane's queue depth is above a threshold, and each tenant has a bulk quota. One tenant's import can't flood the cluster.
- `GET …/imports/{id}` (progress) · `GET …/imports/{id}/errors` (error report) · `POST …/imports/{id}/cancel`.

### 6.3 Bulk lane
- Bulk events go to a **separate set of partitions** `q.loyalty.bulk.p000…` with the same hash, consumed by a capped pool of workers. Live traffic is never queued behind a 10M-row import.
- **Trade-off (decision Q6):** the live and bulk lanes are different queues. The §5.3 fence still guarantees mutual exclusion per customer, but **order between a live event and a bulk event for the same customer is not guaranteed**. That is fine for backfills of history. A use case that needs strict order must go through the live lane (B1).

### 6.4 B3: Opening balances (migrating 60M existing members)
Pushing 60M synthetic events through the rule engine would be wrong (rules would fire on migration data) and slow. Instead:
1. `COPY` the file into a staging table (Npgsql binary import; no new package).
2. Per chunk of 10–50k rows, **in one transaction**:
   - `INSERT INTO customer_accounts … ON CONFLICT DO NOTHING`
   - `INSERT INTO ledger_entries (reason = 'opening_balance', idempotency_key = 'opening:{jobId}:{contact}:{accountType}') … ON CONFLICT (tenant_id, idempotency_key) DO NOTHING RETURNING`
   - `UPDATE customer_accounts SET balance = balance + delta` **only for the rows actually inserted** (from the `RETURNING`), so a rerun adds nothing.
   - Checkpoint the chunk.
3. No per-row outbox events; one `loyalty.import.completed` summary instead. Tiers are assigned afterwards by the tier job.
It is append-only (opening balances are ledger entries, never edits), idempotent by key, resumable by chunk, and runs outside the live lanes. It needs a new `LedgerReason` and a baseline row (decision Q8).

### 6.5 Consumer micro-batching (optional, later)
Inside a lane, consecutive events could share multi-row inbox and `event_log` inserts and outbox writes. That might gain 20–30%, but it complicates failure handling. Measure first (Phase 0) and decide in Phase 5.

---

## 7. Idempotency and race-freedom by concern

| Concern | Today | After | Mechanism |
|---|---|---|---|
| Duplicate HTTP request | Deduped only if the client sends `Idempotency-Key` | Same; required for money types and bulk | `eventId = key`; inbox PK `(tenant, event_id)` |
| 202 returned but message lost | Possible (no confirms) | Not possible | Publisher confirms before 202 |
| Same event delivered twice, at the same time | Safe only because serial | Safe | Atomic inbox claim with lease (§5.4) |
| Redelivery after a crash | Safe (R15 guard + posting keys) | Unchanged | `HasPostedAsync`, unique ledger key |
| Customer's events out of order | Never (one FIFO per tenant: `q.loyalty`, or the tenant's lane since D12) | Never within a lane | Partition by `(tenant, contact)`; single active consumer |
| Two workers on one customer | Impossible (one worker per tenant, D12) | Prevented | Partition ownership + `pg_advisory_xact_lock` fence |
| Wallet creation race | Unique violation → DLQ | Safe | `ON CONFLICT DO NOTHING` |
| Balance check versus debit | Safe | Unchanged | `SELECT … FOR UPDATE` |
| Transfer deadlock | Safe | Safe | Ordinal lock order for rows and fence |
| Streak lost update | Safe only because serial | Safe | Upsert + row lock |
| Per-customer caps and cooldown | Redis read-then-incr; safe only because serial | Safe | `rule_customer_usage` in the posting tx |
| Rule budget total or period | Row lock (safe, hot) | Safe, less contention | Seed once; shard if hot (§9) |
| Max customers | Count-then-post race | Safe | `rule_customers` unique + locked counter |
| Once-only (signup/KYC) | Safe (unique key) | Unchanged | `once:{rule}:{contact}` |
| Outbound duplicates | Safe (dedup key) | Unchanged | `ux_outbox_dedup` |
| Outbound order per customer | Single publisher | Kept | Partition-owned publishing + `seq` |
| Nightly job run twice | On every instance | Once | `pg_try_advisory_lock` per job |
| Bulk batch retried | — | Safe | Per-item key → inbox |
| Bulk file re-run or crash | — | Safe | Deterministic row ids + checkpoint after confirm |
| Opening balance re-run | — | Safe | `ON CONFLICT DO NOTHING RETURNING`; balance updated only for inserted rows |

---

## 8. Capacity model (to validate in Phase 0)

The required parallelism is `lanes ≈ peak events/s × seconds per event`.

**Illustrative assumptions, to replace with the client's real figures:**
- 60M members × 1.5 events/member/day → 90M events/day → about 1,040 events/s on average.
- A 10× peak → about **10,000 events/s**.
- 30 ms per event, once the §5.6 fixes are in.
- **Result:** 10,000 × 0.03 ≈ **300 concurrent lanes**.
  - With N = 128 partitions that is short by design, so either N = 256–512, or the per-event time has to come down.
  - The **database becomes the limit**. Each event is about 4–6 transactions and 20–40 round trips, which is about 50k transactions/s at peak. That is beyond a comfortably sized single Postgres primary.

**Levers, in order of cost:**
1. Fewer round trips: merge the inbox claim, handler and posting into fewer transactions, and keep counters in rows (§5.5) instead of scans.
2. PgBouncer in transaction mode (the fence and job locks are compatible, §5.3, §5.9), and read replicas for portal and Customer 360 reads.
3. Sub-partition `ledger_entries` (today one list partition per tenant: a 60M-member tenant is one huge table) by hash of account or by month.
4. **Tenant-level sharding:** the largest tenants get their own database cluster. Routing already starts from the tenant slug, so this fits the multi-tenant model.

Phase 0 must give the real seconds-per-event, transactions-per-event and the plans of the hot queries before N and the sizing are fixed.

---

## 9. Hot rule budgets (Phase 5)

One budget row per rule serializes every posting of that rule across all lanes (H1). Options:
- **(a) Hold the lock for less time** (cheap): reserve the budget **last**, just before commit, so the row lock is held for milliseconds. This is enough for most rules.
- **(b) Sharded budget counters:** K sub-rows per rule, each holding `budget / K`. A lane takes the shard matching its partition. When a shard is empty, it takes slack from the others under lock. The total is never exceeded, but it is more complex. Only for rules with a total budget and a high fire rate.
- **(c) Redis atomic reserve (Lua)** with DB reconciliation: fastest, but Redis is not authoritative for money. **Rejected.**

Recommendation: (a) for every rule, and (b) only if Phase 0 or production shows contention.

---

## 10. Contracts check (`00-project-guardrails.md` §2)

| Contract | Impact |
|---|---|
| Tenancy | The partition key includes the tenant; bulk tenant comes only from the route; new tables carry `tenant_id` and are filtered by it. No change to tenant resolution |
| Money | Bulk amounts stay JSON strings / `decimal`; opening balances are `numeric(20,4)` |
| Ledger | Append-only kept: opening balances are entries; no updates to ledger rows; every new path uses deterministic idempotency keys |
| Events | Payloads stay snake_case. `order.refunded.contact_key` and outbound `seq` are additive |
| Rule versioning, CASH approvals | Unchanged |
| Time | UTC; streak timezones unchanged |
| DSL parity | Unchanged |

**Breaking changes:** none forced.
- New topology: the old `q.loyalty` is drained and retired (§11).
- `Idempotency-Key` becomes required only on bulk, and on money types if Q4 = yes. Required on money types would be a breaking API change for existing integrations: announce it with a deprecation window.
- New inbox status `processing` is visible in the portal and in `GET events/{eventId}`.

---

## 11. Phasing and rollout

| Phase | Content | Exit criteria |
|---|---|---|
| **0: Measure** | Load-test harness (extend `TestCli`); metrics (queue depth per partition, lane latency p50/p95/p99, DB tx/s, lock waits); `EXPLAIN ANALYZE` of Q1–Q5 on a tenant with ≥10M ledger rows | Baseline events/s and seconds-per-event recorded |
| **1: Hardening** (no behaviour change) | F3 publisher confirms; §5.4 atomic inbox claim; R2/R3 upserts; §5.7 transient retry; §5.9 job locks; §5.6 Q1/Q2/Q5 fixes + ledger index | All existing tests green; new concurrency tests (two workers, same event; two first events, same customer); Phase 0 rerun shows lower seconds-per-event |
| **2: Partitioned processing** | §5.1–5.3, §5.10; `order.refunded` `contact_key`; refund retry queue; config `Messaging:Partitioned` | Load test reaches the target events/s; order tests per customer; chaos test (kill an instance mid-lane → no loss, no double posting) |
| **3: Bulk API** | §6.1, §6.3 | 1,000-item batch retried 3× → applied once |
| **4: Imports + opening balances** | §6.2, §6.4 | 10M-row import, killed mid-run and resumed → exact totals; opening-balance rerun → balances unchanged |
| **5: Shared-state scale-out** | §5.5 R4–R6 counter tables; §9 budgets; §8 DB levers; evaluate C/D | Contention metrics under peak within target |

**Switch-over for Phase 2 (keeps per-customer order):**
1. Deploy the Consumers with partitioned listeners **idle**.
2. Switch the API to publish to `loyalty.events.p`.
3. The legacy single-lane consumer drains `q.loyalty` and every `q.loyalty.tenant.<slug>` lane (D12).
4. The partition listeners start only once `q.loyalty` **and all tenant lanes** are empty (a startup gate). A customer's old and new events therefore never run out of order.
5. Retire `q.loyalty`, the tenant lanes, `RabbitMq:IsolatedTenants` and the legacy listener in the next release. If a tenant still needs isolation after that, give it its own partition set rather than keeping lanes.

**Rollback:** switch the API back. Partition queues drain under the fence, and the legacy listener resumes.

---

## 12. Risks
| Risk | Mitigation |
|---|---|
| Hidden reliance on global order not found in this analysis | Phase 1 concurrency tests; per-customer fence; Phase 2 chaos test. D12 already runs different tenants in parallel; no cross-tenant shared state was found on the event path |
| DB becomes the bottleneck instead of the queue | §8 levers; Phase 0 numbers decide N and sizing before Phase 2 |
| Static partition assignment: a dead instance stalls its partitions until restart | StatefulSet auto-restart + alert on partition lag; approach C if unacceptable |
| Bulk floods shared infrastructure | Separate lane, back-pressure, per-tenant quotas |
| Live versus bulk ordering for the same customer | Documented (§6.3); strict-order cases use B1 |
| Changing N later | Drain-and-switch procedure as in §11; choose N generously |
| Requiring `Idempotency-Key` breaks integrations | Required only on new bulk endpoints; money types after a deprecation window (Q4) |

---

## 13. Decisions needed

| # | Question | Recommendation |
|---|---|---|
| Q1 | Transport: partitioned RabbitMQ queues (B), Super Streams (C) or Kafka (D)? | **B now**, with a transport-independent partition function; revisit C/D after Phase 0 |
| Q2 | Number of partitions N | 128, final value after Phase 0 (§8) |
| Q3 | `order.refunded`: add optional `contact_key` with an inbox lookup fallback? | **Yes** |
| Q4 | `Idempotency-Key` required on money-moving events (cash, redeem, transfer, purchase)? | Yes, after a deprecation window; required on bulk from day one |
| Q5 | Strict mode: park a customer's later events while one of theirs is dead-lettered? | No by default (today's behaviour); per-tenant option later |
| Q6 | Accept no live-versus-bulk ordering for the same customer? | Yes, for imports and backfills |
| Q7 | Bulk limits: items per call, file size, formats (CSV / NDJSON) | 1,000 items / 5 MB; CSV + NDJSON; 2 GB per file |
| Q8 | Is the opening-balance migration (B3) in scope? | Yes, needed to onboard 60M members |
| Q9 | Target SLOs: sustained and peak events/s, end-to-end p95 (accept → posted) | Client to provide; drives §8 |
| Q10 | Jobs: advisory-lock leader election, a separate Jobs host, or the platform's `dEngage.Common.RecurringJob.Distributed`? | Advisory lock (no new deployable); the platform library if it can keep its lock in the loyalty DB (§5.9) |
| Q12 | Keep D12 tenant lanes after Phase 2? | No: drain and retire them at the switch-over (§11); isolation becomes a partition-set decision |
| Q11 | Production rate limit per tenant | Contracted per tenant; counted per event |

---

## 14. Explicitly out of scope
- Multi-region and active-active deployment.
- Changing rule semantics, payout amounts, or the condition DSLs.
- Replacing Redis or Postgres.
- Real-time analytics on the event stream.
- Tenant-level DB sharding beyond the design note in §8 (its own Scope Change Request if Phase 0 shows it is needed).

---

## 15. Docs to update when each phase is implemented
- `docs/SCOPE_BASELINE.md`: new rows for bulk API, imports and opening balances; an NFR row for throughput and ordering.
- `docs/SOW.md` §3: close the two concurrency gaps (R8) when Phase 5 lands.
- `LoyaltySaaSApi.md`:
  - `POST events/bulk` and the imports endpoints;
  - `order.refunded.contact_key`;
  - 503 when the broker doesn't confirm;
  - the `Idempotency-Key` requirements;
  - the inbox status `processing`.
- `scripts/loyalty_schema_reference.md`: inbox columns; `rule_customers`, `rule_customer_usage`, `bulk_import_jobs`, `bulk_import_errors`; the `outbox_events.partition` column; the new ledger index.
- `.claude/rules/backend-consumer.md` ("don't raise the prefetch or parallelize without an architecture decision" becomes the partition model; the D12 tenant-lanes paragraph is removed when lanes are retired) and `backend-api.md` (bulk ingestion). Also the debugging pitfalls in `01-workflow-and-debugging.md`.

---

## 16. Document record (review record per `00-project-guardrails.md` §6)
| File | Change | Reason |
|---|---|---|
| `docs/scope-changes/2026-10-13-event-throughput-partitioning-and-bulk.md` | Created | Impact analysis for this scope change. No code was changed |
| `docs/scope-changes/2026-10-13-event-throughput-partitioning-and-bulk.md` | Revised (rev 2) | Brought in line with platform alignment Phase 1 and D12: code references re-checked, §2.7 added, lanes added to §0–§2, §4, §5.9, §7, §11–§13, §15. No code was changed by this revision |

## 17. Revision history
| Rev | Date | Change |
|---|---|---|
| 1 | 2026-10-13 | First draft from a code read on `1.8-rule-confg-limits-by-trigger` (`4ebddcf`) |
| 2 | 2026-10-20 | Updated for platform alignment Phase 1 (.NET 9, platform host, fail-fast config) and D12 tenant lanes; line references re-checked on `platform-alignment/phase-2`; job single-runner alternative (Q10); lane retirement at the Phase 2 switch-over (Q12) |
