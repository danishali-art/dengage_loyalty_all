import { computed } from 'vue';
import { LOCALE_NAMES, SUPPORTED_LOCALES, type AppLocale } from '@/shared/i18n';
import { useLocaleStore } from '@/shared/model';
import type { SelectOption } from '@/shared/ui';

/** Pick the portal's language. Used by the login page and the topbar's user menu. */
export function useLocaleSwitch() {
  const store = useLocaleStore();

  const options: SelectOption<AppLocale>[] = SUPPORTED_LOCALES.map((value) => ({
    value,
    label: LOCALE_NAMES[value],
  }));

  const locale = computed<AppLocale>({
    get: () => store.locale,
    set: (next) => store.set(next),
  });

  return { locale, options, select: (next: AppLocale) => store.set(next) };
}
