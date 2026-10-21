import { expect, test } from '@playwright/test';
import { PLATFORM_ADMIN, apiAuth, loginAs, provisionTenant, requireBackend } from './fixtures';

/** Public.Api (event ingestion by API key), which the dev-server proxy doesn't cover. */
const PUBLIC_API_URL = process.env['PUBLIC_API_URL'] ?? 'http://localhost:5175';

/**
 * Port of `web/e2e/customers.e2e.spec.ts`: rule → event through the API-key gateway → Consumer →
 * ledger → Customer 360. Needs Public.Api and the Consumer running beside the Api, not just the Api.
 */
test.describe('Customers — event ingestion round-trip (real backend)', () => {
  requireBackend();

  test('an ingested order.created event shows up on the customer profile', async ({ page, request }) => {
    // The Consumer's RuleSyncService delta-polls every 30s; the test waits that out.
    test.setTimeout(90_000);

    const { tenantId, adminEmail, adminPassword } = await provisionTenant(request, 'e2e_customers');
    const auth = await apiAuth(request, adminEmail, adminPassword);

    const programRes = await request.post(`/api/v1/tenants/${tenantId}/programs`, {
      headers: auth,
      data: { name: 'Customers Program', status: 'active' },
    });
    const program = (await programRes.json()) as { id: string };

    const accountTypeRes = await request.post(
      `/api/v1/tenants/${tenantId}/programs/${program.id}/account-types`,
      { headers: auth, data: { type: 'POINTS', name: 'E2E Points', config: { decimals: 0 } } },
    );
    const accountType = (await accountTypeRes.json()) as { id: string };

    await request.post(`/api/v1/tenants/${tenantId}/programs/${program.id}/rules`, {
      headers: auth,
      data: {
        name: 'Spend earns points',
        trigger: 'order.created',
        targetAccountTypeId: accountType.id,
        type: 'SpendRule',
        calculation: { rate: '1' },
        priority: 0,
        stackable: true,
      },
    });

    // 1.3.CL: the Consumer only evaluates programs that are published and active — publish once
    // the rule exists, so the published snapshot includes it.
    const publishRes = await request.post(`/api/v1/tenants/${tenantId}/programs/${program.id}/publish`, {
      headers: auth,
    });
    expect(publishRes.ok()).toBeTruthy();

    const platformAuth = await apiAuth(request, PLATFORM_ADMIN.email, PLATFORM_ADMIN.password);
    const apiKeyRes = await request.post(`/api/v1/platform/tenants/${tenantId}/api-keys`, {
      headers: platformAuth,
    });
    const { rawKey } = (await apiKeyRes.json()) as { rawKey: string };

    await loginAs(page, adminEmail, adminPassword);

    // Wait out the Consumer's 30s rule-sync cycle before publishing, or the event is processed
    // against a rule-less cache for this brand-new tenant.
    await page.waitForTimeout(35_000);

    const contactKey = `e2e_customer_${Date.now()}`;
    // Event ingestion is served by Public.Api, not the Api the portal proxies to.
    const eventRes = await request.post(`${PUBLIC_API_URL}/api/v1/tenants/${tenantId}/events/order-created`, {
      headers: { 'X-Api-Key': rawKey },
      data: { contactKey, amount: '50' },
    });
    expect(eventRes.status()).toBe(202);

    await page.goto(`/customers/${contactKey}`);
    await expect(async () => {
      await page.reload();
      await expect(page.locator('main')).toContainText('E2E Points');
      await expect(page.locator('main')).toContainText('50');
    }).toPass({ timeout: 15_000, intervals: [1000, 2000, 3000] });

    // The customer is also in the browsable list, not just reachable by direct lookup.
    await page.goto('/customers');
    await page.getByPlaceholder('Search by contact key, e.g. cust_1001').fill(contactKey);
    await page.getByRole('button', { name: 'Search' }).click();
    await expect(page.getByRole('cell', { name: contactKey })).toBeVisible();
  });
});
