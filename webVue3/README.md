# Loyalty Admin Portal — Vue 3

The Vue 3 version of the admin portal, built in phases beside the Angular portal in `../web/`.
Architecture and conventions: [CLAUDE.md](CLAUDE.md) and [../.claude/rules/frontend-vue.md](../.claude/rules/frontend-vue.md).

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
