import { createI18n } from 'vue-i18n';
import en from './locales/en.json';

export type MessageSchema = typeof en;

/**
 * vue-i18n, English only for now. Keys are dotted and grouped by screen; Turkish is added later as
 * a second locale file with the same keys.
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
