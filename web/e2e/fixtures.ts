import { APIRequestContext, Page, expect } from '@playwright/test';

export const PLATFORM_ADMIN = { email: 'admin@loyalty.local', password: 'Sup3rSecretPlatformAdmin!' };

export function uniqueTenantId(prefix: string): string {
  return `${prefix}_${Date.now().toString(36)}${Math.floor(Math.random() * 1000)}`;
}

export async function loginAs(page: Page, email: string, password: string): Promise<void> {
  await page.goto('/login');
  await page.getByLabel('Email').fill(email);
  await page.getByLabel('Password').fill(password);
  await page.getByRole('button', { name: /sign in/i }).click();
  // Wait for the login POST to actually resolve and the token to be persisted before returning —
  // a caller that immediately does a hard page.goto() to a deep route would otherwise race (and
  // typically cancel) the still-in-flight login request, landing back on /login unauthenticated.
  await page.waitForURL((url) => !url.pathname.startsWith('/login'));
}

/** Creates a fresh tenant + tenant_admin via the real API (fast path, not driven through the UI). */
export async function provisionTenant(
  request: APIRequestContext,
  namePrefix: string,
): Promise<{ tenantId: string; adminEmail: string; adminPassword: string }> {
  const loginRes = await request.post('/api/v1/auth/login', { data: PLATFORM_ADMIN });
  expect(loginRes.ok()).toBeTruthy();
  const { token } = (await loginRes.json()) as { token: string };
  const auth = { Authorization: `Bearer ${token}` };

  const tenantId = uniqueTenantId(namePrefix);
  const createTenantRes = await request.post('/api/v1/platform/tenants', {
    headers: auth,
    data: { id: tenantId, name: `E2E ${namePrefix}` },
  });
  expect(createTenantRes.ok()).toBeTruthy();

  const adminEmail = `${tenantId}@e2e.local`;
  const adminPassword = 'E2eTenantAdminPass123!';
  const createAdminRes = await request.post(`/api/v1/platform/tenants/${tenantId}/admin-users`, {
    headers: auth,
    data: { email: adminEmail, password: adminPassword, role: 'tenant_admin' },
  });
  expect(createAdminRes.ok()).toBeTruthy();

  return { tenantId, adminEmail, adminPassword };
}
