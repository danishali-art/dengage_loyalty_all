# CLAUDE.md — Loyalty Admin Portal, Vue 3 (`webVue3/`)

The Vue 3 rewrite of the Angular admin portal (`../web/`), delivered in phases **beside** it
(scope change `../docs/scope-changes/2026-10-12-vue3-portal-rewrite.md`). The Angular portal stays
the live one until parity is signed off. Rules to follow when editing: `../.claude/rules/frontend-vue.md`.

## Commands (run from `webVue3/`)

```bash
npm run dev          # Vite on :4201, /api proxied to http://localhost:5173 (same backend as web/)
npm run build        # api:check, then vue-tsc + vite build
npm run type-check   # vue-tsc
npm run lint         # ESLint, including the architecture rules
npm test             # Vitest (unit, component, generator)
npm run api:spec     # regenerate openapi/openapi.json from the backend C# source
npm run api:types    # regenerate src/shared/api/generated/schema.d.ts from the spec
npm run api:check    # fail if either generated file is out of date
npm run e2e          # Playwright (reuses the dev server on :4201); PW_CHANNEL=msedge for an installed browser
E2E_BACKEND=1 npm run e2e   # also the *.e2e.spec.ts ports of web/e2e (Api on :5173; customers needs the Consumer)
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
             lib/ (money, date, forms, loading, dsl = condition + streak DSL ported verbatim), model/ (session + tenant stores), config/, i18n/
tools/       openapi/ (C# → OpenAPI generator, drift check), i18n/ (Angular locale importer)
openapi/     openapi.json (generated, committed), overrides.ts (hand-kept, each entry sourced)
tests/       setup.ts (jsdom polyfills), utils/mount-with-providers.ts (component-test helper)
e2e/         Playwright: *.spec.ts run against mock-api.ts (no backend); *.e2e.spec.ts against the
             real backend (skipped unless E2E_BACKEND=1); fixtures.ts (loginAs, pick, provisionTenant)
```

- Layers import downwards only; another slice only through its `index.ts`; same-layer slices
  never import each other (`eslint-plugin-boundaries`).
- **Containers** (pages, widgets) call one composable; **presentational** components (entities/
  features/shared `ui/`) take props and emit events.
- **HTTP** only in `shared/api/client.ts` (`openapi-fetch` + middleware ported from the Angular
  interceptors). **Server state** in Vue Query (tenant-scoped keys); **client state** in Pinia.
- **UI kit:** shadcn-vue components on Reka UI, styled with Tailwind v4, generated into
  `shared/ui/primitives/` (`components.json`) and used only through the `shared/ui` App* wrappers.
  Icons (lucide) only through `AppIcon`.
- **Types** come from the generated OpenAPI types (`ApiSchema<'ProgramResponse'>`), never hand-written.

## Phase status

| Phase | Scope | Status |
|---|---|---|
| 0 | Foundation, OpenAPI types, auth, shell, tenant picker, programs list / create / overview | Done |
| 1 | Account types, tiers (reorder, delete guard), rewards (approve, retired view-only), program history | Done |
| 2 | Rules (grouped list, builder, condition tree), streak campaigns, card buckets | Done |
| 3 | Customers + Customer 360, dashboard, event simulator | Done |
| 4 | Platform (tenants, API keys, admins), e2e, parity checklist | Done; sign-off pending |

Every Angular screen has a Vue port. Sign-off follows
`../docs/scope-changes/2026-10-12-vue3-parity-checklist.md`; the real-backend e2e ports haven't run yet.

## Differences from `web/` (deliberate)

- Angular's `stub` auth mode isn't ported; sign-in is always `POST /auth/login`.
- The topbar's search box (non-functional in Angular too) is left out.
- Roboto isn't bundled (Angular self-hosts it via `@fontsource`); the same font stack falls back.
- English only; `tr` comes later through `tools/i18n/import-angular-locale.ts`.
- Phase 1: name lengths follow the backend validators (account type ≤ 100, where Angular allowed
  255; a reward name incl. its slug prefix ≤ 100); tier minimum points is a decimal-string text field
  (Angular used a number input); the account-type Type select is disabled on edit (Angular only
  showed a hint); a tier move waits for the server instead of reordering optimistically.
- Phase 2: a rule's rate / amount and a card bucket's reward amount must be greater than 0, and a
  rule's priority a whole number (`RulesValidators` / `CardBucketsValidators`; Angular accepted 0
  and decimals and left it to the API). Numeric fields are decimal-string text fields, so the
  redeem / transfer pre-fill from the wallet is a string too. The new-rule form only sends hold days
  with Delayed posting. A condition field is flagged as undeclared only when the trigger declares
  its fields (Angular flagged every field on card buckets and tenant-defined triggers). Rule, streak
  and bucket forms show the server's field errors on their fields instead of only in the summary.
- Phase 3: the dashboard's program filter and the customer filters use the shared searchable /
  plain selects instead of native selects; the Customer 360 tabs are a Reka tab list (arrow keys
  move between tabs). The event drawer is a dialog docked to the right with the same sections.
  Behaviour is unchanged: the same endpoints, filters (last 30 days by default, local time sent
  as UTC) and page sizes (10 / 20 / 30). `/` opens the dashboard, as in `web/`.
- Phase 4: the new API key's dialog forgets the raw key however it is closed (Escape and outside
  click too, not only Done); the admin form also checks email ≤ 255 (`PlatformValidators`). Text
  colours are darker than `web/` (muted, success, warning, danger) to pass WCAG AA, which the e2e
  axe checks enforce. Toasts sit below the topbar, over the page header's actions — e2e specs
  wait a toast out before the next header click.
