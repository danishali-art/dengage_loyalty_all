import { test, expect } from '@playwright/test';
import { apiAuth, loginAs, pick, provisionTenant, requireBackend } from './fixtures';

/** Port of `web/e2e/rules.e2e.spec.ts`, against the real backend. */
test.describe('Rules — DSL round-trip (real backend)', () => {
  requireBackend();

  test('tenant_admin creates a spend rule with a condition and can disable/delete it', async ({
    page,
    request,
  }) => {
    const { tenantId, adminEmail, adminPassword } = await provisionTenant(request, 'e2e_rules');
    const auth = await apiAuth(request, adminEmail, adminPassword);

    // Seed a program + account type through the API — this spec is about rules, not CRUD.
    const programRes = await request.post(`/api/v1/tenants/${tenantId}/programs`, {
      headers: auth,
      data: { name: 'Rules Program', status: 'active' },
    });
    const program = (await programRes.json()) as { id: string };
    await request.post(`/api/v1/tenants/${tenantId}/programs/${program.id}/account-types`, {
      headers: auth,
      data: { type: 'POINTS', name: 'E2E Points', config: { decimals: 0 } },
    });

    await loginAs(page, adminEmail, adminPassword);
    await page.goto(`/programs/${program.id}/rules`);
    await expect(page.getByRole('heading', { name: 'Rules' })).toBeVisible();

    await page.getByRole('button', { name: 'New rule' }).click();
    await expect(page).toHaveURL(new RegExp(`/programs/${program.id}/rules/new$`));

    await page.locator('#name').fill('Spend earns points');
    await pick(page, 'trigger', 'order.created');
    await pick(page, 'type', 'SpendRule');
    await pick(page, 'targetAccountTypeId', 'E2E Points (POINTS)');
    await page.locator('#calcRate').fill('2');

    // The grouped condition editor starts with one empty condition.
    await page.locator('input[aria-label="Field"]').first().fill('order.category');
    await page.locator('button[aria-label="Operator"]').first().click();
    await page.getByRole('option', { name: 'eq', exact: true }).click();
    await page.locator('input[aria-label="Value"]').first().fill('groceries');

    await page.getByRole('button', { name: 'Save' }).click();
    await expect(page).toHaveURL(new RegExp(`/programs/${program.id}/rules$`));
    const row = page.locator('tr', { hasText: 'Spend earns points' });
    await expect(row).toBeVisible();

    await row.getByRole('button', { name: 'Disable rule' }).click();
    await expect(row).toContainText(/disabled/i);

    await row.getByRole('button', { name: 'Delete rule' }).click();
    await page.getByRole('alertdialog').getByRole('button', { name: 'Delete', exact: true }).click();
    await expect(page.locator('main').getByText('Spend earns points')).toHaveCount(0);
  });
});
