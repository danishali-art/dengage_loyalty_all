import { test, expect } from '@playwright/test';
import AxeBuilder from '@axe-core/playwright';
import { loginAs } from './fixtures';
import { mockApi } from './mock-api';

const PROGRAMS_URL = /\/programs(\?.*)?$/;

/**
 * Shell and routing smoke tests. They sign in through the real
 * login form, but `/api` is answered by `mockApi`, so no backend is needed.
 */
test.describe('shell + auth + tenancy', () => {
  test.beforeEach(async ({ page }) => {
    await mockApi(page);
  });

  test('an anonymous visit is sent to the login page with a return url', async ({ page }) => {
    await page.goto('/customers');
    await expect(page).toHaveURL(/\/login\?returnUrl=\/customers$/);
  });

  test('a tenant_admin lands on Programs with the app chrome rendered', async ({ page }) => {
    await loginAs(page, 'ops@novapay.test', 'any-password');

    await expect(page).toHaveURL(PROGRAMS_URL);
    await expect(page.getByRole('navigation', { name: 'Primary' })).toBeVisible();
    await expect(page.getByRole('heading', { level: 1 })).toContainText('Programs');
    // tenant_admin: the tenant is fixed by the JWT claim and shown as a static label.
    await expect(page.getByText('novapay')).toBeVisible();
    // Platform screens are platform_admin only.
    await expect(page.getByRole('link', { name: 'Platform' })).toHaveCount(0);
  });

  test('a platform_admin with no tenant is sent to the tenant picker', async ({ page }) => {
    await loginAs(page, 'platform@loyalty.test', 'any-password');

    await expect(page).toHaveURL(/\/select-tenant$/);
    await expect(page.getByRole('heading', { name: /select a tenant/i })).toBeVisible();
  });

  test('a tenant_admin is refused the platform screens', async ({ page }) => {
    await loginAs(page, 'ops@novapay.test', 'any-password');
    await page.goto('/platform/tenants');
    await expect(page).toHaveURL(/\/forbidden$/);
    await expect(page.getByText('403')).toBeVisible();
  });

  test('unknown routes redirect to the 404 page', async ({ page }) => {
    await loginAs(page, 'ops@novapay.test', 'any-password');
    await expect(page).toHaveURL(PROGRAMS_URL);
    await page.goto('/does-not-exist');
    await expect(page).toHaveURL(/\/not-found$/);
    await expect(page.getByText('404')).toBeVisible();
  });

  test('Programs screen has no critical accessibility violations', async ({ page }) => {
    await loginAs(page, 'ops@novapay.test', 'any-password');
    await expect(page).toHaveURL(PROGRAMS_URL);
    const results = await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa']).analyze();
    const serious = results.violations.filter((v) => v.impact === 'critical' || v.impact === 'serious');
    expect(serious, JSON.stringify(serious, null, 2)).toEqual([]);
  });
});
