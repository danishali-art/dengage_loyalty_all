import { expect, test } from '@playwright/test';
import { PLATFORM_ADMIN, loginAs, provisionTenant } from './fixtures';

/**
 * Proves the full chain: UI-created program/rule -> event ingested via the API-key gateway ->
 * LoyaltySaaS.Consumer processes it from RabbitMQ -> a ledger entry lands in Postgres -> the
 * Customer 360 screen reads it back. Requires LoyaltySaaS.Consumer running alongside the API
 * (documented in the plan) — this is the one spec that exercises the pre-existing event
 * pipeline, not just the new API/UI layer.
 */
test.describe('Customers — event ingestion round-trip', () => {
  test('an ingested order.created event shows up on the customer profile', async ({ page, request }) => {
    // The Consumer's RuleSyncService delta-polls every 30s (see the comment below) — this test
    // waits out that cycle, so it needs more than Playwright's default 30s test timeout.
    test.setTimeout(90_000);

    const { tenantId, adminEmail, adminPassword } = await provisionTenant(request, 'e2e_customers');

    const adminLoginRes = await request.post('/api/v1/auth/login', { data: { email: adminEmail, password: adminPassword } });
    const { token } = (await adminLoginRes.json()) as { token: string };
    const auth = { Authorization: `Bearer ${token}` };

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

    // Platform admin issues the API key (platform-scoped route).
    const platformLoginRes = await request.post('/api/v1/auth/login', { data: PLATFORM_ADMIN });
    const { token: platformToken } = (await platformLoginRes.json()) as { token: string };
    const apiKeyRes = await request.post(`/api/v1/platform/tenants/${tenantId}/api-keys`, {
      headers: { Authorization: `Bearer ${platformToken}` },
    });
    const { rawKey } = (await apiKeyRes.json()) as { rawKey: string };

    await loginAs(page, adminEmail, adminPassword);

    // The Consumer's RuleSyncService only picks up newly-created rules on its 30s delta poll
    // (RuleSyncService.DeltaInterval) — publishing the event before that cycle runs would have
    // it processed with a stale (rule-less) cache for this brand-new tenant, silently matching
    // nothing. So wait out the cycle here, before publishing, rather than polling longer after.
    await page.waitForTimeout(35_000);

    const contactKey = `e2e_customer_${Date.now()}`;
    const eventRes = await request.post(`/api/v1/tenants/${tenantId}/events/order-created`, {
      headers: { 'X-Api-Key': rawKey },
      data: { contactKey, amount: '50' },
    });
    expect(eventRes.status()).toBe(202);

    await page.goto(`/customers/${contactKey}`);

    // Processing itself is fast once the rule is cached — this just absorbs normal async lag.
    await expect(async () => {
      await page.reload();
      const balanceRow = page.locator('div', { hasText: 'E2E Points' }).last();
      await expect(balanceRow).toContainText('50');
      await expect(page.getByText('+50')).toBeVisible();
    }).toPass({ timeout: 15_000, intervals: [1000, 2000, 3000] });

    // The customer also shows up in the browsable list, not just via direct lookup.
    await page.goto('/customers');
    await page.getByPlaceholder('Filter by contact key…').fill(contactKey);
    await expect(page.getByRole('cell', { name: contactKey })).toBeVisible();
  });
});
