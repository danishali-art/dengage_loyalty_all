import { APIRequestContext, Page, expect, test } from '@playwright/test';
import { loginAs, provisionTenant } from './fixtures';

async function login(request: APIRequestContext, email: string, password: string): Promise<string> {
  const res = await request.post('/api/v1/auth/login', { data: { email, password } });
  expect(res.ok()).toBeTruthy();
  const { token } = (await res.json()) as { token: string };
  return token;
}

/** Selects an option from a `SearchableSelect` combobox identified by its accessible label. */
async function chooseSearchable(page: Page, labelName: string, optionName: string): Promise<void> {
  await page.getByRole('combobox', { name: labelName }).click();
  await page.getByRole('option', { name: optionName }).click();
}

test.describe('Rules — DSL round-trip', () => {
  test('tenant_admin creates a spend rule with a condition and can disable/delete it', async ({
    page,
    request,
  }) => {
    const { tenantId, adminEmail, adminPassword } = await provisionTenant(request, 'e2e_rules');
    const token = await login(request, adminEmail, adminPassword);
    const auth = { Authorization: `Bearer ${token}` };

    // Seed a program + account type directly via the API — this spec is about Rules, not re-testing CRUD.
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

    await page.getByLabel('Name').fill('Spend earns points');
    await chooseSearchable(page, 'Trigger event', 'order.created');
    await page.locator('#type').selectOption('SpendRule');
    await chooseSearchable(page, 'Target account', 'E2E Points (POINTS)');
    await page.locator('#rate').fill('2');

    await page.getByRole('button', { name: 'Add condition' }).click();
    const conditionRow = page.locator('div.grid', { has: page.getByPlaceholder('field, e.g. order.category') });
    await conditionRow.getByPlaceholder('field, e.g. order.category').fill('order.category');
    await conditionRow.getByRole('combobox').click();
    await page.getByRole('option', { name: 'eq', exact: true }).click();
    await conditionRow.locator('input.field-input').nth(1).fill('groceries');

    await page.getByRole('button', { name: 'Save' }).click();
    await expect(page).toHaveURL(new RegExp(`/programs/${program.id}/rules$`));
    await expect(page.getByText('Spend earns points')).toBeVisible();

    // Disable, then delete
    await page.getByRole('button', { name: 'Disable rule' }).click();
    await expect(page.locator('main').getByText('disabled', { exact: true })).toBeVisible();

    await page.getByRole('button', { name: 'Delete rule' }).click();
    await page.getByRole('dialog').getByRole('button', { name: 'Delete', exact: true }).click();
    await expect(page.getByText('Spend earns points')).toHaveCount(0);
  });
});
