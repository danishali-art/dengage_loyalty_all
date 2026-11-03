# Loyalty — Deployment

What the `dengage_loyalty_all` repository ships, how each part is configured, and what DevOps needs
to set up. The repository follows the dEngage multi-image layout (as `dengage.coupon`): one
solution, one `build.json`, several Docker images built by the shared Bitbucket pipeline, plus
NuGet packages for other dEngage services (as `dengage.models` and `dengage.parameters`).

## 1. Summary

| What | Count | Names |
|---|---|---|
| Docker images, deployed separately | **4** | `dengage.loyalty.api`, `dengage.loyalty.public.api`, `dengage.loyalty.consumer`, `dengage.loyalty.frontend` |
| NuGet packages on the private feed | **2** | `dEngage.Loyalty.Contract`, `dEngage.Loyalty.Api.Client` |
| Database step per release | 1 | `scripts/loyalty_schema.sql` (idempotent migration script) |

Everything else in the solution is either compiled into the images or never shipped (section 7).

## 2. Docker images

All four are listed in `build.json` (`DeploymentProjectSettingsList`). Build context is the repo
root for every image.

| Image | Source | Dockerfile | Role | Instances | Exposure |
|---|---|---|---|---|---|
| `dengage.loyalty.api` | `src/dEngage.Loyalty.Api` | `src/dEngage.Loyalty.Api/Dockerfile` | Admin API used by the portal (JWT and API keys) | 1 or more | Internal, reached through the frontend |
| `dengage.loyalty.public.api` | `src/dEngage.Loyalty.Public.Api` | `src/dEngage.Loyalty.Public.Api/Dockerfile` | Event ingestion API for integrators (API keys only) | 1 or more | Public (integrators) |
| `dengage.loyalty.consumer` | `src/dEngage.Loyalty.Consumer` | `src/dEngage.Loyalty.Consumer/Dockerfile` | Event worker (RabbitMQ) and nightly jobs | **exactly 1** | Cluster-internal only |
| `dengage.loyalty.frontend` | `webVue3` | `dengage.loyalty.frontend/Dockerfile` | Vue admin portal behind OpenResty/nginx | 1 or more | Public (admin users) |

**Exactly one Consumer.** It processes events one at a time to keep each customer's events in
order, and it runs the nightly jobs (tier downgrade, points expiry, …), which have no leader
election yet. A second replica would break ordering and run every job twice.

The Angular portal (`web/`) is **not** deployed: no image builds it and `.dockerignore` excludes it.

### 2.1 What each image contains

| Internal library | api | public.api | consumer |
|---|:-:|:-:|:-:|
| `Shared`, `Schema`, `Contract` | ✅ | ✅ | ✅ |
| `Api.Framework` | ✅ | ✅ | — |
| `Ledger`, `RuleEngine`, `Engine.Framework` | ✅ | — | ✅ |

The frontend image contains only the built portal (HTML, JS, CSS) and nginx.

## 3. Infrastructure the services need

| Dependency | Used by | Notes |
|---|---|---|
| PostgreSQL | api, public.api, consumer | One loyalty database. Schema owned by this repo (section 5). |
| Redis | api, public.api, consumer | Rule/config cache, limit counters, rate limiting. |
| RabbitMQ | api, public.api, consumer | Incoming: exchange `loyalty.events`, queue `q.loyalty`, dead-letter exchange `loyalty.events.dlx` and queue `q.loyalty.dlq`, optional per-tenant queues `q.loyalty.tenant.<slug>`. Outgoing (for other services): exchange `loyalty.outbound`. Declared by the services at startup. |
| Graylog | api, public.api, consumer | GELF over UDP; endpoint in `appsettings.logging.json` (`graylogEndPoint`). |

## 4. Configuration

### 4.1 Backend services (api, public.api, consumer)

Platform hosting (`dEngage.Common.Hosting`): the deployment mounts the environment's
`appsettings*.json` files into a folder and points `CONFIG_PATH` at it.

