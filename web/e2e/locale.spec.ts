import { test, expect } from '@playwright/test';
import AxeBuilder from '@axe-core/playwright';
import { loginAs } from './fixtures';
import { mockApi } from './mock-api';

/** The language switch (CR 2026-10-28), against the mocked `/api`. */
test.describe('language switch', () => {
  test.beforeEach(async ({ page }) => {
    await mockApi(page);
  });

  test('the login page switches to Turkish and remembers it after a reload', async ({ page }) => {
    await page.goto('/login');
    await page.getByRole('combobox', { name: 'Language' }).click();
    await page.getByRole('option', { name: 'Türkçe', exact: true }).click();

    await expect(page.getByRole('heading', { name: "Sadakat'e giriş yapın" })).toBeVisible();
    await expect(page.locator('html')).toHaveAttribute('lang', 'tr');

    await page.reload();
    await expect(page.getByRole('button', { name: 'Giriş yap' })).toBeVisible();
  });

  test("the user menu's Language submenu switches the shell to Turkish", async ({ page }) => {
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
  });
});
