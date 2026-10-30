# CLAUDE.md — Loyalty Admin Portal (`web/`, Vue 3)

The admin portal for the loyalty platform. Rules to follow when editing: `../.claude/rules/frontend.md`.

## Commands (run from `web/`)

```bash
npm run dev          # Vite on :4201, /api proxied to http://localhost:5173
npm run build        # api:check, then vue-tsc + vite build
npm run type-check   # vue-tsc
npm run lint         # ESLint, including the architecture rules
npm test             # Vitest (unit, component, generator)
npm run api:spec     # regenerate openapi/openapi.json from the backend C# source
npm run api:types    # regenerate src/shared/api/generated/schema.d.ts from the spec
npm run api:check    # fail if either generated file is out of date
npm run e2e          # Playwright (reuses the dev server on :4201); PW_CHANNEL=msedge for an installed browser
E2E_BACKEND=1 npm run e2e   # also the *.e2e.spec.ts real-backend flows (Api on :5173; customers needs the Consumer)
```

## Architecture — Feature-Sliced Design

```
src/
  app/       main.ts, App.vue, providers/ (Pinia, Vue Query, i18n, API hooks),
             router/ (routes, guards), layouts/ShellLayout.vue, styles/global.css (Tailwind + theme tokens)
  pages/     route containers: login, select-tenant, programs-list, program-overview, account-types,
             tiers, rewards, program-history, rules, rule-edit, streak-campaigns, streak-campaign-edit,
             card-buckets, card-bucket-edit, dashboard, customers, customer-detail (Customer 360: its
             tabs and the event drawer live in this page slice), event-simulator, platform-tenants,
             platform-tenant-overview, platform-api-keys, platform-admin-users, status
  widgets/   app-sidebar (nav + program / tenant sub-nav), app-topbar (tenant switcher, user menu)
  features/  auth-login, auth-logout, tenant-switch, program-create, program-edit-info, program-lifecycle,
             account-type-edit, tier-edit, tier-actions, reward-edit, reward-approve, rule-edit,
             rule-actions, streak-campaign-edit, streak-campaign-actions, card-bucket-edit,
             card-bucket-actions, tenant-create, tenant-status, api-key-manage, admin-user-create
  entities/  program, account-type, tier, reward, config-version, tenant, rule, streak-campaign,
             card-bucket, event-type, condition (the condition editors), customer, dashboard, event,
             api-key, admin-user —
             api/ (requests + queries),
             model/ (types, schemas, mappers), ui/
  shared/    api/ (typed client, ApiError, query client, generated types), ui/ (App* wrappers; primitives/ = shadcn-vue),
             lib/ (money, date, forms, loading, dsl = condition + streak DSL mirroring the C# engine), model/ (session + tenant stores), config/, i18n/
tools/       openapi/ (C# → OpenAPI generator, drift check)
openapi/     openapi.json (generated, committed), overrides.ts (hand-kept, each entry sourced)
tests/       setup.ts (jsdom polyfills), utils/mount-with-providers.ts (component-test helper)
e2e/         Playwright: *.spec.ts run against mock-api.ts (no backend); *.e2e.spec.ts against the
             real backend (skipped unless E2E_BACKEND=1); fixtures.ts (loginAs, pick, provisionTenant)
```

- Layers import downwards only; another slice only through its `index.ts`; same-layer slices
  never import each other (`eslint-plugin-boundaries`).
- **Containers** (pages, widgets) call one composable; **presentational** components (entities/
  features/shared `ui/`) take props and emit events.
- **HTTP** only in `shared/api/client.ts` (`openapi-fetch` + middleware for auth, tenant header,
  correlation id, error normalisation and loading). **Server state** in Vue Query (tenant-scoped
  keys); **client state** in Pinia.
- **UI kit:** shadcn-vue components on Reka UI, styled with Tailwind v4, generated into
  `shared/ui/primitives/` (`components.json`) and used only through the `shared/ui` App* wrappers.
  Icons (lucide) only through `AppIcon`.
- **Types** come from the generated OpenAPI types (`ApiSchema<'ProgramResponse'>`), never hand-written.

## Behaviour notes

- Sign-in is always `POST /auth/login`.
- English and Turkish (CR 2026-10-28). The language is picked only in the user menu's
  **Language** submenu and saved per browser (`useLocaleStore`, `loyalty.portal.locale`). It's
  English until changed (the browser's language is never used), and the login page shows the
  saved language without offering a picker. `<html lang>` follows it. Dates and numbers are formatted in it through
  `intlLocale()` (display only). Messages sent by the API stay English.
- Form limits follow the backend validators: account type name ≤ 100; a reward name including its
  slug prefix ≤ 100; admin email ≤ 255 (`PlatformValidators`); a rule's rate / amount and a card
  bucket's reward amount > 0, and a rule's priority a whole number (`RulesValidators` /
  `CardBucketsValidators`).
- Numeric fields (tier minimum points, rule amounts, the redeem / transfer pre-fill from the wallet)
  are decimal-string text fields. An account type's Type can't be changed after creation. A tier
  move waits for the server instead of reordering optimistically.
- The new-rule form only sends hold days with Delayed posting. A condition field is flagged as
  undeclared only when the trigger declares its fields. Rule, streak and bucket forms show the
  server's field errors on their fields.
- `/` opens the dashboard. Customer filters default to the last 30 days (local time sent as UTC);
  page sizes are 10 / 20 / 30. The Customer 360 tabs are a tab list (arrow keys move between tabs);
  the event drawer is a dialog docked to the right.
- The new API key's dialog forgets the raw key however it is closed (Escape and outside click too).
- Text colours pass WCAG AA, which the e2e axe checks enforce. Toasts sit below the topbar, over
  the page header's actions — e2e specs wait a toast out before the next header click.
