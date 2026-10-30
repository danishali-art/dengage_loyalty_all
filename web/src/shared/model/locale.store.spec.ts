import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { createPinia, setActivePinia } from 'pinia';
import { i18n, translate } from '@/shared/i18n';
import { formatMoney } from '@/shared/lib/money';
import { useLocaleStore } from './locale.store';

describe('useLocaleStore', () => {
  beforeEach(() => {
    localStorage.clear();
    setActivePinia(createPinia());
  });

  afterEach(() => {
    vi.restoreAllMocks();
    // Other specs expect the English default.
    i18n.global.locale.value = 'en';
    document.documentElement.lang = '';
  });

  it('starts in English when nothing is stored', () => {
    expect(useLocaleStore().locale).toBe('en');
  });

  // English until the admin changes it — the browser's language is never used as a guess.
  it('starts in English even for a Turkish browser', () => {
    vi.spyOn(navigator, 'language', 'get').mockReturnValue('tr-TR');
    expect(useLocaleStore().locale).toBe('en');
  });

  it('starts in the stored language', () => {
    localStorage.setItem('loyalty.portal.locale', 'tr');
    expect(useLocaleStore().locale).toBe('tr');
  });

  it('ignores a stored value the portal has no locale for', () => {
    localStorage.setItem('loyalty.portal.locale', 'xx');
    expect(useLocaleStore().locale).toBe('en');
  });

  it('set() switches the messages, <html lang> and number format, and saves the choice', () => {
    const store = useLocaleStore();
    store.set('tr');

    expect(localStorage.getItem('loyalty.portal.locale')).toBe('tr');
    expect(document.documentElement.lang).toBe('tr');
    expect(translate('auth.signOut')).toBe('Çıkış yap');
    expect(formatMoney('1234.5', { currency: 'SAR' })).toBe('1.234,50 SAR');

    store.set('en');
    expect(translate('auth.signOut')).toBe('Sign out');
    expect(formatMoney('1234.5', { currency: 'SAR' })).toBe('1,234.50 SAR');
  });
});
