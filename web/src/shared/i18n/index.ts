import { createI18n } from 'vue-i18n';
import en from './locales/en.json';
import tr from './locales/tr.json';

export type MessageSchema = typeof en;

export const SUPPORTED_LOCALES = ['en', 'tr'] as const;
export type AppLocale = (typeof SUPPORTED_LOCALES)[number];
export const DEFAULT_LOCALE: AppLocale = 'en';

/** Each language named in itself, so an admin finds theirs whatever is showing (CR 2026-10-28). */
export const LOCALE_NAMES: Readonly<Record<AppLocale, string>> = { en: 'English', tr: 'Türkçe' };

export function isAppLocale(value: unknown): value is AppLocale {
  return typeof value === 'string' && (SUPPORTED_LOCALES as readonly string[]).includes(value);
}

/**
 * vue-i18n with English and Turkish. Keys are dotted and grouped by screen; every locale file has
 * exactly `en.json`'s keys — typing them all as `MessageSchema` makes a missing key a type error.
 * The active locale is chosen by `useLocaleStore` (`shared/model`).
 */
export const i18n = createI18n<[MessageSchema], AppLocale, false>({
  legacy: false,
  locale: DEFAULT_LOCALE,
  fallbackLocale: DEFAULT_LOCALE,
  messages: { en, tr },
});

/** Translate outside components (composables, toasts). Inside templates use `t` from `useI18n()`. */
export function translate(key: string, params?: Record<string, unknown>): string {
  return i18n.global.t(key, params ?? {});
}

/**
 * The locale `Intl` formats dates and numbers in: the portal's language, not the browser's, so a
 * Turkish screen shows `1.234,56` and Turkish month names. Display only — inputs and the wire
 * format don't change (guardrails §2, Money).
 */
export function intlLocale(): AppLocale {
  return i18n.global.locale.value;
}
