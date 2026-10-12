# Scope Change Impact Analysis: Vue 3 rewrite of the admin portal (`webVue3/`)

**Status:** **Proposed** 2026-10-12 — **awaiting approval by the team lead** (Product Owner + Architect, step 3 of the scope-change process in `CLAUDE.md`). No implementation starts before that approval is recorded below.

**Date:** 2026-10-12
**Author:** Claude Code, at the request of Syed jamal ud din
**Type:** Stack change. It adds a second frontend in a new framework and a new set of npm packages; `00-project-guardrails.md` §3 forbids swapping frameworks or libraries "unless explicitly asked", and here the developer explicitly asked. No product capability is added, removed or changed.
**Affects:**
- `docs/SCOPE_BASELINE.md`: a new section, "Frontend (Vue 3): `webVue3/src`", with one row per feature that tracks porting status. The existing Angular rows are unchanged.
- `docs/SOW.md`: no contractual change, because the screens and behaviour are the same. If the PO treats the stack as contractual, add a note to §2.8.
- The docs listed in §8, in the same change as the code.
- **Not affected:** the backend (`src/`), the API contract, the database schema and migrations, the Consumer, the Angular portal (`web/`) and `run.ps1`.

This document is step 2 of the scope-change process in `CLAUDE.md`. It analyses the change against `docs/SCOPE_BASELINE.md` and the as-built code. Every design decision was asked and answered before it was written down (§0).

## 0. Decisions agreed with the developer (2026-10-12)

| # | Decision |
|---|---|
| D1 | **Phased delivery.** Phase 0 builds the foundation and one vertical slice (programs). The other features follow in phases 1 to 4, and each screen is checked for parity with its Angular counterpart (§6). |
| D2 | **Side by side.** `web/` (Angular) stays the live portal and keeps getting changes. `webVue3/` runs on port **4201** against the same `/api` proxy (`http://localhost:5173`). Retiring `web/` is a separate decision after parity sign-off. |
| D3 | **UI kit and styling.** shadcn-vue components, built on Reka UI and styled with Tailwind v4, generated into `shared/ui/primitives/` by the shadcn-vue CLI. The shadcn theme variables are mapped onto the current brand tokens (`web/src/styles.css`: `--color-brand #125cfa` and the success, warn, danger and streak families). Icons come from lucide and toasts from vue-sonner. All are open source (MIT; lucide ISC). |
| D4 | **API types are generated from an OpenAPI spec, and the spec is built from the backend's C# source by a script.** The backend has no spec (§2.2), must not change, and the API and database aren't running now. The script reads `*Dtos.cs`, `*Module.cs`, `*AppService.cs` and the engine models they reference, and writes `webVue3/openapi/openapi.json`. `openapi-typescript` turns that into TypeScript types. A check script regenerates the spec and fails if it differs from the committed copy (§3.4). |
| D5 | **The stack the developer asked for** (§3.1): Vue 3 with TypeScript and Vue Router; Feature-Sliced Design organised by feature; logic in composables; container and presentational components; a separate API layer (components and stores never call `fetch` or `axios`); the UI kit wrapped in `shared/ui`; Pinia for client state; TanStack Vue Query for server state; VeeValidate with Zod for forms; vue-i18n with English first. |

## Approval record

**Pending.** To be filled in when the change is approved:

| Item | Value |
|---|---|
| Approved by (team lead / Product Owner + Architect) | |
| Date | |
| Revisions requested | |
| Answers to the open decisions (§5) | |

## 1. Why

- **Contract drift.** The Angular portal's API types are hand-written copies of the backend DTOs (`web/src/app/features/*/*.model.ts`), kept in step by convention (`frontend.md`). A renamed or retyped DTO field compiles in `web/` and fails only at runtime. Generating the types from the backend's code makes that a build error.
- **Stack preference.** The team wants the portal on Vue 3 with a stated architecture: Feature-Sliced Design, composables, a separate API layer, and library-agnostic UI wrappers.

