---
paths:
  - "web/**"
---

# Frontend rules — Loyalty Admin Portal (`web/`, Vue 3)

`web/CLAUDE.md` has the overview and commands. These are the rules to apply when editing `web/`.

## Stack (don't swap without asking)
- Vue 3 (`<script setup lang="ts">`), Vue Router, Pinia, TanStack Vue Query, **shadcn-vue**
  components on **Reka UI**, styled with **Tailwind v4** (lucide icons, vue-sonner toasts),
  VeeValidate + Zod, vue-i18n, `openapi-fetch` + `openapi-typescript`. Vite, Vitest, ESLint (with
  `eslint-plugin-boundaries`), Prettier. TypeScript **5.9**: the `openapi-typescript` peer range
  doesn't accept TypeScript 6 yet.
- Don't add npm packages without being asked. Pin exact versions (`--save-exact`).

## Feature-Sliced Design (lint-enforced)
Layers, top to bottom: `app → pages → widgets → features → entities → shared`.
- A file imports only from **lower** layers, or from **its own slice**. Two slices on the same
  layer never import each other. If they need the same thing, move it down a layer.
- Another slice is reached **only through its `index.ts`**. Don't deep-import `…/model/types`.
- `app/` holds bootstrap, providers, router, guards and layouts. `pages/<route>/` holds route
  containers. `widgets/<block>/` holds composite blocks (sidebar, topbar). `features/<action>/` holds
  one user action each (login, publish a program). `entities/<domain>/` holds domain slices
  (program, tenant, account-type). `shared/` holds api, ui, lib, model, config and i18n.
- Slice segments: `api/` (request functions + query/mutation composables), `model/` (types, Zod
  schemas, mappers, composables), `ui/` (components). Reference slice to copy: `entities/program`
  + `features/program-create` + `pages/programs-list`.
- Specs (`*.spec.ts`) are outside the boundary rule, so they can mock a slice's `api` seam.

## Components: containers vs presentational
- **Containers** are `pages/*/ui/*Page.vue` and `widgets/*/ui/*`. They call **one composable**
  (`model/use*.ts`) and render. They hold no business logic in the template or the script.
- **Presentational** components are `entities/*/ui`, `features/*/ui` and `shared/ui`. They take
  props, use `defineModel` for form fields, and emit events. Lint bans Vue Query, Pinia, `@/shared/model`
  and `*/api` imports there.
- Logic lives in composables: `features/<x>/model/use<X>.ts` and `pages/<x>/model/use<X>Page.ts`.
- A page with several data-loading parts used only there (the Customer 360 tabs and its drawer)
  keeps them in its own slice: `pages/<x>/ui/tabs/*.vue`, each calling one composable from
  `pages/<x>/model/`. They mount only when shown, so their queries load lazily.

## API layer (lint-enforced)
- Only `shared/api/client.ts` creates the HTTP client. `fetch`, `axios` and `openapi-fetch` are
  banned everywhere else. The one exception is `shared/config/app-config.ts`, which reads `app-config.json`.
