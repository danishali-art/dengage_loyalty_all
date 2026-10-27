import { test, expect, type Page } from '@playwright/test';
import { PLATFORM_ADMIN, loginAs, requireBackend, uniqueTenantId } from './fixtures';

/** Tenant provisioning, against the real backend. */
test.describe('Platform — tenant provisioning (real backend)', () => {
  requireBackend();

  /** Toasts sit over the page header's actions; wait each one out before the next header click. */
  async function toast(page: Page, text: string): Promise<void> {
    await expect(page.locator('[data-sonner-toast]', { hasText: text })).toBeVisible();
    await expect(page.locator('[data-sonner-toast]')).toHaveCount(0, { timeout: 10_000 });
  }

  test('platform_admin creates a tenant, an API key, and a tenant admin', async ({ page }) => {
    test.setTimeout(90_000);
    const main = page.locator('main');
    const sidebar = page.getByRole('navigation', { name: 'Primary' });

    await loginAs(page, PLATFORM_ADMIN.email, PLATFORM_ADMIN.password);
    await expect(page).toHaveURL(/\/select-tenant$/);

    await page.goto('/platform/tenants');
    await expect(page.getByRole('heading', { name: 'Tenants' })).toBeVisible();

    const tenantId = uniqueTenantId('e2e_platform');
    await page.getByRole('button', { name: 'New tenant' }).click();
    await page.getByLabel('Tenant id').fill(tenantId);
    await page.getByLabel('Display name').fill(`E2E Platform ${tenantId}`);
    await page.getByRole('button', { name: 'Create tenant' }).click();
    await toast(page, `Tenant "E2E Platform ${tenantId}" created`);
    await expect(page).toHaveURL(new RegExp(`/platform/tenants/${tenantId}$`));
    await expect(sidebar.getByRole('link', { name: 'API Keys' })).toBeVisible();

    await main.getByRole('link', { name: /API Keys/ }).click();
    await expect(page).toHaveURL(new RegExp(`/platform/tenants/${tenantId}/api-keys$`));
    await page.getByRole('button', { name: 'New key' }).click();
    await expect(page.getByText('Copy this key now')).toBeVisible();
    await page.getByRole('dialog').getByRole('button', { name: 'Done' }).click();
    await expect(main.getByText(/^lk_/)).toBeVisible();

    await sidebar.getByRole('link', { name: 'Tenant Admins' }).click();
    await expect(page).toHaveURL(new RegExp(`/platform/tenants/${tenantId}/admin-users$`));
    await page.getByRole('button', { name: 'New admin' }).click();
    const adminEmail = `${tenantId}@e2e.local`;
    await page.getByLabel('Email').fill(adminEmail);
    await page.getByLabel('Temporary password').fill('E2eTenantAdminPass123!');
    await page.getByRole('button', { name: 'Create admin' }).click();
    await toast(page, `Admin "${adminEmail}" created`);
    await expect(page.getByRole('cell', { name: adminEmail })).toBeVisible();

    await sidebar.getByRole('link', { name: 'Overview' }).click();
    await expect(page).toHaveURL(new RegExp(`/platform/tenants/${tenantId}$`));
    await page.getByRole('button', { name: 'Suspend' }).click();
    await expect(page.getByRole('button', { name: 'Reactivate' })).toBeVisible();
  });
});
