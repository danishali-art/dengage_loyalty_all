# Loyalty Admin Portal

The admin portal for the loyalty platform (Vue 3).
Architecture and conventions: [CLAUDE.md](CLAUDE.md) and [../.claude/rules/frontend.md](../.claude/rules/frontend.md).

## Run it

Node 24 (npm 11). From this folder:

```bash
npm install
npm run dev        # http://localhost:4201
```

The API calls go to `/api`, which Vite proxies to the backend at `http://localhost:5173` — start
it with `..\run.ps1` (or the Api project alone). Without a backend the portal loads, but sign-in
fails with an error from the proxy.

## Checks

```bash
npm run api:check && npm run type-check && npm run lint && npm test && npm run build
```

## End-to-end tests (Playwright)

```bash
npx playwright install chromium       # once, or set PW_CHANNEL=msedge to use an installed Edge
npm run e2e                           # shell + platform flows, /api mocked in the browser
E2E_BACKEND=1 npm run e2e             # also the real-backend flows (Api on :5173; customers needs the Consumer)
```

They reuse a dev server already running on `:4201`, or start one.

## Adding a UI component

The UI kit is [shadcn-vue](https://www.shadcn-vue.com) (Reka UI + Tailwind v4). Generate the
primitive into `src/shared/ui/primitives/`, then wrap it in an `App*` component in `src/shared/ui`:

```bash
npx shadcn-vue@2.8.2 add <component>
```

Pages and features use the wrappers only (lint enforces it).

## After a backend DTO or route change

```bash
npm run api:spec && npm run api:types
```

Commit `openapi/openapi.json` and `src/shared/api/generated/schema.d.ts` with the change. The build
(and `npm run api:check`) fails until they match the C# source.

## Deployment (image `dengage.loyalty.frontend`)

Packaged like the dEngage panel (`adminpanel`): [`../dengage.loyalty.frontend/`](../dengage.loyalty.frontend/)
holds the Dockerfile, the nginx template and the start-up scripts, and `build.json` registers the
image for the shared Bitbucket pipeline. Build context is the repo root, because the build's
`api:check` reads the backend source in `src/`:

```bash
docker build -t dengage.loyalty.frontend -f dengage.loyalty.frontend/Dockerfile .
docker run -p 8090:8080 -e BACKEND_URL=http://host.docker.internal:5173 dengage.loyalty.frontend
```

One image serves every environment. OpenResty serves the build and forwards `/api/*` to the Api,
so the browser only ever calls its own origin (no CORS, no API address in the bundle).

| Environment variable | Default | Meaning |
|---|---|---|
| `BACKEND_URL` | `http://127.0.0.1:8080` | The admin Api (`dengage.loyalty.api`), scheme, host and port only. `/api/*` and `/ping` go there. |
| `LISTEN_PORT` | `8080` | Port nginx listens on. |
| `HOST_NAME_LIST` | any host | Space-separated host names to serve; other hosts get no answer (444), except `/ping`. |
| `LOG_DIRECTORY` | `/dengage/log/dengage.loyalty.frontend` | `error.log`, `api.log` (API requests, no auth headers), `rejected_access.log`. Rotated by size. |
| `OUTPUT_DIRECTORY` | `LOG_DIRECTORY` | Where rotated logs go. |

Optional files mounted at `/dengage/config` (as the panel's `adminpanel-frontend.*`):
`loyalty-frontend.app-config.json` replaces `app-config.json` (feature flags, page sizes) at
start-up, and `loyalty-frontend.{server,http,global}-extra.conf` are included into the nginx
config. `/dengage/startup-scripts/loyalty-frontend.sh` runs before nginx starts.

`/ping` answers with the Api's `/health`. `index.html` and `app-config.json` are never cached;
the hashed files under `/assets/` are cached for a year.

Serve the portal on its own host name (for example `loyalty.<env>.dengage.com`): both this portal
and the dEngage panel send `/api/*` to their own backend.
