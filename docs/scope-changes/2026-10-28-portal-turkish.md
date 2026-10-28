# Scope Change Impact Analysis: Turkish in the Admin Portal

**Status:** Approved 2026-10-28 by Product Owner + Architect — implemented (see §9).

**Date:** 2026-10-28
**Author:** Claude Code, at the request of haider.abdullah@dengage.com
**Affects:** `docs/SOW.md` §2.8 and §3 ("English only"), `docs/SCOPE_BASELINE.md` (Frontend →
foundation row), `web/CLAUDE.md`, `.claude/rules/frontend.md`

This document is step 2 of the scope-change process defined in `CLAUDE.md`. It analyzes the
impact of adding Turkish as a second language to the admin portal (`web/`), with a language
switch, against the current `docs/SCOPE_BASELINE.md` baseline and the as-built code.

## Approval record

Product Owner + Architect approved the change in §3 on 2026-10-28 (confirmed by
haider.abdullah@dengage.com) and resolved the open decisions in §5:

1. **Translation sign-off** — Claude Code drafts `tr.json`; a native Turkish speaker reviews it
   before release (still open, outside the repo).
2. **Server messages stay English** — accepted as out of scope.
3. **Turkish number format** — accepted: amounts and dates display in Turkish format (`1.234,56`);
   inputs still take `1234.56`.

## 1. Why this change is being proposed

`docs/SOW.md` §3 lists as a known limitation: "**English only**: every screen's strings are
translation keys, but only the English locale exists; Turkish needs a second locale file." Admins
can't use the portal in Turkish, and there's no way to choose a language. The old Angular portal
had a `tr.json`, but it wasn't carried over to the Vue 3 portal, and it's no longer in the repo.

## 2. Current state (as-built)

- **One locale.** `web/src/shared/i18n/index.ts` creates vue-i18n with `locale: 'en'`,
  `fallbackLocale: 'en'` and only `locales/en.json`, which has 977 keys in 22 top-level groups
  (`app`, `nav`, `action`, `common`, `auth`, `tenant`, … `platform`). No key uses plural forms
  (`|`).
- **Every user-facing string is already a key.** `vue/no-bare-strings-in-template` is on, and code
  outside components uses `translate(key, params)`. Zod form schemas are built by functions that
  call `translate(...)` when the form opens.
- **No language switch.** The topbar user menu (`widgets/app-topbar/model/useTopbar.ts`) has the
  role line and Sign out only. The login page has no topbar.
- **Formatting follows the browser, not the app.** `Intl` is called with an `undefined` locale in
  `shared/lib/date/format.ts`, `shared/lib/money/format.ts` and
  `pages/dashboard/model/useDashboardPage.ts`, so dates and numbers follow the browser's language
  whatever the portal shows.
- **One hard-coded English string** in `shared/api/api-error.ts`: "Cannot reach the server. Check
  your connection and try again." (network failures).
- **Server-sent text is English.** `ProblemDetails` titles and FluentValidation field messages come
  from the Api in English. The portal shows them as they arrive.
- **Client-state pattern to follow:** `shared/model/tenant-context.store.ts` (a Pinia store saved
  with `readStored` / `writeStored` from `shared/model/storage.ts`).

## 3. Proposed change

Portal only. The backend, DB and API don't change.

1. **Turkish locale file.** `web/src/shared/i18n/locales/tr.json`, with exactly the keys and the
   `{x}` placeholders of `en.json`, and the same `@ | { } $` escapes. It is typed as the English
   message schema, so `vue-tsc` fails on a missing or extra key.
2. **Language state.** `web/src/shared/model/locale.store.ts`, a Pinia store shaped like
   `tenant-context.store.ts`.
   - It saves the choice in the browser (localStorage key `loyalty.portal.locale`).
   - On first visit it uses the browser's language when that's Turkish, otherwise English.
   - Choosing a language switches vue-i18n's locale and sets `<html lang>`.
   - The stored choice is applied once at startup.
3. **Language switch.** A new feature slice, `features/locale-switch` (copying the shape of
   `features/tenant-switch`), offering "English" and "Türkçe". It appears:
   - in the topbar user menu, as a **Language** submenu between the role line and Sign out
     (English / Türkçe, the current one checked);
   - on the login page.