| Environment variable | Required | Meaning |
|---|---|---|
| `CONFIG_PATH` | yes | Folder with the mounted `appsettings*.json` files. |
| `NLOG_CONFIG_PATH` | no | Folder of `appsettings.logging.json`, if not in `CONFIG_PATH`. |
| `LOYALTY_MASTER_KEY_FILE` | if any value uses `ENC(...)` | Path to a mounted secret file holding the key that decrypts `ENC(...)` values (create values with `dEngage.Loyalty.CryptoCli`). |

Files to mount in `CONFIG_PATH`:

| File | Content |
|---|---|
| `appsettings.json` | Service settings (below). |
| `appsettings.messagebroker.json` | RabbitMQ connection. |
| `appsettings.logging.json` | NLog targets; set `graylogEndPoint` for the environment. |
| `appsettings.metrics.json` | App.Metrics (Prometheus) — `Enabled: false` unless DevOps scrapes it. |

Settings per service (secrets are marked; never commit their values):

| Key | api | public.api | consumer | Secret |
|---|:-:|:-:|:-:|:-:|
| `ConnectionString` (PostgreSQL) | ✅ | ✅ | ✅ | ✅ |
| `RedisConnectionString` | ✅ | ✅ | ✅ | ✅ |
| `RabbitMQSettings:RabbitMQHost`, `RabbitMQPort`, `RabbitMQUsername`, `RabbitMQPassword`, `VirtualHost`, `UseSsl` | ✅ | ✅ | ✅ | password |
| `Jwt:SigningKey` | ✅ | — | — | ✅ |
| `Cors:AllowedOrigins` | ✅ | — | — | |
| `RabbitMq:GenericEventTypes` (allowed tenant-defined event types) | ✅ | ✅ | ✅ | |
| `RabbitMq:IsolatedTenants` (per-tenant queues) | — | — | ✅ | |
| `SwaggerSettings:IsActive` | optional (default off) | optional (default off) | — | |

All three fail at startup when a required value is missing.

Ports and health:

| Service | Port | Health |
|---|---|---|
| api | 8080 | `GET /health` (liveness), `GET /health/ready` (PostgreSQL, Redis, RabbitMQ) |
| public.api | 8080 | `GET /health`, `GET /health/ready` |
| consumer | 5174 (Kestrel, internal) | `GET /health-check` |

### 4.2 Frontend (`dengage.loyalty.frontend`)

One image for every environment; settings come from the environment at start-up (same model as
the dEngage panel, `adminpanel`). Full details: `webVue3/README.md` → "Deployment".

| Environment variable | Default | Meaning |
|---|---|---|
| `BACKEND_URL` | `http://127.0.0.1:8080` | The admin Api (`dengage.loyalty.api`), scheme, host and port only. `/api/*` and `/ping` are forwarded there. |
| `LISTEN_PORT` | `8080` | nginx port. |
| `HOST_NAME_LIST` | any host | Host names to serve; any other host gets no answer (444). |
| `LOG_DIRECTORY` / `OUTPUT_DIRECTORY` | `/dengage/log/dengage.loyalty.frontend` | nginx logs, rotated by size. |