- Request functions live in `entities/<x>/api/<x>.api.ts` (or a feature's `api/`). They take
  `tenantId` as an argument and call `apiClient().GET('/api/v1/…', …)` with the **generated path
  string**, so the URL, parameters and body are type-checked. Wrap them in `unwrap(...)`.
- Pass `withFlags({ skipErrorToast: true })` when the caller shows the error itself (form
  submits, actions that toast their own message). Otherwise the client toasts it.
- Failures are always thrown as `ApiError` (`shared/api/api-error.ts`). A 401
  ends the session via the hook wired in `app/providers`.
- **Stores never call the API** (lint bans it in `*.store.ts`).

## Server state vs client state
- **Vue Query owns server data.** Query keys come from a per-entity factory (`programKeys`) and
  **always start with the tenant id**, so switching tenants never shows another tenant's cache
  (guardrails §2, Tenancy). Lists use `placeholderData: keepPreviousData`. Mutations update the
  detail with `setQueryData` and invalidate the lists. Logout and a 401 call `queryClient.clear()`.
- **Pinia owns client state only:** `shared/model` holds `useSessionStore` (token → principal) and
  `useTenantContextStore` (active tenant id), plus `useLocaleStore` (the portal's language). Never put API responses in a store. The open program
  comes from the route + `useProgramQuery`, not from a store.
- Retries: only transient failures (0/502/503/504) on queries, once. Mutations never retry.
- Cursor-paged lists (Customer 360) put the page's cursor in the query key and record the next
  one from each loaded page (`CursorPager`, `entities/customer`). Never record placeholder data —
  it is the previous page's. A filter bar keeps a draft and an applied copy; Search applies it.

## Generated API types (OpenAPI)
- The backend publishes no OpenAPI spec of its own. `npm run api:spec` builds
  `openapi/openapi.json` by **reading the C# source** (`*Dtos.cs`, `*Controller.cs`, `*AppService.cs`,
  the engine models they use). `npm run api:types` writes `src/shared/api/generated/schema.d.ts`.
  Both files are generated. **Never edit them by hand.**
- The committed `openapi.json` is the spec the portal is pinned to: the portal builds from it with
  no backend source, as the split's `dengage.loyalty.frontend` does. `npm run api:sync [-- <path-or-url>]`
  pins another spec (default: `../dengage.loyalty.api/openapi/openapi.json`, next to this
  repository), regenerates the types and records the source in `openapi/SPEC_SOURCE`; commit all three.
- `npm run api:check` (also before every build) fails when `schema.d.ts` doesn't match the pinned
  spec. While the backend is in this repository, `npm run api:check:backend` also fails when the
  spec is behind the C# source. After any backend DTO or route change, run `api:spec` then
  `api:types` and commit both with the change.
- Both generated files are LF whatever the checkout (`web/.gitattributes`), because `api:check`
  compares them byte for byte.
- Mapping rules (`tools/openapi/build-spec.ts`): `decimal` → decimal string, `Guid` → uuid,
  `DateTime` → date-time, nullable → optional (the API omits nulls: `WhenWritingNull`),
  `[JsonPropertyName]` wins over camelCase, enums → integers (no `JsonStringEnumConverter`).
- What C# can't express (allowed values of `string` statuses, excluded modules) goes in
  `openapi/overrides.ts`, **each entry naming its C# source**. An override that no longer matches
  a member fails `api:spec` and `api:check:backend`. The generator fails loudly rather than guessing. Fix it or add an override.
- App code uses `ApiSchema<'ProgramResponse'>` (re-exported as entity types). It never hand-writes DTO shapes.

## Forms (VeeValidate + Zod)
- A Zod schema per form in `entities/<x>/model/*.schema.ts`, built by a function so messages are
  translated (`translate(...)`). It validates what the admin types (blank strings). A typed
  mapper (`to<X>Request`) turns values into the **generated** request type, so a DTO change
  breaks the build there.
- The composable owns `useForm({ validationSchema: toTypedSchema(...) })`. Small forms (a few
  fields) use `defineField`, and the presentational form takes `v-model:<field>` plus `errors`
  (`features/program-create`). Large forms take `values` + `errors` and emit `set(field, value)`
  typed by `FieldSetter<FormValues>` (`shared/lib/forms`), which the composable wires to
  `setFieldValue` (`features/account-type-edit`, `tier-edit`, `reward-edit`).
- Numeric fields stay **strings** in the form (`AppInput inputmode="numeric|decimal"`). The schema
  checks the text; the mapper converts. Money and points stay decimal strings on the wire. A
  free-form `jsonb` config whose C# validator reads JSON numbers gets numbers (account-type config).
- Validation that depends on the open item (a name prefix, a grandfathered name) uses a
  `computed` schema: `validationSchema: computed(() => toTypedSchema(schema(context.value)))`.
- Dialog forms expose `open(existing | null, context)`, which resets the form, and `visible`. The
  page renders `AppDialog` + the presentational form and passes the callbacks.
- Page forms (rules, streak campaigns, card buckets: routes `…/new` and `…/:id`, `new` listed
  first) take the id (null = new) and an `onSaved` callback, and fill the form **once**, when
  everything it needs has loaded (`ready`) — a later refetch must not overwrite what the admin is
  typing. Copy loaded JSON with `cloneJson` (`shared/lib/forms`), never `structuredClone`: Vue
  Query data can be a reactive proxy, which `structuredClone` refuses.
- A field whose change needs a confirmation or side effects (the rule trigger) is intercepted in
  the composable's `set`, not with a watcher that has to revert the value.
- Server errors: `mapServerErrors(error, FIELDS, fallback)` → `setErrors(fields)` and a form-level
  `AppFormErrors` list for the rest.
- Mirror the backend validator's limits (lengths, ranges) in the Zod schema, so the form rejects
  what the API would.

## UI kit only behind `shared/ui` (lint-enforced)
- `shared/ui/primitives/` holds the shadcn-vue components, generated by the CLI from
  `components.json`: `npx shadcn-vue@2.8.2 add <name>`. It's owned code but kept as generated (no
  hand edits beyond theming), so the CLI can refresh it. `shared/ui/lib/utils.ts` holds `cn()`.