4. **Formatting follows the chosen language.** The three `Intl` call sites above use the portal's
   language instead of the browser's. This changes display only: parsing and the wire format don't
   change, and timestamps stay UTC ISO on the wire.
5. **The hard-coded network-error string** becomes a translation key.
6. **Tests.**
   - A spec checking `tr.json` has exactly `en.json`'s keys and placeholders.
   - Specs for the locale store and the switch.
   - A mocked e2e spec: switch to Turkish, the choice survives a reload, and the axe accessibility
     check passes in Turkish.

## 4. Impact

| Area | Impact |
|---|---|
| Backend (`src/`) | None |
| DB schema / migrations / `scripts/loyalty_schema_reference.md` | None |
| API / DTOs / `LoyaltySaaSApi.md` / `web/openapi/openapi.json` | None — no breaking change |
| Events / Consumer | None |
| Portal | New: `shared/i18n/locales/tr.json`, `shared/model/locale.store.ts`, `features/locale-switch/*`, specs, `e2e/locale.spec.ts`. Changed: `shared/i18n/index.ts`, `shared/model/index.ts`, `app/main.ts` (or `app/providers`), `widgets/app-topbar/model/useTopbar.ts`, the login page, `shared/lib/date/format.ts`, `shared/lib/money/format.ts`, `pages/dashboard/model/useDashboardPage.ts`, `shared/api/api-error.ts`, `en.json` (one new key) |
| Docs | `docs/SCOPE_BASELINE.md`, `docs/SOW.md` §2.8 / §3, `web/CLAUDE.md`, `.claude/rules/frontend.md` |

**Contracts (guardrails §2):** none touched.
- Money and points stay `DecimalString`. `formatMoney` stays display-only.
- Number inputs still take `.` as the decimal separator, matching the backend validators.
- Tenancy, ledger, events, rule versioning, approvals and DSL parity are unaffected.

## 5. Open decisions for Product Owner + Architect

1. **Translation sign-off.** Claude Code drafts `tr.json`. A native Turkish speaker reviews it
   before release. A short glossary of domain terms should be agreed first, so they're translated
   the same way everywhere: points, wallet / account type, tier, streak, reward, rule, ledger,
   publish, approve, program.
2. **Server messages stay English.** Error titles and field messages sent by the Api are not
   translated by this change. Translating them would be a separate backend localisation CR (or
   portal-side messages keyed on the existing `snake_case` error codes).
3. **Turkish number format.** In Turkish, amounts would display as `1.234,56`, while admins still
   type `1234.56` in number fields. Proposed: accept the Turkish display format. The alternative is
   to keep English number formatting in Turkish too.

## 6. Risks

- **Translation quality.** Mitigated by the native-speaker review (decision 1). That review is a
  release gate outside the repo.
- **Layout.** Turkish strings are often longer than English ones, so they can overflow buttons,
  tabs and table headers. Covered by a manual pass over the main screens.
- **Turkish casing.** `"i".toUpperCase()` gives `I`, not `İ`, and CSS `uppercase` depends on
  `<html lang>`. CSS `uppercase` is used on table headers and section titles (`AppDataTable`,
  the Customer 360 tabs, the event drawer, `RewardForm`), and setting `lang="tr"` makes it correct. Any JS `toUpperCase()` on translated
  text needs `toLocaleUpperCase('tr')`. Today the only `toUpperCase()` in the shell is the avatar
  initial, which is built from the user id, not translated text.
- **Already-shown validation messages** stay in the previous language until the form is reopened,
  because form schemas are built when the form opens. Acceptable for a rare action.

## 7. Verification (for the implementation)

- From `web/`: `npm run api:check`, `npm run type-check`, `npm run lint`, `npm test`,
  `npm run build`, `npx prettier --write` on changed files, `npm run e2e`.
- Manual check:
  1. Switch the language on the login page and in the user menu.
  2. In Turkish, walk through programs, rule edit, Customer 360 (tabs and the event drawer),
     dialogs and toasts.
  3. Check that dates and amounts are formatted the Turkish way and that long labels fit.
  4. Reload and check the choice is kept.

## 8. Files changed by the analysis (before approval)

| File | Change |
|---|---|
| `docs/scope-changes/2026-10-28-portal-turkish.md` | Created — this impact analysis |
| `docs/SCOPE_BASELINE.md` | Open item added pointing to this proposed CR; no rows changed |