Optional mounts in `/dengage/config`: `loyalty-frontend.app-config.json` (portal feature flags and
page sizes) and `loyalty-frontend.{server,http,global}-extra.conf` (nginx additions). Health:
`GET /ping` (answers with the Api's `/health`).

The portal needs **its own host name** (for example `loyalty.<env>.dengage.com`): both this portal
and the dEngage panel send `/api/*` to their own backend.

## 5. Database migrations

None of the services migrate the database at startup. Each release runs the idempotent script
on the loyalty database **before** the new images start:

```bash
psql "<loyalty database>" -f scripts/loyalty_schema.sql
```

The script is generated with `dotnet ef migrations script --idempotent -p src/dEngage.Loyalty.Schema`
and only applies migrations the database doesn't have yet. Schema changes are kept
backward compatible for one release, so instances still running the old version keep working
during the rollout.

## 6. Release order and what to redeploy

Order for a release: **migration script → consumer → api and public.api → frontend.**

| Change in | Redeploy |
|---|---|
| `webVue3` only | frontend |
| `Consumer` only | consumer |
| `Ledger`, `RuleEngine`, `Engine.Framework` | consumer, api |
| `Api.Framework` | api, public.api |
| `Shared`, `Schema`, `Contract` | api, public.api, consumer |
| A new migration | migration script first, then the services above |

## 7. NuGet packages (private feed)

Published to the existing dEngage private feed — the same feed as `dEngage.Models`,
`dEngage.Parameters` and `dEngage.Framework`:

```
https://nugetint.dengage.com/repository/dengage-nuget/
```

| Package | Target | Depends on | For |
|---|---|---|---|
| `dEngage.Loyalty.Contract` | netstandard2.0 | `System.Text.Json` | Any dEngage service that sends or reads loyalty events and API models. |
| `dEngage.Loyalty.Api.Client` | netstandard2.0 | `dEngage.Loyalty.Contract`, `Microsoft.Extensions.Http` | Any dEngage service that calls the loyalty API (`AddLoyaltyApiClient(...)`, an API key per tenant). |

Both are listed in `build.json` (`NugetPackageSettingsList`) and are published together with the
same version. Consumers reference them like any platform package:

```xml
<PackageReference Include="dEngage.Loyalty.Api.Client" Version="1.0.*" />
```

**Versioning (to add).** `dengage.models`, `dengage.parameters`, `dengage.framework`,
`dengage.common` and `panel-back-end` declare their version scheme in `build.json`; loyalty's
entries have only `Name` so far. To follow them:

```json
{ "Name": "dEngage.Loyalty.Contract", "Major": 1, "Minor": 0,
  "AutoIncrementPackageVersion": true, "AddSuffixTimeIfNotMasterEnvironment": true },
{ "Name": "dEngage.Loyalty.Api.Client", "Major": 1, "Minor": 0,
  "AutoIncrementPackageVersion": true, "AddSuffixTimeIfNotMasterEnvironment": true }
```

The patch number then increments automatically, and builds from branches other than master get a
time suffix, so test packages never replace released ones.

### Not published

The internal libraries (`Shared`, `Schema`, `Ledger`, `RuleEngine`, `Engine.Framework`,
`Api.Framework`) are used only by this repository's own images, which compile them from source.
They are deliberately not on the feed: other services must not write the loyalty database or
ledger directly; they go through the API (`Api.Client`) or events (`Contract`).

## 8. Not deployed

| Project | Why |
|---|---|
| `dEngage.Loyalty.CryptoCli` | Local tool that creates `ENC(...)` values. |
| `dEngage.Loyalty.TestCli` | Local test tool. |
| `dEngage.Loyalty.Engine.Tests`, `dEngage.Loyalty.IntegrationTests` | Run in CI only. |
| `web/` (Angular portal) | Replaced by `webVue3`; never built into an image. |

## 9. DevOps checklist

1. **Repository and pipeline.** Move the repository to Bitbucket and attach the shared pipeline
   (`bitbucket-pipelines.yml` is already in place).
2. **Dockerfile location.** Other dEngage repos keep `<Name>/Dockerfile` at the repo root; the
   three backend Dockerfiles here are under `src/<Name>/Dockerfile`. Confirm the shared pipeline
   finds them, or tell us to move them.
3. **Registry and feed access.** Allow the pipeline to push the 4 images to the registry and the
   2 packages to the NuGet feed.
4. **Infrastructure.** A PostgreSQL database, Redis, a RabbitMQ virtual host and user, and the
   Graylog endpoint per environment.
5. **Config and secrets.** Mount the `appsettings*.json` files per service (section 4.1), the
   `ENC(...)` key file if used, and the frontend's optional `app-config.json`.
6. **Deployments.** api and public.api: 1 or more replicas; consumer: **1 replica**; frontend: 1 or
   more replicas with `BACKEND_URL` pointing to the api service.
7. **Host names.** One for the portal (frontend) and one for the public ingestion API.
8. **Release step.** Run `scripts/loyalty_schema.sql` before rolling out new images.

### Open decision before production

Deploying only the Vue portal retires the Angular portal. That is decision O3 of the Vue 3
rewrite (`docs/scope-changes/2026-10-12-vue3-parity-checklist.md`) and still needs Product Owner
and Architect sign-off.
