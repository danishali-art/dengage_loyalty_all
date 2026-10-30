import { defineStore } from 'pinia';
import { ref } from 'vue';
import { DEFAULT_LOCALE, i18n, isAppLocale, type AppLocale } from '@/shared/i18n';
import { readStored, writeStored } from './storage';

const LOCALE_KEY = 'loyalty.portal.locale';

/** English until the admin picks another language in the user menu — never the browser's guess. */
function initialLocale(): AppLocale {
  const stored = readStored(LOCALE_KEY);
  return isAppLocale(stored) ? stored : DEFAULT_LOCALE;
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