- `reka-ui`, `vue-sonner`, `@lucide/vue`, `class-variance-authority`, `clsx`, `tailwind-merge`,
  `primitives/` and `lib/` are imported **only** in `src/shared/ui/**`. Everything else uses the
  `App*` wrappers from `@/shared/ui`.
- Wrappers expose **our** props (`variant`, `pending`, `tone`…), never a primitive's, so swapping
  a primitive means changing the wrapper only. Add a primitive + wrapper before using a new component.
- Icons only through `AppIcon name="…"` (add the lucide icon to its map). Styling: Tailwind
  utilities and the theme variables in `app/styles/global.css` (shadcn variables mapped onto the
  `--app-*` brand tokens); component-scoped CSS uses the `--app-*` tokens.
- Toasts go through `notify.*` (works outside components). Confirmations go through `useAppConfirm().ask()`.

## i18n
- vue-i18n, English and Turkish (`shared/i18n/locales/en.json`, `tr.json`). Every new key goes in
  **both** files, with the same `{x}` placeholders (`locales.spec.ts` fails otherwise). Keys are
  dotted and grouped by screen (`programs.list.col.name`); `@ | { } $` are escaped.
- The active language lives in `useLocaleStore` (`shared/model`). Format dates and numbers with
  `intlLocale()` from `@/shared/i18n`, never the browser's locale (`undefined`). A form schema whose
  messages must follow a language switch while it's open is a `computed`.
- JS upper-casing of translated text uses `toLocaleUpperCase(intlLocale())` (Turkish i → İ).
- `vue/no-bare-strings-in-template` is on. Every user-facing string is a key. Outside components
  use `translate(key, params)`.

## Contracts carried over (guardrails §2)
- Money and points stay `DecimalString` (`shared/lib/money`). `formatMoney` is for display only.
  big.js is for live preview only.
- UTC ISO timestamps (`shared/lib/date`). Streak timezones are explicit IANA ids.
- DSL parity: the condition and streak validators in `shared/lib/dsl/` mirror the C# engine, with
  their table tests. Change them only together with the C# engine
  (`GroupedConditionDsl.cs`, `ConditionDsl`, `StreakConfig.Validate`). The frontend must never accept
  what the engine rejects. The condition editors are the `entities/condition` slice (rules and card
  buckets use the grouped tree / leaf list, streak campaigns the flat clause list).

## Before calling work done (from `web/`)
`npm run api:check` · `npm run type-check` · `npm run lint` · `npm test` · `npm run build`, and
`npx prettier --write` on changed files. Every screen gets a page spec that mocks only the
entity `api/*.api.ts` functions (`vi.mock`) and mounts with `tests/utils/mount-with-providers.ts`
(see `pages/tiers/ui/TiersPage.spec.ts`); assert user-visible behaviour, and wait for VeeValidate's
async validation with `vi.waitFor`. `tests/setup.ts` stubs the browser APIs jsdom lacks and the UI kit
calls (`matchMedia`, `ResizeObserver`, pointer capture, `scrollIntoView`); dialogs render in a
portal on `document.body`, so query them there (`[role="dialog"]`, `[role="alertdialog"]`).
A new string field backed by a C# constant needs an `openapi/overrides.ts` entry (then `api:spec` +
`api:types`).

End to end: `npm run e2e` (Playwright, reuses the dev server on `:4201`; `PW_CHANNEL=msedge` for an
installed browser). `e2e/*.spec.ts` answer `/api` with `e2e/mock-api.ts` and must pass without the
backend, including their axe checks (no critical or serious WCAG 2 A/AA violations; keep text
colours at 4.5:1). `e2e/*.e2e.spec.ts` drive the real stack and run only with `E2E_BACKEND=1`. Drive
selects with `pick(page, id, option)` from `e2e/fixtures.ts`, and wait a toast out before clicking a
page-header action (toasts sit over it).

## Don't
- Don't import the UI kit, `fetch` or `openapi-fetch` outside their layer, or deep-import another slice.
- Don't put server data in Pinia, or call the API from a store or a presentational component.
- Don't hand-write a DTO type or edit `generated/` or `openapi.json`.
