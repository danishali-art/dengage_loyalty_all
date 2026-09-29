import { test, expect, Page } from '@playwright/test';
import { loginAs, provisionTenant } from './fixtures';

/** Selects an option from a `SearchableSelect` combobox identified by its accessible label. */
async function chooseSearchable(page: Page, labelName: string, optionName: string): Promise<void> {
  await page.getByRole('combobox', { name: labelName }).click();
  await page.getByRole('option', { name: optionName }).click();
}

/**
 * Clicks a button that opens a CDK dialog, then gives the freshly-mounted dialog component one
 * settled render before the caller starts filling it in. A dialog is a brand-new zoneless OnPush
 * component instance each time it opens — its reactive-forms bindings aren't wired up until that
 * first change-detection pass runs, so typing into it in the same tick a real user never could
 * (a scripted `.fill()` immediately after the open click) can race that pass and have the value
 * silently dropped once a later control (e.g. a `<select>`) triggers the delayed first render.
 */
async function openDialog(page: Page, buttonName: string): Promise<void> {
  await page.getByRole('button', { name: buttonName }).click();
  await page.waitForTimeout(300);
}

test.describe('Programs — program, account types, tiers, rewards', () => {
  test('tenant_admin builds a program end to end via the new sub-pages', async ({ page, request }) => {
    const { adminEmail, adminPassword } = await provisionTenant(request, 'e2e_programs');

    // Scoped to <main> throughout: toast notifications render outside it, and often echo the
    // same entity name (e.g. 'Account type "E2E Wallet" created'), which would otherwise make
    // a plain getByText(...) match two elements.
    const main = page.locator('main');
    const sidebar = page.locator('nav[aria-label="Primary"]');

    await loginAs(page, adminEmail, adminPassword);
    await expect(page).toHaveURL(/\/programs$/);

    // Create program -> lands on the (slimmed) Overview page
    await openDialog(page, 'New program');
    await page.getByLabel('Program name').fill('E2E Rewards');
    await page.getByLabel('Description').fill('Created by Playwright');
    await page.getByRole('button', { name: 'Create program' }).click();
    await expect(page).toHaveURL(/\/programs\/[0-9a-f-]{36}$/);
    await expect(page.getByRole('heading', { name: 'E2E Rewards' })).toBeVisible();

    // The Sidebar's contextual sub-nav should now show this program's sections.
    await expect(sidebar.getByRole('link', { name: 'Account Types' })).toBeVisible();

    // --- Account Types (reached via the Overview page's summary card) ---
    await main.getByRole('link', { name: /Account Types/ }).click();
    await expect(page).toHaveURL(/\/account-types$/);

    await openDialog(page, 'Add account type');
    await page.getByLabel('Account name').fill('E2E Points');
    await page.locator('#type').selectOption('POINTS');
    await page.locator('#decimals').fill('0');
    await page.getByRole('button', { name: 'Create', exact: true }).click();
    await expect(main.locator('span', { hasText: 'E2E Points' })).toBeVisible();

    await openDialog(page, 'Add account type');
    await page.getByLabel('Account name').fill('E2E Wallet');
    await page.locator('#type').selectOption('CASH');
    await page.locator('#currency').fill('SAR');
    await page.locator('#cashDecimals').fill('2');
    await page.getByRole('button', { name: 'Create', exact: true }).click();
    await expect(main.locator('span', { hasText: 'E2E Wallet' })).toBeVisible();

    // --- Tiers (reached via the Sidebar's contextual sub-nav) ---
    await sidebar.getByRole('link', { name: 'Tiers' }).click();
    await expect(page).toHaveURL(/\/tiers$/);

    await openDialog(page, 'Add tier');
    await page.getByLabel('Name (slug)').fill('bronze');
    await page.getByLabel('Display name').fill('Bronze');
    await page.getByLabel('Minimum qualifying points').fill('0');
    await page.getByRole('button', { name: 'Create', exact: true }).click();
    await expect(main.getByText('Bronze')).toBeVisible();

    await openDialog(page, 'Add tier');
    await page.getByLabel('Name (slug)').fill('gold');
    await page.getByLabel('Display name').fill('Gold');
    await page.getByLabel('Minimum qualifying points').fill('1000');
    await page.getByRole('button', { name: 'Create', exact: true }).click();
    await expect(main.getByText('Gold')).toBeVisible();

    // Reorder: move Gold above Bronze, then back
    const rows = page.locator('table').filter({ hasText: 'Min points' }).locator('tbody tr');
    await expect(rows).toHaveCount(2);
    await rows.nth(1).getByRole('button', { name: 'Move up' }).click();
    await expect(rows.nth(0)).toContainText('Gold');
    await rows.nth(0).getByRole('button', { name: 'Move down' }).click();
    await expect(rows.nth(0)).toContainText('Bronze');

    // --- Rewards (reached via the Sidebar's contextual sub-nav) ---
    await sidebar.getByRole('link', { name: 'Rewards' }).click();
    await expect(page).toHaveURL(/\/rewards$/);

    await openDialog(page, 'Add reward');
    await page.getByLabel('Name (slug)').fill('welcome_bonus');
    await page.getByLabel('Display name').fill('Welcome Bonus');
    await page.getByLabel('Coupon type').fill('WELCOME_BONUS');
    await page.locator('#acquisition').selectOption('points_purchase');
    await page.locator('#pointsPrice').fill('100');
    await chooseSearchable(page, 'Points account', 'E2E Points');
    await page.getByRole('button', { name: 'Create', exact: true }).click();
    await expect(main.getByText('Welcome Bonus')).toBeVisible();

    // --- Back to Overview to edit the program info ---
    await sidebar.getByRole('link', { name: 'Overview' }).click();
    await expect(page).toHaveURL(/\/programs\/[0-9a-f-]{36}$/);
    await page.getByLabel('Program name').fill('E2E Rewards (edited)');
    await page.getByRole('button', { name: 'Save' }).click();
    await expect(page.getByText('Program saved')).toBeVisible(); // toast — fine to match outside <main>
  });
});
