import { Page, test, expect } from '@playwright/test';
import { PLATFORM_ADMIN, loginAs, uniqueTenantId } from './fixtures';

/**
 * Clicks a button that opens a CDK dialog, then gives the freshly-mounted dialog component one
 * settled render before the caller starts filling it in. A dialog is a brand-new zoneless OnPush
 * component instance each time it opens — its reactive-forms bindings aren't wired up until that
 * first change-detection pass runs, so typing into it in the same tick a real user never could
 * (a scripted `.fill()` immediately after the open click) can race that pass and have the value
 * silently dropped once a later action triggers the delayed first render.
 */
async function openDialog(page: Page, buttonName: string): Promise<void> {
  await page.getByRole('button', { name: buttonName }).click();
  await page.waitForTimeout(300);
}

test.describe('Platform — tenant provisioning', () => {
  test('platform_admin creates a tenant, an API key, and a tenant admin', async ({ page }) => {
    const main = page.locator('main');
    const sidebar = page.locator('nav[aria-label="Primary"]');

    await loginAs(page, PLATFORM_ADMIN.email, PLATFORM_ADMIN.password);
    await expect(page).toHaveURL(/\/select-tenant$/);

    await page.goto('/platform/tenants');
    await expect(page.getByRole('heading', { name: 'Tenants' })).toBeVisible();

    const tenantId = uniqueTenantId('e2e_platform');
    await openDialog(page, 'New tenant');
    await page.getByLabel('Tenant id').fill(tenantId);
    await page.getByLabel('Display name').fill(`E2E Platform ${tenantId}`);
    await page.getByRole('button', { name: 'Create tenant' }).click();

    await expect(page.getByText(`Tenant "E2E Platform ${tenantId}" created`)).toBeVisible();
    await page.getByRole('cell', { name: `E2E Platform ${tenantId}` }).click();
    await expect(page).toHaveURL(new RegExp(`/platform/tenants/${tenantId}$`));

    // The Sidebar's contextual sub-nav should now show this tenant's sections.
    await expect(sidebar.getByRole('link', { name: 'API Keys' })).toBeVisible();

    // --- API key (reached via the Overview page's summary card) ---
    await main.getByRole('link', { name: /API Keys/ }).click();
    await expect(page).toHaveURL(new RegExp(`/platform/tenants/${tenantId}/api-keys$`));

    await openDialog(page, 'New key');
    await expect(page.getByText('Copy this key now')).toBeVisible();
    await page.getByRole('button', { name: 'Done' }).click();
    await expect(main.getByText(/^lk_/)).toBeVisible();

    // --- Tenant admin (reached via the Sidebar's contextual sub-nav) ---
    await sidebar.getByRole('link', { name: 'Tenant Admins' }).click();
    await expect(page).toHaveURL(new RegExp(`/platform/tenants/${tenantId}/admin-users$`));

    await openDialog(page, 'New admin');
    const adminEmail = `${tenantId}@e2e.local`;
    await page.getByLabel('Email').fill(adminEmail);
    await page.getByLabel('Temporary password').fill('E2eTenantAdminPass123!');
    await page.getByRole('button', { name: 'Create admin' }).click();
    await expect(page.getByText(`Admin "${adminEmail}" created`)).toBeVisible();
    await expect(page.getByRole('cell', { name: adminEmail })).toBeVisible();

    // --- Back to Overview for Suspend / reactivate ---
    await sidebar.getByRole('link', { name: 'Overview' }).click();
    await expect(page).toHaveURL(new RegExp(`/platform/tenants/${tenantId}$`));
    await page.getByRole('button', { name: 'Suspend' }).click();
    await expect(page.getByRole('button', { name: 'Reactivate' })).toBeVisible();
  });
});
