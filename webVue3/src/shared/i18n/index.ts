import { createI18n } from 'vue-i18n';
import en from './locales/en.json';

export type MessageSchema = typeof en;

/**
 * vue-i18n, English only for now (scope change 2026-10-12). Keys are the Angular portal's keys
 * (imported by tools/i18n/import-angular-locale.ts), so both portals translate the same way.
 * Turkish is added later with the same importer.
 */
export const i18n = createI18n<[MessageSchema], 'en'>({
  legacy: false,
  locale: 'en',
  fallbackLocale: 'en',
  messages: { en },
});

/** Translate outside components (composables, toasts). Inside templates use `t` from `useI18n()`. */
export function translate(key: string, params?: Record<string, unknown>): string {
  return i18n.global.t(key, params ?? {});
}
