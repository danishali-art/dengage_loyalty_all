---
paths:
  - "src/dEngage.Loyalty.Ledger/**"
---

# dEngage.Loyalty.Ledger — balances, outbox, expiry

The source of truth for customer balances. It references Schema, Shared and Engine.Framework.
**Correctness beats convenience here, every time.**

## Ledger invariants (never break these)
- **Append-only:** `LedgerEntry` rows are never updated or deleted, including by jobs,
  migrations, scripts or "quick fixes". Corrections, refunds, reversals and expiries are
  **new** entries with the right `LedgerReason`.
- **Idempotent:** `AddEntryAsync(..., idempotencyKey, ...)` returns the existing entry without
  touching the balance if the key was already written. The unique index is
  `(tenant_id, idempotency_key)`, so **always filter by tenant** when checking. Keys are
  deterministic (for example `{eventId}:{ruleId}`), never random.
- **Serialized:** the balance changes only inside a transaction, after taking a row lock
  (`LockAccountAsync` → `SELECT ... FOR UPDATE`). Never read a balance, compute, and write it back
  without the lock.
- The balance on `CustomerAccount` and the sum of its ledger entries must agree. Any code that
  writes one without the other is a bug.
- Overdrafts fail with `InvalidOperationException("insufficient_balance: ...")` /
  `"insufficient_points: ..."`, inside the transaction. Don't clamp silently.

## Outbox
- `IOutboxService.Enqueue` adds an `OutboxEvent` to the **current DbContext without saving**, so
  it commits atomically with the ledger change. `OutboxPublisherWorker` (Consumer) publishes it
  to `MessagingTopology.OutboundExchange`.
- Use a `dedupKey` for anything that could be enqueued twice (for example `rule_awarded:{eventId}:{ruleId}`).
- `OutboxEventPublisherSink` adapts this to Engine.Framework's `IEventPublisherSink`. Keep the
  "add, don't save" contract identical.

## Jobs (`PointsExpirationJob`, `PointsExpiringDetectorJob`, `EventLogRetentionJob`)
- Called by Consumer workers. Each run must be idempotent (a rerun after a mid-run crash does no
  double work) and filter every query by tenant. Keep the work bounded, so a run never loads a
  whole table into memory.
- Expiry posts negative entries with an expiry reason and idempotency key. It never deletes or
  edits the original credits.
- Retention (`RetentionPolicy`) applies only to `event_log` / inbox-type operational tables,
  **never** to the ledger.

## Refunds (`RefundService`)
- A refund reverses exactly what the original posting granted (look it up by source event or
  idempotency key). Fail with `original_entries_not_found` rather than guessing.
