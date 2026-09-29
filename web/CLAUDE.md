# CLAUDE.md — Loyalty Admin Portal (`web/`)

Angular admin/configuration portal for the LoyaltySaaS engine. See the repo-root `CLAUDE.md`
for the backend; the API contract this app targets is `../LoyaltySaaSApi.md`; the delivery
plan is `~/.claude/plans/enterprise-loyalty-engine-cheeky-mitten.md`.

## Commands (run from `web/`)

```bash
npm start                       # ng serve (proxies /api -> localhost:5000; MSW stubs when API is down)
npm run build                   # ng build --configuration production (strict, budgeted)
npx ng build --configuration development
npm test                        # Vitest (@angular/build:unit-test)
npx ng lint                     # angular-eslint flat config
npx prettier --write .          # format
```

## Architecture

- **Angular 22**, standalone only, **zoneless**, signals-first. Typed reactive forms
  (`NonNullableFormBuilder`). No `NgModule`.
- **Feature-based**: `core/` (singletons), `shared/` (design system + pure utils),
  `layout/` (shell/sidebar/topbar), `features/*` (one lazy-loaded folder per resource).
  Lint forbids `features/* → features/*` and `core|shared → features`.
- **UI**: Angular CDK + custom components + **Tailwind v4** (`@theme` tokens in
  `src/styles.css`, ported from `../ux/en/fintech/*.html`). No Material / PrimeNG.
- **State**: services + signals. One route-scoped `ProgramContextStore`. No NgRx.
- **Tenancy**: routes are tenant-relative; `ApiClient` injects the `/tenants/{id}` segment
  into API URLs only. `TenantContext` holds the id; `TenantStore` owns switching.
- **Auth**: `AuthService` (abstract) + `SessionStore`. `authMode` in `public/app-config.json`
  picks `StubAuthService` (default, auto-signs-in) or `HttpAuthService` (`POST /auth/login`).
- **HTTP**: `ApiClient` (scopes: `tenantScope` / `platform` / `auth`) over `HttpClient` +
  6 functional interceptors (`core/http/interceptors`). Failures normalise to `ApiError`.
- **Config**: build-time `src/environments/*` (only `production` / `useApiMock` / `logLevel`);
  deploy-time `public/app-config.json` (fetched by an app initializer → `APP_CONFIG` token).

## Conventions

- Money/points are **decimal strings**, never JS numbers (`shared/money`, `DecimalString`).
  `big.js` is for live-preview math only — never for a submitted value.
- All timestamps are UTC ISO. Streak `timezone` is an IANA id (no browser default).
- Author i18n keys from day one (`public/i18n/{en,tr}.json`) — no bare UI strings.
- Components: `ChangeDetectionStrategy.OnPush`, `input()`/`output()`, `*.page.ts` for routed
  orchestrators, presentational components do no HTTP.
- Icon-only buttons require `ariaLabel` (lint-enforced). Status is never colour-only.
- Port DSL validators (`condition-dsl.ts`, `streak-config.ts`) verbatim from the C# in
  `../src/LoyaltySaaS.RuleEngine/Models/` — table-test against those cases.
