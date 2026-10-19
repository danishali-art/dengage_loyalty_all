import { test, expect, type Page } from '@playwright/test';
import { loginAs, pick, provisionTenant, requireBackend } from './fixtures';

/**
 * Port of `web/e2e/programs.e2e.spec.ts`, against the real backend. The Angular spec's "Coupon type"
 * reward field no longer exists; the reward here is a cashback bought with points (the creatable
 * types are cashback and tier_upgrade).
 */
test.describe('Programs — program, account types, tiers, rewards (real backend)', () => {
  requireBackend();

  async function toast(page: Page, text: string): Promise<void> {
    await expect(page.locator('[data-sonner-toast]', { hasText: text })).toBeVisible();
    await expect(page.locator('[data-sonner-toast]')).toHaveCount(0, { timeout: 10_000 });
  }

  test('tenant_admin builds a program end to end via the sub-pages', async ({ page, request }) => {
    test.setTimeout(120_000);
    const { adminEmail, adminPassword } = await provisionTenant(request, 'e2e_programs');
    // Scoped to <main>: toasts render outside it and often echo the same entity name.
    const main = page.locator('main');
    const sidebar = page.getByRole('navigation', { name: 'Primary' });
    const dialog = page.getByRole('dialog');

    await loginAs(page, adminEmail, adminPassword);
    await expect(page).toHaveURL(/\/programs$/);

    await page.getByRole('button', { name: 'New program' }).click();
    await page.getByLabel('Program name').fill('E2E Rewards');
    await page.getByLabel('Description').fill('Created by Playwright');
    await page.getByRole('button', { name: 'Create program' }).click();
    await expect(page).toHaveURL(/\/programs\/[0-9a-f-]{36}$/);
    await expect(page.getByRole('heading', { name: 'E2E Rewards' })).toBeVisible();
    await expect(sidebar.getByRole('link', { name: 'Account Types' })).toBeVisible();

    // --- Account types (through the overview card) ---
    await main.getByRole('link', { name: /Account Types/ }).click();
    await expect(page).toHaveURL(/\/account-types$/);

    await page.getByRole('button', { name: 'Add account type' }).click();
    await page.getByLabel('Account name').fill('E2E Points');
    await pick(page, 'accountTypeType', '⭐ POINTS');
    await page.locator('#decimals').fill('0');
    await dialog.getByRole('button', { name: 'Create', exact: true }).click();
    await expect(main.getByText('E2E Points')).toBeVisible();

    await page.getByRole('button', { name: 'Add account type' }).click();
    await page.getByLabel('Account name').fill('E2E Wallet');
    await pick(page, 'accountTypeType', '💳 CASH');
    await pick(page, 'currency', 'SAR');
    await page.locator('#cashDecimals').fill('2');
    await dialog.getByRole('button', { name: 'Create', exact: true }).click();
    await expect(main.getByText('E2E Wallet')).toBeVisible();

    // --- Tiers (through the sidebar sub-nav) ---
    await sidebar.getByRole('link', { name: 'Tiers' }).click();
    await expect(page).toHaveURL(/\/tiers$/);
    for (const [slug, name, min] of [
      ['bronze', 'Bronze', '0'],
      ['gold', 'Gold', '1000'],
    ] as const) {
      await page.getByRole('button', { name: 'Add tier' }).click();
      await page.getByLabel('Name (slug)').fill(slug);
      await page.getByLabel('Display name').fill(name);
      await page.getByLabel('Minimum qualifying points').fill(min);
      await dialog.getByRole('button', { name: 'Create', exact: true }).click();
      await expect(main.getByText(name, { exact: true })).toBeVisible();
    }

    const rows = page.locator('table').filter({ hasText: 'Min points' }).locator('tbody tr');
    await expect(rows).toHaveCount(2);
    await rows.nth(1).getByRole('button', { name: 'Move up' }).click();
    await expect(rows.nth(0)).toContainText('Gold');
    await rows.nth(0).getByRole('button', { name: 'Move down' }).click();
    await expect(rows.nth(0)).toContainText('Bronze');

    // --- Rewards (through the sidebar sub-nav) ---
    await sidebar.getByRole('link', { name: 'Rewards' }).click();
    await expect(page).toHaveURL(/\/rewards$/);
    await page.getByRole('button', { name: 'Add reward' }).click();
    await page.locator('#rewardName').fill('welcome_bonus');
    await page.getByLabel('Display name').fill('Welcome Bonus');
    await pick(page, 'acquisition', 'Purchase with points');
    await page.locator('#pointsPrice').fill('100');
    await pick(page, 'pointsAccountTypeId', 'E2E Points');
    await pick(page, 'rewardType', 'Cashback');
    await page.locator('#cashbackAmount').fill('5');
    await pick(page, 'cashbackCurrency', 'SAR');
    await pick(page, 'cashAccountTypeId', 'E2E Wallet');
    await dialog.getByRole('button', { name: 'Create', exact: true }).click();
    await expect(main.getByText('Welcome Bonus')).toBeVisible();

    // --- Back to the overview to edit the program info ---
    await sidebar.getByRole('link', { name: 'Overview' }).click();
    await expect(page).toHaveURL(/\/programs\/[0-9a-f-]{36}$/);
    await page.getByLabel('Program name').fill('E2E Rewards (edited)');
    await page.getByRole('button', { name: 'Save' }).click();
    await toast(page, 'Program saved');
  });
});
