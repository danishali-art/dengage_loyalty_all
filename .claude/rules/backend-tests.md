---
paths:
  - "src/dEngage.Loyalty.Engine.Tests/**"
  - "src/dEngage.Loyalty.IntegrationTests/**"
---

# Backend tests — Engine.Tests and IntegrationTests

xUnit + FluentAssertions (`.Should()`), with Moq for unit tests. **Never delete, skip
(`Skip =`), or loosen a test to make a change pass.** If a test's expectation is actually wrong,
say so and explain why before changing it.

## Where a test goes
| What you're testing | Project | Notes |
|---|---|---|
| Pure logic: rule handlers' `Compute`, condition evaluators/DSL, winner selection, period math, registries, `NextRunAt` | `dEngage.Loyalty.Engine.Tests` | Fast, no infra. Mock the `Processing/I*` seams with Moq. |
| Engine behaviour against a real DB/Redis: limits, budgets, versioning, stacking, jobs | `IntegrationTests/Engine` | Use `RuleEngineTestHarness`. |
| HTTP endpoints, auth, validation, status codes, host boot | `IntegrationTests/Api` | `CustomWebApplicationFactory` / `PostgresWebApplicationFactory` + `JwtIssuingHelper`. |
| Full flows across processes (ingest → consume → ledger → endpoint) | `IntegrationTests/E2E` | Slowest. Only for real end-to-end guarantees. |

## Conventions
- Name tests `Method_Scenario_ExpectedResult`, or follow the existing naming in the file you're
  editing. One behaviour per test, with arrange/act/assert kept readable.
- Use `[Theory]` + `[InlineData]`/`MemberData` table tests for DSL, operator, rounding and period
  cases. Condition DSL cases should mirror the portal's table tests.
- Use the fixtures instead of building your own: `FakeEventPublisher` (assert published events),
  `FakeRateLimiter`, `JwtIssuingHelper` (tenant admin, platform admin, API-key principals).
- Integration tests use **Testcontainers** (Postgres, Redis, RabbitMQ). Docker must be running. If
  it isn't, report that. Don't mock the DB out of an integration test to make it pass.
- Tests must be isolated and parallel-safe: a unique tenant slug and ids per test, no dependence
  on execution order, no shared mutable static state (see the `NancyModuleTypeRegistry` lock).
- Money assertions compare `decimal` values exactly (for example `100.0000m`). Never use float tolerance.
- Time-dependent logic: pass an explicit `DateTime` (as `NextRunAt(now)` does) or use `IClock`.
  Never depend on the real wall clock.

## What every change must cover
- A bug fix → a test that fails without the fix.
- A new validator rule → a valid case plus each invalid case (including "field set when it must
  not be").
- A new domain error code → a test that it maps to the intended HTTP status.
- Tenancy → at least one test that tenant B can't read or modify tenant A's resource.
- Ledger or engine side effects → assert idempotency (process the same event twice → one posting,
  one budget reservation, one limit increment).

## Running
```
dotnet test src/dEngage.Loyalty.Engine.Tests
dotnet test src/dEngage.Loyalty.IntegrationTests            # needs Docker
dotnet test --filter "FullyQualifiedName~<ClassName>"       # focused run
```
Report the pass/fail counts, and show the output of any failure.
