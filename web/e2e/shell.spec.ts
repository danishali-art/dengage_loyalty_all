import { test, expect, Page } from '@playwright/test';
import AxeBuilder from '@axe-core/playwright';

const PROGRAMS_URL = /\/programs(\?.*)?$/;

/**
 * These are fast shell/routing smoke tests that intentionally don't need a backend — they
 * force stub auth mode via a config override, independent of the app's real deployed
 * `authMode` (which defaults to "http" so the rest of the suite exercises the real API).
 *
 * Stub mode signs in with a locally-generated, unsigned JWT — fine when nothing validates it,
 * but a real API (which this test run always has alongside it, for the other specs) correctly
 * rejects it with 401, which would otherwise bounce these tests back to /login. So every
 * `/api/**` call is stubbed too, keeping this suite genuinely backend-independent.
 */
async function useStubAuth(page: Page): Promise<void> {
  await page.route('**/app-config.json', async (route) => {
    const response = await route.fetch();
    const body = await response.json();
    await route.fulfill({ response, json: { ...body, authMode: 'stub' } });
  });
  await page.route('**/api/v1/**', async (route) => {
    await route.fulfill({ json: { data: [], page: 1, pageSize: 25, total: 0 } });
  });
}

test.describe('P0 shell + auth + tenancy', () => {
  test('a tenant_admin lands on Programs with the app chrome rendered', async ({ page }) => {
    await useStubAuth(page);
    await page.goto('/?stubRole=tenant_admin');

    await expect(page).toHaveURL(PROGRAMS_URL);
    await expect(page.getByRole('navigation', { name: 'Primary' })).toBeVisible();
    await expect(page.getByRole('heading', { level: 1 })).toContainText('Programs');
    // tenant_admin -> tenant is fixed from the JWT claim, shown as a static label
    await expect(page.getByText('novapay')).toBeVisible();
  });

  test('a platform_admin with no tenant is sent to the tenant picker', async ({ page }) => {
    await useStubAuth(page);
    await page.goto('/?stubRole=platform_admin');

    await expect(page).toHaveURL(/\/select-tenant$/);
    await expect(page.getByRole('heading', { name: /select a tenant/i })).toBeVisible();
  });

  test('unknown routes redirect to the 404 page', async ({ page }) => {
    await useStubAuth(page);
    await page.goto('/?stubRole=tenant_admin');
    await expect(page).toHaveURL(PROGRAMS_URL);
    await page.goto('/does-not-exist');
    await expect(page).toHaveURL(/\/not-found$/);
    await expect(page.getByText('404')).toBeVisible();
  });

  test('Programs screen has no critical accessibility violations', async ({ page }) => {
    await useStubAuth(page);
    await page.goto('/?stubRole=tenant_admin');
    await expect(page).toHaveURL(PROGRAMS_URL);
    const results = await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa']).analyze();
    const serious = results.violations.filter(
      (v) => v.impact === 'critical' || v.impact === 'serious',
    );
    expect(serious, JSON.stringify(serious, null, 2)).toEqual([]);
  });
});