## 2. Current state (as-built)

### 2.1 Portal (`web/`)

- Angular 22 is used with standalone components, zoneless change detection and signals. UI comes from the Angular CDK, custom components and Tailwind v4, and translations from `@ngx-translate`, with en and tr files of 497 lines each.
- The code is about 190 TypeScript files across `core/` (auth, tenant, config, http with 6 interceptors, ui services), `shared/` (21 UI components, money, date and condition DSL), `layout/` (shell, sidebar, topbar, tenant switcher) and 8 feature areas (auth, dashboard, customers, events, platform, and programs with account types, tiers, rules, rewards, card buckets, streak campaigns and history).
- Each feature's `*.service.ts` is the only file that does HTTP, through `ApiClient`.

### 2.2 Backend API (unchanged by this CR)

- The API is .NET 8, with **Nancy 2.0.0** modules hosted in ASP.NET Core (`Api.Framework.csproj`; 14 modules registered in `Api/Program.cs`).
- **No OpenAPI spec exists.** No Swashbuckle, NSwag or `Microsoft.AspNetCore.OpenApi` package is referenced, nothing calls `AddSwagger`, `UseSwagger` or `MapOpenApi`, and the repo has no spec file. The `"launchUrl": "swagger"` entries in `launchSettings.json` lead nowhere. Nancy has no maintained OpenAPI generator for .NET 8.
- The human-readable contract is `LoyaltySaaSApi.md`, which is hand-maintained.

### 2.3 Facts the OpenAPI generator relies on (checked 2026-10-12)

| Fact | Detail |
|---|---|
| DTOs are one-line positional records | 97 `sealed record`s in 13 `*Dtos.cs` files, and no classes, e.g. `TierResponse(Guid Id, string Name, decimal MinPoints, int? QualifyingPeriodDays, …)`. |
| Routes follow one pattern | There are 76 `MapGet/MapPost/MapPatch/MapDelete("…")` calls on a fixed constructor base path (`backend-api.md`). **One exception:** `AuthModule` registers `Post("/api/v1/auth/login", …)` with the full path. |
| Request and response types can be found | Bodies come from `ReadValidatedJsonBodyAsync(validator)`, or from `ReadJsonBodyAsync<T>`. Responses come from the `I<Resource>AppService` method the handler calls, e.g. `Task<PagedResult<TierResponse>> ListAsync(...)`. |
| How values are serialized | `decimal` is written as a string by `DecimalStringJsonConverter`. Names are camelCase by policy. |
| Explicit JSON names | `[JsonPropertyName]` appears in `RulesDtos.cs` and `StreakCampaignsDtos.cs`, and in 6 engine models: `RuleCalculation`, `RuleLimits`, `RuleSettings`, `ConditionTree`, `ConditionClause` and `StreakConfig`. Some of these names are snake_case and must be kept as written. |
| Free-form JSON | 12 DTO properties are `JsonElement`, e.g. account-type `config` and reward `typeConfig`. |
| Enum-like values are plain strings | Statuses, rule types and reward types are `string` in the DTOs. The allowed values live in `Shared/Constants`. |

## 3. Proposed direction

### 3.1 Stack (exact versions pinned at install)

| Area | Choice |
|---|---|
| Build and language | Vite, TypeScript with `strict` and `noUncheckedIndexedAccess` (as in `web/`), `vue-tsc` |
| Framework | Vue 3 (`<script setup lang="ts">`), Vue Router 4 |
| State | Pinia for **client state only**: session, active tenant, program context, locale. `@tanstack/vue-query` for **server state**: caching, refetching, pagination and invalidation. |
| UI | shadcn-vue components (Reka UI), Tailwind v4 with the brand tokens, lucide icons and vue-sonner toasts, **used only inside `shared/ui`** |
| Forms | VeeValidate with `@vee-validate/zod` and Zod |
| i18n | vue-i18n, English only at first, with keys seeded from `web/public/i18n/en.json` |
| API | `openapi-typescript` for the types and `openapi-fetch` for the typed client, with middleware for the cross-cutting concerns |
| Libraries also used by `web/` | big.js (live preview maths only), jwt-decode |
| Quality | ESLint flat config (`typescript-eslint`, `eslint-plugin-vue`, `eslint-plugin-boundaries`), Prettier, Vitest with `@vue/test-utils` and jsdom, `tsx` to run the generator, and Playwright in phase 4 |

