import { test, expect, type Page } from '@playwright/test';
import AxeBuilder from '@axe-core/playwright';
import { loginAs } from './fixtures';
import { mockApi } from './mock-api';

/**
 * Asserts a toast, then waits for it to go: toasts sit top-right under the topbar, over the page
 * header's action buttons, so the next header click would land on the toast.
 */
async function toast(page: Page, text: string): Promise<void> {
  await expect(page.locator('[data-sonner-toast]', { hasText: text })).toBeVisible();
  await expect(page.locator('[data-sonner-toast]')).toHaveCount(0, { timeout: 10_000 });
}

/**
 * The tenant provisioning flow, against `mockApi` instead of the real backend
 * (`platform.e2e.spec.ts` runs the same flow against the real one).
 */
test.describe('Platform — tenant provisioning (mocked API)', () => {
  test('platform_admin creates a tenant, an API key, and a tenant admin', async ({ page }) => {
    // A long flow, and each toast is waited out (`toast`).
    test.setTimeout(90_000);
    await mockApi(page);
    const main = page.locator('main');
    const sidebar = page.getByRole('navigation', { name: 'Primary' });

    await loginAs(page, 'platform@loyalty.test', 'any-password');
    await expect(page).toHaveURL(/\/select-tenant$/);

    await page.goto('/platform');
    await expect(page).toHaveURL(/\/platform\/tenants$/);
    await expect(page.getByRole('heading', { name: 'Tenants' })).toBeVisible();

    // Client-side validation mirrors the backend's id rule.
    await page.getByRole('button', { name: 'New tenant' }).click();
    await page.getByLabel('Tenant id').fill('Bad-Id');
    await page.getByLabel('Display name').fill('Bad');
    await page.getByRole('button', { name: 'Create tenant' }).click();
    await expect(page.locator('#tenant-id-error')).toHaveText(
      '1-50 lowercase letters, digits, or underscores.',
    );

    const tenantId = 'e2e_platform';
    await page.getByLabel('Tenant id').fill(tenantId);
    await page.getByLabel('Display name').fill('E2E Platform');
    await expect(page.locator('#tenant-id-error')).toHaveCount(0);
    await page.getByRole('button', { name: 'Create tenant' }).click();
    await expect(page.getByText('Tenant "E2E Platform" created')).toBeVisible();
    await expect(page).toHaveURL(new RegExp(`/platform/tenants/${tenantId}$`));
    await expect(page.getByRole('heading', { level: 1 })).toHaveText('E2E Platform');

    // The sidebar's contextual sub-nav shows this tenant's sections.
    await expect(sidebar.getByRole('link', { name: 'API Keys' })).toBeVisible();

    // --- API key (reached through the overview card) ---
    await main.getByRole('link', { name: /API Keys/ }).click();
    await expect(page).toHaveURL(new RegExp(`/platform/tenants/${tenantId}/api-keys$`));
    await expect(main.getByText('No API keys yet')).toBeVisible();

    await page.getByRole('button', { name: 'New key' }).click();
    const dialog = page.getByRole('dialog');
    await expect(dialog.getByText('Copy this key now — it will not be shown again.')).toBeVisible();
    await expect(dialog.locator('[data-raw-key]')).toHaveText(/^lk_\w+_x{32}$/);
    await dialog.getByRole('button', { name: 'Done' }).click();
    await expect(dialog).toHaveCount(0);
    await expect(main.getByText(/^lk_\w+…$/)).toBeVisible();

    // Revoke asks first, then the row shows "revoked".
    await main.getByRole('button', { name: 'Revoke key' }).click();
    const confirm = page.getByRole('alertdialog');
    await expect(confirm).toContainText('Any client still using it will start getting 401s immediately.');
    await confirm.getByRole('button', { name: 'Revoke', exact: true }).click();
    await toast(page, 'API key revoked');
    await expect(main.getByText('revoked', { exact: true })).toBeVisible();
    await expect(main.getByRole('button', { name: 'Revoke key' })).toHaveCount(0);

    // --- Tenant admin (reached through the sidebar sub-nav) ---
    await sidebar.getByRole('link', { name: 'Tenant Admins' }).click();
    await expect(page).toHaveURL(new RegExp(`/platform/tenants/${tenantId}/admin-users$`));
    await expect(main.getByText('No tenant admins yet')).toBeVisible();

    await page.getByRole('button', { name: 'New admin' }).click();
    await expect(page.getByText(`Grants sign-in access to ${tenantId}.`)).toBeVisible();
    const adminEmail = `${tenantId}@e2e.local`;
    await page.getByLabel('Email').fill(adminEmail);
    await page.getByLabel('Temporary password').fill('short');
    await page.getByRole('button', { name: 'Create admin' }).click();
    await expect(page.locator('#admin-password-error')).toHaveText('At least 12 characters.');
    await page.getByLabel('Temporary password').fill('E2eTenantAdminPass123!');
    // Let the dialog settle (the error line swaps back to the hint) before clicking again.
    await expect(page.locator('#admin-password-error')).toHaveCount(0);
    await page.getByRole('button', { name: 'Create admin' }).click();
    await toast(page, `Admin "${adminEmail}" created`);
    await expect(page.getByRole('cell', { name: adminEmail })).toBeVisible();

    // --- Back to the overview to suspend and reactivate ---
    await sidebar.getByRole('link', { name: 'Overview' }).click();
    await expect(page).toHaveURL(new RegExp(`/platform/tenants/${tenantId}$`));
    await page.getByRole('button', { name: 'Suspend' }).click();
    await toast(page, 'Tenant suspended');
    await page.getByRole('button', { name: 'Reactivate' }).click();
    await toast(page, 'Tenant reactivated');
    await expect(page.getByRole('button', { name: 'Suspend' })).toBeVisible();

    // The new tenant is listed.
    await page.getByRole('link', { name: 'Tenants' }).first().click();
    await expect(page.getByRole('cell', { name: 'E2E Platform' })).toBeVisible();
  });

  test('platform screens have no critical accessibility violations', async ({ page }) => {
    await mockApi(page);
    await loginAs(page, 'platform@loyalty.test', 'any-password');
    for (const path of [
      '/platform/tenants',
      '/platform/tenants/novapay',
      '/platform/tenants/novapay/api-keys',
    ]) {
      await page.goto(path);
      await expect(page.getByRole('heading', { level: 1 })).toBeVisible();
      const results = await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa']).analyze();
      const serious = results.violations.filter((v) => v.impact === 'critical' || v.impact === 'serious');
      expect(serious, `${path}: ${JSON.stringify(serious, null, 2)}`).toEqual([]);
    }
  });
});
