---
paths:
  - "src/dEngage.Loyalty.Consumer/**"
---

# dEngage.Loyalty.Consumer — event worker and scheduled jobs host

A generic-host process (`Host.CreateApplicationBuilder`). It consumes RabbitMQ events, runs them
through handlers and the rule engine, and hosts the nightly and periodic jobs. **It moves money.
Every path must be idempotent and safe to retry.**

## Event pipeline (`EventConsumerWorker`) — understand it before touching handlers
1. One durable queue (`MessagingTopology.Queue`) bound to every `EventTypes.All` plus the
   configured `RabbitMq:GenericEventTypes`. `BasicQos(0,1)` processes **sequentially** to keep
   per-customer ordering. Don't raise the prefetch or parallelize without an architecture decision.
2. **Idempotency:** the `EventInbox` row keyed by `(tenant, event_id)` → a processed duplicate is
   skipped. Status is `Pending` → `Processed` or `Failed`.
3. Non-built-in types → `GenericEventValidator.Validate`. Then an idempotent `event_log` insert
   (`ON CONFLICT DO NOTHING`).
4. `IEventHandlerRegistry.Resolve(eventType).HandleAsync(...)`.
5. **The worker itself then calls `ICampaignEvaluationService.EvaluateAsync` for every event type
   except `order.created`.**
6. Failure → the inbox is marked `Failed`, then **nack without requeue → dead-letter queue**. A
   failure is never lost and never retried in a hot loop.

## Adding an event handler
- `public class <Name>Handler(...) : IEventHandler` in `Handlers/`, with
  `EventType => EventTypes.<Constant>`. Exactly one handler per event type. `GenericEventHandler`
  (`EventType == null`) is the only fallback.
- Register it in `Program.cs`: `builder.Services.AddScoped<IEventHandler, <Name>Handler>();`.
  An unregistered handler silently falls back to generic handling.
- Read payload fields in **snake_case** (`contact_key`, `account_type_id`, ...). Missing fields
  must be logged and handled explicitly, never silently skipped.
- **Don't call `ICampaignEvaluationService.EvaluateAsync` from a handler** unless you also exclude
  that event type in the worker (as `order.created` is). Otherwise the rule engine runs twice for
  one event. Ledger postings are deduped, but budget reservations and Redis limit counters are not.
- Balance changes go through `ILedgerService` / the rule engine, never through direct
  `CustomerAccount` edits. Outbound messages go through the outbox (`IOutboxService` or
  `IDomainEventDispatcher`), never a direct RabbitMQ publish.
- Throw on unrecoverable input, and let the worker mark it failed and dead-letter it. Don't
  swallow the exception and ack.

## Background workers
- Shape (see `BirthdayBonusWorker`): `BackgroundService` → a loop computing `NextRunAt(UtcNow)` →
  `Task.Delay` → **a new DI scope per run** → resolve the `*Job` → `RunAsync(ct)`. Catch
  `Exception when not OperationCanceledException`, log it, and wait for the next run.
- **Workers only schedule.** Job logic lives in the owning domain project (`Ledger/*Job`,
  `RuleEngine/*Job`, `Campaigns/Streak/StreakMaintenanceJob`) so it can be tested without the host.
- Keep `NextRunAt` a pure `internal static` function so it can be tested.
- Nightly order is deliberate (UTC): tier downgrade 00:00 → birthday bonus 02:00 → points
  expiration 03:00. Check the dependencies before moving a schedule.
- Register with `AddHostedService<...>()` in `Program.cs`. Jobs are `AddScoped`.
- Jobs must be idempotent. A crash mid-run followed by a rerun must not grant or expire twice.

## Composition (`Program.cs`)
- Order: ENC(...) secret resolution → `ConsumerConfig` → `AddLoyaltyEngineFramework()` → register
  `IEventPublisherSink` (Ledger's `OutboxEventPublisherSink`) → DbContext / tenant resolver /
  Redis → Ledger → RuleEngine → campaigns → handlers → hosted services.
- Rule type handlers are `AddSingleton<IRuleTypeHandler, ...>`. `TransferRule` / `ReversalRule`
  deliberately bypass that registry through dedicated processors.
- Every service that touches `LoyaltyDbContext` is **Scoped**. Never inject a scoped service into
  a singleton or a `BackgroundService` constructor. Use `IServiceScopeFactory`.

## RabbitMQ topology
- Names come only from `MessagingTopology`. Changing the queue's arguments requires deleting the
  existing queue once (`PRECONDITION_FAILED`). Call that out in the change and in deployment notes.