### 3.2 Architecture: Feature-Sliced Design, organised by feature

- **Layers** run `app → pages → widgets → features → entities → shared`. Each layer imports only downwards, slices on the same layer never import each other, and other code reaches a slice only through its `index.ts`. `eslint-plugin-boundaries` enforces all three rules.
- **Entities** are the domain slices: program, account-type, tier, rule, reward, card-bucket, streak-campaign, config-version, customer, tenant, admin-user, api-key, event and dashboard. Each has:
  - `api/`: request functions, a query-key factory, and the `useXxxQuery` / `useXxxMutation` composables;
  - `model/`: types re-exported from the generated schema, Zod schemas and mappers;
  - `ui/`: presentational components only.
- **Features** are user actions, such as login, switching tenant, publishing a program or approving a rule, each with a `model/use*.ts` composable and a thin `ui/`.
- **Pages** are the route-level containers. **Widgets** are composite blocks such as the sidebar, topbar and tenant switcher.
- **Containers and presentational components.** Pages and widgets are containers: they may use composables, stores and queries. Components in `entities/*/ui`, `features/*/ui` and `shared/ui` take props and emit events only. A lint rule bans Vue Query, Pinia stores and `*/api` imports in them.
- **API layer.** Only `shared/api/client.ts` creates the HTTP client. `fetch`, `axios` and `openapi-fetch` are banned everywhere else by `no-restricted-imports`.
- **UI wrappers.** The shadcn-vue primitives, Reka UI, the icon set and the styling helpers may be imported only in `shared/ui/**`. The wrappers expose **our own** props: `AppButton`, `AppInput`, `AppMoneyInput`, `AppSelect`, `AppSearchableSelect`, `AppDataTable`, `AppPaginator`, `AppDialog`, `AppDrawer`, `AppTabs`, `AppStatusPill`, `AppEmptyState`, `AppSkeleton`, `AppPageHeader`, `AppBreadcrumbs`, `AppMeter` and `AppFormField`, plus `useAppToast`, `useAppConfirm` and `useAppDialog`. Swapping the UI library, or a single primitive, would only touch the wrappers.

### 3.3 Contracts carried over unchanged (`00-project-guardrails.md` §2)

| Contract | How `webVue3` keeps it |
|---|---|
| Tenancy | Composables take the active tenant from the `tenant` store and pass it as the `{tenantId}` path parameter. Every query key includes `tenantId`, so switching tenants never shows another tenant's cached data. A tenant id is never read from a body. |
| Money and points | The spec types `decimal` as `string`. `shared/money` (`DecimalString`, formatting, `AppMoneyInput`) is ported from `web/`. Money is never a JS `number`, and big.js is for live preview only. |
| Time | UTC ISO strings everywhere (port of `shared/date`). Streak timezones are explicit IANA ids. |
| DSL parity | `condition-tree-dsl.ts`, `condition-dsl.ts` and `streak-config.ts` are ported **verbatim**, with their spec files, into `shared/lib/dsl/`. |
| CASH approval, rule versioning | Enforced by the backend. The portal keeps the same approve actions and badges. |
| Error format | `core/http/api-error.ts` is ported as is (RFC 7807 body becomes `ApiError`). Server field errors are mapped onto VeeValidate forms. |
| HTTP behaviour | The 6 Angular interceptors become `openapi-fetch` middleware: correlation id, bearer token, `X-Tenant-Id`, retry, error normalisation with a toast, and the loading indicator. A 401 ends the session. |

### 3.4 OpenAPI generation (D4)

