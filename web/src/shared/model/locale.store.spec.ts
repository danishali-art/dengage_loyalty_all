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

  it('starts in English when nothing is stored and the browser language is not Turkish', () => {
    vi.spyOn(navigator, 'language', 'get').mockReturnValue('de-DE');
    expect(useLocaleStore().locale).toBe('en');
  });

  it('starts in Turkish for a Turkish browser', () => {
    vi.spyOn(navigator, 'language', 'get').mockReturnValue('tr-TR');
    expect(useLocaleStore().locale).toBe('tr');
  });

  it('prefers the stored choice over the browser language', () => {
    vi.spyOn(navigator, 'language', 'get').mockReturnValue('tr-TR');
    localStorage.setItem('loyalty.portal.locale', 'en');
    expect(useLocaleStore().locale).toBe('en');
  });

  it('ignores a stored value the portal has no locale for', () => {
    vi.spyOn(navigator, 'language', 'get').mockReturnValue('en-US');
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
