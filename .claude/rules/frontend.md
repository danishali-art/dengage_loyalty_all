---
paths:
  - "web/**"
---

# Frontend rules — Loyalty Admin Portal (`web/`, Angular 22)

`web/CLAUDE.md` holds the architecture overview and commands. These are the rules to apply
when editing.

## Angular idioms (required)
- Standalone components only, **zoneless**, signals first. Never add an `NgModule`, zone.js,
  NgRx, Angular Material or PrimeNG.
- Components use `changeDetection: ChangeDetectionStrategy.OnPush`, `input()` / `output()` /
  `model()` (not `@Input` / `@Output`), and `inject()` (not constructor injection).
- Templates use built-in control flow (`@if`, `@for` with `track`, `@switch`). Don't use
  `*ngIf` / `*ngFor`.
- Local state lives in `signal()` / `computed()`. Use `effect()` only for side effects, never to
  derive state.
- Forms are typed reactive forms built with `NonNullableFormBuilder`. Don't use template-driven forms.

## Folder layout and boundaries (lint-enforced)
- `core/` holds singletons (http, auth, tenant, config, ui services), `shared/` holds the design
  system and pure utilities, `layout/` holds the shell, and `features/<resource>/` holds one
  lazy-loaded folder per resource.
- `features/*` must not import another top-level feature. `core/` and `shared/` must never
  import from `features/`. If two features need the same thing, move it to `shared/` or `core/`.
- File naming inside a feature, using `features/programs/rewards/` as the reference:
  - `<name>.model.ts` — types that mirror the API DTOs.
  - `<name>s.service.ts` — `@Injectable({ providedIn: 'root' })`, the only file that does HTTP.
  - `<name>s-list.page.ts` / `<name>-form.page.ts` — routed orchestrators.
  - `<name>-form.dialog.ts` — CDK dialogs, opened through `DialogService`.
  - `<feature>.routes.ts` — lazy routes.
- Presentational components (`shared/ui/*`) never do HTTP or inject feature services.

## HTTP and API contract
- Always go through `ApiClient` (`api.tenantScope` / `api.platform` / `api.auth`). Never inject
  `HttpClient` in features, and never hand-build the `/tenants/{id}` segment, because
  `ApiClient` injects it.
- Services return `Promise` through `firstValueFrom(...)`. Lists use `getPage<T>()` → `Page<T>`.
- Errors normalise to `ApiError`. Pass `{ skipErrorToast: true }` when the caller shows the error
  inline (for example form dialogs mapping field errors). Otherwise let the interceptor toast it.
- Model types must match the backend DTOs in `src/dEngage.Loyalty.Api/<Resource>/*Dtos.cs` and
  `LoyaltySaaSApi.md`. REST fields are camelCase. Free-form JSON configs (for example reward
  `typeConfig`) keep the snake_case keys the backend validates.
- String unions for backend enums (for example `RewardType`) must match the backend registries
  exactly. When the backend adds a value, update the union in the same change.

## Money, numbers and time
- Money and points are **`DecimalString`** (`shared/money`), never `number`. They are sent and
  received as strings.
- `big.js` is only for live preview math. Never submit a value computed with it.
- Timestamps are UTC ISO strings. Format them through `shared/date`. Streak `timezone` is an
  explicit IANA id, never the browser default.

## UI, styling and accessibility
- Use Tailwind v4 utilities and the `@theme` tokens in `src/styles.css`. Reuse the
  `shared/ui` components (`app-button`, `app-page-header`, `data-table`, `paginator`,
  `status-pill`, `empty-state`, `skeleton`, `dialog-shell`, `searchable-select`, ...) before
  writing new markup.
- Don't hard-code hex colours or add component CSS files when a token or utility exists.
- Icon-only buttons need an `ariaLabel` (lint-enforced). Decorative SVGs get `aria-hidden="true"`.
- Never show status through colour alone. Pair it with text (`status-pill`).
- Loading states set `aria-busy`. Lists show an `empty-state` when there is nothing to display.

## i18n
- New user-facing strings get keys in **both** `public/i18n/en.json` and `public/i18n/tr.json`.
  Don't add new bare strings to templates, and when you touch a string in an existing file,
  migrate it to a key.

## Condition and streak DSL
- `shared/forms/condition-tree-dsl.ts` and the streak config validators mirror the C# in
  `src/dEngage.Loyalty.RuleEngine/` (`GroupedConditionDsl.cs`, `Models/`). Port changes verbatim
  and table-test them against the same cases. The frontend must never accept what the engine rejects.

## Before calling work done (run from `web/`)
- `npx ng lint`, `npm test`, and `npx ng build --configuration development`. Use `npm run build`
  (production, budgeted) for anything that adds dependencies or large assets.
- `npx prettier --write` on the files you changed (the Tailwind class-order plugin is configured).
- Don't add npm dependencies without being asked.
- If the change alters the API contract or scope, update `LoyaltySaaSApi.md` and
  `docs/SCOPE_BASELINE.md` in the same change (see the root `CLAUDE.md`).