`webVue3/tools/openapi/generate-spec.ts` reads the C# files without changing or running them:

1. **Schemas** come from the positional records in `Api/*/*Dtos.cs`, the engine models they reference, and `Api.Framework`'s `PagedResult<T>` and `PageRequest`.
2. **Type mapping:**
   - `decimal` becomes a decimal-string `string`.
   - `Guid` becomes a `string` with format `uuid`, and `DateTime` a `string` with format `date-time`.
   - `T?` becomes nullable, and lists become arrays.
   - `JsonElement` becomes a free-form object.
   - Names become camelCase unless `[JsonPropertyName]` overrides them.
3. **Paths** come from each module's base path and its `MapX("…")` calls, plus the raw `Post` in `AuthModule`. Each route's request body comes from the validator or `ReadJsonBodyAsync<T>`. Its response comes from the app-service method and `JsonResponses`: `Ok` gives 200, `Created` 201, `NoContent` 204 and `Accepted` 202. Query parameters come from `ReadPageRequest()` and the `Request.Query[...]` reads.
4. **Overrides.** `webVue3/openapi/overrides.ts` covers what the script can't infer, mainly string unions taken from `Shared/Constants`. Each override carries a comment naming its C# source.
5. **Scripts.** `api:spec` writes the spec, `api:types` writes `src/shared/api/generated/schema.d.ts`, and `api:check` regenerates both and fails on any difference (run before every build and meant for CI). The generator **fails** on anything it can't map instead of guessing.
6. **Typed forms.** Zod form schemas are declared against the generated request types (`satisfies z.ZodType<…>`), so a DTO change breaks the frontend build.

**Limits.** The spec reflects the shapes in the code, not behaviour, such as which error codes an endpoint returns or which nested `JsonElement` keys a validator enforces. If the backend ever serves its own spec, `api:spec` becomes a download of it and nothing else changes.

## 4. Blast radius

| Area | Impact |
|---|---|
| Backend (`src/`), DB, migrations, Consumer | **None.** The generator only reads files. |
| API contract | **None.** It is read, never changed. |
| `web/` (Angular) | **None.** It stays the live portal (D2). |
| New folder | `webVue3/`, with its own `package.json`, `node_modules` and build output. The `.gitignore` needs entries for `webVue3/node_modules` and `webVue3/dist`. |
| Docs | §8 |
| Developer workflow | Two portals while the rewrite runs, and a backend DTO change needs `npm run api:spec && npm run api:types` in `webVue3`. `api:check` reports it when someone forgets. |

## 5. Open decisions

- **O1.** Should `webVue3` be added to `run.ps1`? Not in this CR; offered after phase 0.
- **O2.** Turkish (`tr`) translations: added after English parity, as a follow-up.
- **O3.** Retiring `web/` after parity sign-off is a separate decision, taken after phase 4.

## 6. Phasing and verification

| Phase | Contents |
|---|---|
| **0: Foundation and vertical slice** | Scaffold, tooling and lint boundaries; the OpenAPI generator and types; `shared` (API client and middleware, the UI wrappers needed so far, money, date, i18n, config); `app` (providers, router, the auth, role, tenant and feature-flag guards, layouts); **auth** (login, logout, session); **shell** (sidebar with program sub-nav, topbar, tenant switcher and picker, select-tenant, forbidden, not-found); **programs**: a paginated list, the create dialog, and the overview (edit, publish, Active switch, slug, default rounding). |
| 1: Program configuration | Account types, tiers (with reorder), rewards (with approve), program history. |
| 2: Rule builder and campaigns | Rules list and rule form, including the metadata cascade, condition-tree editor, and limits and configuration visibility; streak campaigns; card buckets. |
| 3: Customers, dashboard and events | Customers search and Customer 360 (9 tabs, event drawer, cursor paging); the dashboard; the event simulator. |
| 4: Platform and sign-off | Tenants, tenant overview, API keys, tenant admins; Playwright equivalents of `web/e2e`; a parity checklist for the PO and Architect. |

