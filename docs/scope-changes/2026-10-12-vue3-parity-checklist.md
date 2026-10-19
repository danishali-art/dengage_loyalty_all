# Parity checklist: Vue 3 portal (`webVue3/`) vs Angular portal (`web/`)

**For:** Product Owner + Architect sign-off (scope change
`docs/scope-changes/2026-10-12-vue3-portal-rewrite.md`, phase 4).
**Prepared:** 2026-10-12, by Claude Code at the request of Syed jamal ud din.
**Status:** **Awaiting sign-off.** Every Angular screen has a Vue port. **The real-backend
end-to-end run is still outstanding** (§4) and must pass before `web/` can be retired (O3).

## 1. How to read this

Each row is one Angular screen (`web/src/app/features/**`) and its Vue port. The columns say how the
port was checked:

| Check | What it means |
|---|---|
| **Spec** | ✓ = a Vitest page spec in `webVue3/src/pages/<page>/ui/*.spec.ts` mounts the real page with only the entity `api/*.api.ts` functions mocked, porting the Angular spec's cases. Otherwise the specs that cover the screen's logic are named, or — if none. |
| **Browser** | The screen was driven in Edge on the dev server (`:4201`) with `/api` answered by DTO-shaped JSON (scripts in the session scratchpad; results in the scope-change record §12.3–§16.3). |
| **E2E (mocked)** | Covered by a Playwright spec in `webVue3/e2e/*.spec.ts` that runs without the backend. |
| **E2E (real)** | Covered by a Playwright port of `web/e2e/*.e2e.spec.ts` in `webVue3/e2e/*.e2e.spec.ts`. **Written, not yet run** (no backend in this environment). |
| **Diff** | Deliberate differences, by id from the scope-change record (P1-*, P2-*, P3-*, P4-*) and `webVue3/CLAUDE.md` → "Differences from `web/`". |

"Same fields, validation and i18n keys" was checked for every row against the Angular source (forms,
validators, messages); where Vue follows the backend validator more strictly than Angular did, it
is listed under Diff.

## 2. Screens

| # | Angular screen (route) | Vue page (`webVue3/src/pages/`) | Spec | Browser | E2E (mocked) | E2E (real) | Diff |
|---|---|---|---|---|---|---|---|
| 1 | Login (`/login`) | `login` | session store, API client | ✓ | ✓ (`shell.spec.ts`) | ✓ (`loginAs` in every port) | No `stub` auth mode |
| 2 | Tenant picker (`/select-tenant`) | `select-tenant` | guards | ✓ | ✓ | ✓ | — |
| 3 | Shell: sidebar + program / tenant sub-nav, topbar tenant switcher, user menu | `widgets/app-sidebar`, `widgets/app-topbar` | guards | ✓ | ✓ (+ axe) | ✓ | Topbar search box (non-functional in Angular) left out |
| 4 | Forbidden / Not found | `status` | — | ✓ | ✓ | — | — |
| 5 | Dashboard (`/dashboard`, landing page) | `dashboard` | ✓ | ✓ | — | — | P3-1 (shared selects) |
| 6 | Programs list + create (`/programs`) | `programs-list` | ✓ | ✓ | ✓ (+ axe) | ✓ (`programs.e2e`) | — |
| 7 | Program overview (`/programs/:id`) | `program-overview` | program schema | ✓ | — | ✓ (`programs.e2e`) | — |
| 8 | Account types | `account-types` | ✓ | ✓ | — | ✓ (`programs.e2e`) | P1: name ≤ 100 (backend), Type disabled on edit |
| 9 | Tiers (reorder, delete guard) | `tiers` | ✓ | ✓ | — | ✓ (`programs.e2e`) | P1: min points as decimal text; move waits for the server |
| 10 | Rewards (approve, retired view-only) | `rewards` | ✓ | ✓ | — | ✓ (`programs.e2e`) | P1: name incl. prefix ≤ 100 |
| 11 | Program history | `program-history` | config-version model | ✓ | — | — | — |
| 12 | Rules list (grouped) | `rules` | ✓ | ✓ | — | ✓ (`rules.e2e`) | — |
| 13 | Rule form (new / edit) | `rule-edit` | ✓ | ✓ | — | ✓ (`rules.e2e`) | P2: rate / amount > 0, whole-number priority; server field errors on fields |
| 14 | Streak campaigns list + form | `streak-campaigns`, `streak-campaign-edit` | form ✓, list — | ✓ | — | — | P2 (as rules) |
| 15 | Card buckets list + form | `card-buckets`, `card-bucket-edit` | form ✓, list — | ✓ | — | — | P2: reward amount > 0 |
| 16 | Customers list (`/customers`) | `customers` | ✓ | ✓ | — | ✓ (`customers.e2e`) | P3-1 |
| 17 | Customer 360 (9 tabs + event drawer) | `customer-detail` | ✓ | ✓ | — | ✓ (`customers.e2e`) | P3-1 (Reka tabs; drawer = dialog docked right) |
| 18 | Event Simulator (`/events`, feature flag) | `event-simulator` | ✓ | ✓ | — | — | — |
| 19 | Platform: tenants list + create (`/platform/tenants`) | `platform-tenants` | ✓ | ✓ | ✓ (+ axe) | ✓ (`platform.e2e`) | — |
| 20 | Platform: tenant overview, suspend / reactivate | `platform-tenant-overview` | ✓ | ✓ | ✓ (+ axe) | ✓ (`platform.e2e`) | — |
| 21 | Platform: API keys (create shown once, revoke) | `platform-api-keys` | ✓ | ✓ | ✓ (+ axe) | ✓ (`platform.e2e`) | P4-1 |
| 22 | Platform: tenant admins (create) | `platform-admin-users` | ✓ | ✓ | ✓ | ✓ (`platform.e2e`) | P4-2 |

