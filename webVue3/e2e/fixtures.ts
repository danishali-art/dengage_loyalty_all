import { type APIRequestContext, type Page, expect, test } from '@playwright/test';

/** The seeded platform admin (same fixture as `web/e2e`). Only the real-backend specs use it. */
export const PLATFORM_ADMIN = { email: 'admin@loyalty.local', password: 'Sup3rSecretPlatformAdmin!' };

/** Real-backend specs (`*.e2e.spec.ts`) run only when the Api (and, for customers, the Consumer) is up. */
export function requireBackend(): void {
  test.skip(!process.env['E2E_BACKEND'], 'Needs the real backend: set E2E_BACKEND=1 with the Api on :5173.');
}

export function uniqueTenantId(prefix: string): string {
  return `${prefix}_${Date.now().toString(36)}${Math.floor(Math.random() * 1000)}`;
}

export async function loginAs(page: Page, email: string, password: string): Promise<void> {
  await page.goto('/login');
  await page.getByLabel('Email').fill(email);
  await page.getByLabel('Password').fill(password);
  await page.getByRole('button', { name: /sign in/i }).click();
  // Wait until the token is stored before returning, so a caller's next page.goto() doesn't race
  // (and cancel) the login request.
  await page.waitForURL((url) => !url.pathname.startsWith('/login'));
}

/** Picks an option in an `AppSelect` (plain or searchable) by the select's id. */
export async function pick(page: Page, id: string, option: string): Promise<void> {
  await page.locator(`#${id}`).click();
  await page.getByRole('option', { name: option, exact: true }).click();
}

/** Creates a fresh tenant + tenant_admin through the real API (fast path, not through the UI). */
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

/** Signs in a real-backend user through the API and returns the bearer header. */
export async function apiAuth(
  request: APIRequestContext,
  email: string,
  password: string,
): Promise<{ Authorization: string }> {
  const res = await request.post('/api/v1/auth/login', { data: { email, password } });
  expect(res.ok()).toBeTruthy();
  const { token } = (await res.json()) as { token: string };
  return { Authorization: `Bearer ${token}` };
}
