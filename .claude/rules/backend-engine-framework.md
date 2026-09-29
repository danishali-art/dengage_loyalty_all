---
paths:
  - "src/dEngage.Loyalty.Engine.Framework/**"
---

# dEngage.Loyalty.Engine.Framework — engine-side abstractions

Small, stable seams shared by Ledger, RuleEngine and Consumer. It references **only Schema and
Shared**, and must never reference Ledger, RuleEngine or Consumer (that would create a cycle:
Ledger → Engine.Framework, RuleEngine → Ledger).

## What lives here, and nothing else
| Area | Types | Contract |
|---|---|---|
| `Abstractions/` | `IClock` / `SystemClock`, `IIdGenerator` / `SystemIdGenerator` | UTC time, and Postgres-friendly UUIDs (UUIDNext). Swap them in tests. |
| `Consumers/` | `IEventHandler`, `IEventHandlerRegistry` | One handler per `EventType`. The `null` EventType is the single fallback. |
| `Events/` | `IDomainEvent`, `IDomainEventDispatcher`, `DomainEventDispatcher`, `IOutboxTranslator<T>`, `IEventPublisherSink` | Typed domain events → outbox. |
| `Bootstrap/` | `AddLoyaltyEngineFramework()` | Registers clock, id generator and dispatcher. Doesn't register the sink. |

## Rules
- Keep this project **interfaces plus trivial defaults**. No business logic, no EF queries, no
  Redis or RabbitMQ. If code needs one of those, it belongs in Ledger, RuleEngine or Consumer.
- Every interface change here ripples into three projects plus the tests. Prefer adding a new
  interface or member with a default over changing an existing signature, and update every
  implementation in the same change.
- **Domain events:** add a concrete `IDomainEvent` (with `TenantId` and `OccurredAtUtc`), then an
  `IOutboxTranslator<TEvent>` that owns the event's exact outbound JSON. Register the translator
  in DI. An event with **no translator is dropped silently** on purpose: register one only for
  events that should go out.
- Outbound payloads must stay byte-compatible for existing consumers. Changing a translator's
  shape is a breaking contract change.
- `IEventPublisherSink.Publish` has the same contract as `Ledger.OutboxService.Enqueue`: it
  **adds to the DbContext and doesn't save**. The caller's transaction commits it. Never call
  `SaveChanges` or publish to RabbitMQ from a sink or translator.
- The sink implementation (`Ledger.OutboxEventPublisherSink`) is registered by the host
  (Consumer `Program.cs`) before `IDomainEventDispatcher` is resolved.
- `DomainEventDispatcher` is Scoped, because it shares the request/job scope and its DbContext.