Screens without a page spec (login, tenant picker, status, program overview and history, the streak-campaign and card-bucket lists) are covered by the browser runs and, where marked, the e2e specs; adding page specs for them is a follow-up, not a parity gap.

No Angular route is left without a Vue port: the "not in the new portal yet" placeholder was
removed in phase 4.

## 3. Cross-cutting contracts (`00-project-guardrails.md` §2)

| Contract | Vue portal | Checked by |
|---|---|---|
| Tenancy | Every tenant query key starts with the tenant id; platform keys with `['platform', …]`; the platform screens use the route's `:tenantId`, never the active tenant context; `tenant_admin` is refused `/platform/**` (403). | Query-key specs; guard specs; `shell.spec.ts` ("refused the platform screens") |
| Money and points | Decimal strings end to end (`DecimalString`, generated types say `string`); numeric form fields are text. | Money specs; generator spec (decimal → string) |
| DSL parity | Condition and streak validators ported verbatim (`shared/lib/dsl`). | The ported Angular `condition-tree-dsl.spec.ts` (Angular has no streak-config spec, so neither does Vue) |
| CASH / reward approval | Approve buttons, self-approval refused by the server and toasted. | Rules / rewards page specs; browser run |
| Time | UTC ISO on the wire; local display; IANA streak timezones. | Date specs; filter specs |
| API contract | Types generated from the C# source; `npm run api:check` fails on drift (also before every build). | `api:check` |
| Accessibility | axe (WCAG 2 A/AA, no critical or serious violations) on Programs and the three platform screens. | `shell.spec.ts`, `platform.spec.ts` |

## 4. Outstanding before `web/` is retired

1. **Run the real-backend suite**: with Postgres, Redis, RabbitMQ, the Api and the Consumer up,
   run `E2E_BACKEND=1 npm run e2e` from `webVue3/` (with the dev server on `:4201`). The four
   `*.e2e.spec.ts` ports have never run. Their selectors come from the Vue forms (ids and labels),
   not from a run, so expect small fixes the first time.
2. **Side-by-side visual review** of each row with both portals running against the same data.
3. **Decisions**: O1 (add `webVue3` to `run.ps1`), O2 (Turkish), O3 (retire `web/`), and the
   toast placement question below.

## 5. Observations for the Product Owner

- **Toast placement (P4-3).** Vue shows toasts top-right *below* the topbar, so a toast covers the
  page header's action buttons for about 4 seconds (Angular shows them over the topbar instead).
  The e2e specs wait each toast out. Left unchanged pending a decision.
- **Colour contrast (P4-4).** The axe checks found the muted text and the success / warning /
  danger status colours below WCAG AA (4.5:1). The tokens were darkened in phase 4 (muted
  `#8a94a6` → `#5f6b7a`, success `#16a34a` → `#15803d`, warning `#d97706` → `#b45309`, danger
  `#ef4444` → `#b91c1c`, which is also the destructive button). This changes the look slightly
  from `web/`.

## 6. Deliberate differences introduced in phase 4

| # | Difference | Why |
|---|---|---|
| P4-1 | The new key's dialog forgets the raw key when it closes, however it is closed (Done, Escape, outside click). | Same as Angular's Done; the other ways of closing now behave the same. |
| P4-2 | The admin form also checks email ≤ 255 on the client (Angular checked a valid email and password ≥ 12 only). | Mirrors `PlatformValidators`; Angular left the length to the API. |
| P4-3 | Toast placement (see §5). | Phase-2 choice, kept. |
| P4-4 | Darker text tokens (see §5). | WCAG AA, enforced by the e2e axe checks. |

## 7. Sign-off

| Role | Name | Decision | Date |
|---|---|---|---|
| Product Owner | | | |
| Architect | | | |
