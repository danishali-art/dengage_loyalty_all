import { defineStore } from 'pinia';
import { ref } from 'vue';
import { DEFAULT_LOCALE, i18n, isAppLocale, type AppLocale } from '@/shared/i18n';
import { readStored, writeStored } from './storage';

const LOCALE_KEY = 'loyalty.portal.locale';

/** No choice stored yet: the browser's language if the portal has it, otherwise English. */
function initialLocale(): AppLocale {
  const stored = readStored(LOCALE_KEY);
  if (isAppLocale(stored)) return stored;
  const browser = typeof navigator === 'undefined' ? '' : navigator.language.slice(0, 2).toLowerCase();
  return isAppLocale(browser) ? browser : DEFAULT_LOCALE;
}

/**
 * Client state: the portal's language, saved per browser (CR 2026-10-28). `<html lang>` follows
 * it so CSS `uppercase` cases Turkish correctly (i → İ).
 */
export const useLocaleStore = defineStore('locale', () => {
  const locale = ref<AppLocale>(initialLocale());

  function apply(): void {
    i18n.global.locale.value = locale.value;
    if (typeof document !== 'undefined') document.documentElement.lang = locale.value;
  }

  function set(next: AppLocale): void {
    locale.value = next;
    writeStored(LOCALE_KEY, next);
    apply();
  }

  return { locale, set, apply };
});