**Checks each phase must pass:**
- `npm run api:check`, `type-check`, `lint` (including a deliberate boundary violation that must fail), `test` and `build` in `webVue3`.
- Ported spec files must pass: DSL, money, `api-error`, pagination and server errors.
- Component and composable tests that mock the entity `api/` functions.
- `npm run dev` on port 4201.
- A side-by-side parity check with `web/` (same fields, validation and i18n keys).

**Constraint today:** the API and database aren't running. End-to-end checks against the real API wait until they are, and this document will say which checks have been run.

## 7. Risks

| Risk | Mitigation |
|---|---|
| **Double maintenance.** Features land in `web/` during the rewrite. | Each phase re-checks parity against the then-current `web/` screen, and `SCOPE_BASELINE.md` tracks status per feature. |
| **Generator gaps.** An unusual C# construct is skipped or mis-typed. | The generator fails loudly; overrides need a source comment; generator unit tests use real fixtures (Tiers, Rules, Customers). The spec covers shapes, not behaviour (§3.4). |
| **DSL drift.** The ported validators diverge from the engine. | Ported verbatim with their table tests; `.claude/rules/frontend-vue.md` binds future changes to the same rule as `web/`. |
| **Money precision.** A decimal ends up in a JS `number`. | The spec types decimals as strings; `DecimalString` is ported; Zod schemas validate decimal strings. |
| **No backend now.** Runtime behaviour can't be checked end to end. | The API layer is a single seam that tests can mock; end-to-end checks are scheduled for when the backend runs. |
| **Generated primitives drift from upstream.** The shadcn-vue components are copied into the repo, not installed. | They are kept as generated (no hand edits beyond theming) and refreshed with the pinned CLI; the app depends only on the wrappers' props. |
| **Size.** About 190 Angular files. | Phased (D1); phase 0 proves every pattern on one slice before the rest is ported. |

## 8. Docs to update in the same change (phase 0)

- **New `.claude/rules/frontend-vue.md`:** the rules for `webVue3/` (layers, slices, containers and presentational components, the API layer, wrappers, Pinia and Vue Query, forms, i18n, the OpenAPI workflow, contracts, and the checks to run before calling work done).
- **New `webVue3/CLAUDE.md` and `webVue3/README.md`.**
- **`.claude/rules/frontend.md`:** state that it applies to `web/`, and point to `frontend-vue.md`.
- **`.claude/rules/00-project-guardrails.md` §3:** add the `webVue3` reference slice.
- **`.claude/rules/01-workflow-and-debugging.md`:** `webVue3` verification commands and OpenAPI pitfalls.
- **Root `CLAUDE.md`:** `webVue3/` in the architecture and related-docs sections.
- **`docs/SCOPE_BASELINE.md`:** the new Vue 3 section and a revision-history row.
- **`LoyaltySaaSApi.md`:** a pointer to the generated `webVue3/openapi/openapi.json`.

## 9. Explicitly out of scope

- Any backend, API or schema change, including serving OpenAPI from the backend.
- New features or behaviour changes. This is a port; parity is the goal.
- Retiring or changing `web/`, and changes to `run.ps1` (O1, O3).
- Turkish translations (O2).
- The dev login bypass and dummy data, which are stashed separately as `devLoginBypass` and not part of either portal.

## 10. Next step

1. Raise this as a **Scope Change Request** in Jira, linking this document.
2. The team lead (Product Owner + Architect) reviews it, answers the open decisions in §5
   (O1 `run.ps1`, O2 Turkish, O3 retiring `web/`) and approves, approves with revisions, or rejects.
3. Only after the approval record above is filled in: implement on a feature branch, phase by
   phase (§6), updating the docs in §8 in the same change; then the Architect reviews the diff.

## 11. Document record (review record per `00-project-guardrails.md` §6)

| File | Change |
|---|---|
| `docs/scope-changes/2026-10-12-vue3-portal-rewrite.md` | Created: this impact analysis (proposal, awaiting approval). |