## 9. Implementation (2026-10-28)

As proposed in §3, with two additions:

- The login form's schema is now a `computed`, so its validation messages follow a language switch
  made on the login page itself (other forms are rebuilt when they open, see §6).
- At the developer's request (2026-10-28), the user menu's language choice was moved from a bare
  item ("Türkçe" under the role) into its own **Language** submenu that lists both languages and
  marks the current one. This is UI only, inside the approved scope.

### Files

| File | Change |
|---|---|
| `web/src/shared/i18n/locales/tr.json` | Created — Turkish messages, the same keys and placeholders as `en.json` (draft, pending native-speaker review) |
| `web/src/shared/i18n/locales/en.json` | Changed — new keys `locale.label` and `common.networkError` |
| `web/src/shared/i18n/index.ts` | Changed — registers `tr` (typed as the English schema), `SUPPORTED_LOCALES`, `LOCALE_NAMES`, `isAppLocale`, `intlLocale()` |
| `web/src/shared/i18n/locales.spec.ts` | Created — same keys, same placeholders, no empty messages |
| `web/src/shared/model/locale.store.ts` | Created — the active language: stored choice → browser language → English; sets vue-i18n and `<html lang>`; saved as `loyalty.portal.locale` |
| `web/src/shared/model/locale.store.spec.ts` | Created — fallback order, saving, `<html lang>`, Turkish messages and number format |
| `web/src/shared/model/index.ts` | Changed — exports `useLocaleStore` |
| `web/src/app/providers/index.ts` | Changed — applies the saved language at startup |
| `web/src/features/locale-switch/index.ts`, `model/useLocaleSwitch.ts`, `ui/LocaleSwitch.vue` | Created — the language switch (options, current language, select) |
| `web/src/widgets/app-topbar/model/useTopbar.ts` | Changed — a Language submenu in the user menu, between the role and Sign out |
| `web/src/shared/ui/overlay/types.ts`, `AppPopupMenu.vue`, `web/src/shared/ui/index.ts` | Changed — `PopupMenuItem.choices`: a submenu of radio options, built from the existing dropdown-menu primitives (sub, radio group, radio item) |
| `web/src/shared/ui/overlay/AppPopupMenu.spec.ts` | Created — submenu opens, current option checked, a pick is reported |
| `web/src/pages/login/ui/LoginPage.vue` | Changed — language picker under the sign-in card |
| `web/src/features/auth-login/model/useLoginForm.ts` | Changed — computed schema so its messages follow a language switch |
| `web/src/shared/lib/date/format.ts`, `web/src/shared/lib/money/format.ts`, `web/src/pages/dashboard/model/useDashboardPage.ts` | Changed — format in the portal's language (`intlLocale()`) instead of the browser's |
| `web/src/shared/api/api-error.ts` | Changed — the network-error message is a translation key |
| `web/e2e/locale.spec.ts` | Created — switch on the login page and survive a reload; switch from the user menu; axe check in Turkish |
| `docs/scope-changes/2026-10-28-portal-turkish.md` | Created — this document |
| `docs/SCOPE_BASELINE.md` | Changed — foundation row, limitations sentence, open item (translation review), revision history |
| `docs/SOW.md` | Changed — §2.8 localization line; §3 "English only" struck through |
| `web/CLAUDE.md` | Changed — behaviour note on languages |
| `.claude/rules/frontend.md` | Changed — i18n rules (both locale files, `intlLocale()`, computed schemas, Turkish casing), `useLocaleStore` in client state |

### Verification

From `web/`:
- **Passed** (re-run after the Language submenu change): `npm run type-check`, `npm run lint`,
  `npm test` (44 files, 239 tests, including the 3 new specs), `npx vite build`, `npm run e2e`
  with `PW_CHANNEL=msedge` (10 mocked tests passed, including the 2 new ones; the 4 real-backend
  specs are skipped by default).
- **`npm run build` fails at its `api:check` prebuild**, which was already failing on this Windows
  checkout and isn't caused by this change. `openapi.json` and `schema.d.ts` are CRLF here
  (`core.autocrlf=true`) but generated as LF. With line endings ignored, both match the backend
  source.
- **Not done:** a manual pass over every screen in Turkish (long labels), and the native-speaker
  review.
