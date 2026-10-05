---
paths:
  - "src/dEngage.Loyalty.Schema/**"
  - "src/dEngage.Loyalty.Shared/**"
  - "src/dEngage.Loyalty.CryptoCli/**"
  - "src/dEngage.Loyalty.TestCli/**"
---

# Schema, Shared and the CLIs — the foundation layers

## dEngage.Loyalty.Schema (EF Core 8 + Postgres)
References **only Shared**. Everything else depends on it, so changes here ripple everywhere.

### Entities and configuration
- An entity is a plain class in `Entities/`. Its mapping is an `IEntityTypeConfiguration<T>` in
  `Configurations/<Entity>Configuration.cs`, picked up by `ApplyConfigurationsFromAssembly`.
  **No data annotations** on entities.
- Explicit snake_case names: `ToTable("reward_definitions")`, `HasColumnName("tenant_id")`, and so on.
  There is no automatic naming convention, so every new property needs `HasColumnName`.
- Money and points → `HasColumnType("numeric(20,4)")`. JSON config → `HasColumnType("jsonb")` with
  a default (`"{}"`) and a string property. Strings get an explicit `HasMaxLength`.
- Tenant-owned entities implement `ITenantScopedEntity` (`Id`, `TenantId`), with an index that
  leads with `tenant_id`. Uniqueness is per tenant (for example `(tenant_id, idempotency_key)`).
- Status and type columns store constants from `Shared/Constants` as strings. Don't use
  integer enums on the wire or in the DB.

### Migrations
- Add one: `dotnet ef migrations add <Name> -p src/dEngage.Loyalty.Schema` (the design-time
  `LoyaltyDbContextFactory` lives in this project). Suffix names with the change request id,
  for example `CustomerBirthdayCr10`.
- **Never** edit an applied migration or `LoyaltyDbContextModelSnapshot.cs`. Fix mistakes with a
  new migration.
- Review the generated `Up`/`Down` before handing it over. Changes must not lose data:
  1. add a nullable column,
  2. backfill it,
  3. then tighten it.
  Never drop or rename a column that still holds data without an approved plan.
- Some tables are partitioned (per-tenant partitions are created during provisioning, see
  `scripts/provision_tenant.sql` and the `InitialCreate` / `EventLog` migrations). Raw SQL in
  migrations must work with partitions.
- Ledger tables stay append-only. Migrations never `UPDATE` or `DELETE` ledger rows.
- After a schema change, update `scripts/loyalty_schema_reference.md` by hand in the same change
  (new/changed tables, columns, types, keys, indexes, with their Turkish descriptions), checked
  against the EF Core configurations and the latest migration id. Keep
  `scripts/loyalty_schema.sql` / `provision_tenant.sql` in step if provisioning depends on the change.

### Tenant resolution
- `TenantSlugResolver` / `TenantSlugCache` converts a slug to a Guid. It's shared by the Api and
  the Consumer, so keep it cheap and cached.

## dEngage.Loyalty.Shared
References **nothing**. It holds only constants, event contracts, `Result`, and crypto helpers.
- `Constants/*` and `Events/EventTypes` / `OutboundEventTypes` are **wire and DB contracts**.
  Add values freely. Renaming or removing one breaks stored data, queued events and the portal,
  so that is a scope change.
- `EventEnvelope` shape changes are breaking for every producer and consumer. Only add optional
  members.
- `GenericEventValidator` guards tenant-defined event types. Keep it strict.
- `Security/SecretCrypto` handles `ENC(...)` config secrets. Never log keys or decrypted values,
  and never weaken the algorithm or key loading.
- No EF, no DI, no I/O in this project.

## CLIs
- `CryptoCli` encrypts and decrypts `ENC(...)` config values. Never echo secrets into logs or files.
- `TestCli` is a dev tool that drives the Consumer, Ledger and RuleEngine locally. Don't add
  production-only logic here, and don't let production code reference it.
