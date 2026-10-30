import { test, expect } from '@playwright/test';
import AxeBuilder from '@axe-core/playwright';
import { loginAs } from './fixtures';
import { mockApi } from './mock-api';

/**
 * The language switch (CR 2026-10-28), against the mocked `/api`. The portal is English until the
 * admin picks another language in the user menu; the login screen has no picker.
 */
test.describe('language switch', () => {
  test.beforeEach(async ({ page }) => {
    await mockApi(page);
  });

  test.describe('in a Turkish browser', () => {
    test.use({ locale: 'tr-TR' });

    test('the login page is English and offers no language picker', async ({ page }) => {
      await page.goto('/login');
      await expect(page.getByRole('heading', { name: 'Sign in to Loyalty' })).toBeVisible();
      await expect(page.locator('html')).toHaveAttribute('lang', 'en');
      await expect(page.getByRole('combobox', { name: 'Language' })).toHaveCount(0);
    });
  });

  test("the user menu's Language submenu switches to Turkish, and the choice is kept", async ({ page }) => {
    await loginAs(page, 'ops@novapay.test', 'any-password');
    // The avatar button's accessible name is the user id from the mocked token.
    await page.locator('.topbar__avatar').click();
    await page.getByRole('menuitem', { name: 'Language' }).click();
    await expect(page.getByRole('menuitemradio', { name: 'English' })).toHaveAttribute(
      'aria-checked',
      'true',
    );
    await page.getByRole('menuitemradio', { name: 'Türkçe' }).click();

    await expect(page.getByRole('navigation', { name: 'Ana menü' })).toBeVisible();
    await expect(page.locator('html')).toHaveAttribute('lang', 'tr');

    // The menu itself is now Turkish, with Türkçe checked.
    await page.locator('.topbar__avatar').click();
    await page.getByRole('menuitem', { name: 'Dil' }).click();
    await expect(page.getByRole('menuitemradio', { name: 'Türkçe' })).toHaveAttribute('aria-checked', 'true');
    await page.keyboard.press('Escape');
    await page.keyboard.press('Escape');

    const results = await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa']).analyze();
    const serious = results.violations.filter((v) => v.impact === 'critical' || v.impact === 'serious');
    expect(serious, JSON.stringify(serious, null, 2)).toEqual([]);

    await page.reload();
    await expect(page.getByRole('navigation', { name: 'Ana menü' })).toBeVisible();

    // Signing out keeps the saved language: the login page is Turkish too.
    await page.locator('.topbar__avatar').click();
    await page.getByRole('menuitem', { name: 'Çıkış yap' }).click();
    await expect(page.getByRole('heading', { name: "Sadakat'e giriş yapın" })).toBeVisible();
  });
});
