import type { Page, Route } from '@playwright/test';

/**
 * An in-memory stand-in for the platform part of the API, answered in the browser with
 * `page.route`, so the shell and platform specs run without the backend. Shapes follow the
 * generated DTOs (`TenantResponse`, `ApiKeyResponse`, `AdminUserResponse`); anything not modelled
 * here answers an empty page (GET) or 404, which is enough for screens these specs only pass through.
 *
 * Sign-in: an email starting with `platform` gets a platform_admin token, any other a tenant_admin
 * token for tenant `novapay`. The tokens are unsigned — nothing validates them here.
 */
export interface MockApi {
  tenants: { id: string; name: string; status: 'active' | 'suspended'; createdAt: string }[];
}

const b64 = (o: unknown) => Buffer.from(JSON.stringify(o)).toString('base64url');
const token = (claims: Record<string, unknown>) =>
  `${b64({ alg: 'none' })}.${b64({ exp: Math.floor(Date.now() / 1000) + 3600, ...claims })}.sig`;

const page1 = <T>(data: T[], url: URL) => {
  const page = Number(url.searchParams.get('page') ?? 1);
  const pageSize = Number(url.searchParams.get('pageSize') ?? 25);
  return { data: data.slice((page - 1) * pageSize, page * pageSize), page, pageSize, total: data.length };
};

export async function mockApi(page: Page): Promise<MockApi> {
  const state: MockApi = {
    tenants: [{ id: 'novapay', name: 'NovaPay Bank', status: 'active', createdAt: '2026-01-01T00:00:00Z' }],
  };
  const apiKeys = new Map<string, Record<string, unknown>[]>();
  const admins = new Map<string, Record<string, unknown>[]>();
  let seq = 0;
  const uuid = () => `00000000-0000-4000-8000-${String(++seq).padStart(12, '0')}`;

  await page.route('**/api/v1/**', async (route: Route) => {
    const req = route.request();
    const url = new URL(req.url());
    const path = url.pathname;
    const method = req.method();
    const json = (body: unknown, status = 200) => route.fulfill({ status, json: body });

    if (path === '/api/v1/auth/login') {
      const { email } = req.postDataJSON() as { email: string };
      const platform = email.startsWith('platform');
      return json({
        token: token({
          sub: email,
          email,
          role: platform ? 'platform_admin' : 'tenant_admin',
          tenant_id: platform ? null : 'novapay',
        }),
        tokenType: 'Bearer',
        expiresInSeconds: 3600,
      });
    }

    if (path === '/api/v1/platform/tenants') {
      if (method === 'POST') {
        const body = req.postDataJSON() as { id: string; name: string };
        if (state.tenants.some((t) => t.id === body.id)) {
          return json(
            {
              title: 'Conflict',
              status: 409,
              code: 'tenant_exists',
              errors: { id: ['Tenant id is taken.'] },
            },
            409,
          );
        }
        const tenant = { ...body, status: 'active' as const, createdAt: new Date().toISOString() };
        state.tenants.push(tenant);
        return json(tenant, 201);
      }
      return json(page1(state.tenants, url));
    }

    const m = /^\/api\/v1\/platform\/tenants\/([^/]+)(?:\/(api-keys|admin-users)(?:\/([^/]+))?)?$/.exec(path);
    if (m) {
      const [, tenantId, section, itemId] = m as unknown as [string, string, string?, string?];
      const tenant = state.tenants.find((t) => t.id === tenantId);
      if (!tenant) return json({ title: 'Tenant not found.', status: 404, code: 'not_found' }, 404);

      if (!section) {
        if (method === 'PATCH') Object.assign(tenant, req.postDataJSON());
        return json(tenant);
      }
      if (section === 'api-keys') {
        const keys = apiKeys.get(tenantId) ?? [];
        apiKeys.set(tenantId, keys);
        if (method === 'POST') {
          const prefix = `lk_${Math.random().toString(36).slice(2, 8)}`;
          const created = { id: uuid(), prefix, createdAt: new Date().toISOString() };
          keys.unshift(created);
          return json({ ...created, rawKey: `${prefix}_${'x'.repeat(32)}` }, 201);
        }
        if (method === 'DELETE' && itemId) {
          const key = keys.find((k) => k['id'] === itemId);
          if (key) key['revokedAt'] = new Date().toISOString();
          return route.fulfill({ status: 204 });
        }
        return json(page1(keys, url));
      }
      const users = admins.get(tenantId) ?? [];
      admins.set(tenantId, users);
      if (method === 'POST') {
        const body = req.postDataJSON() as { email: string; role: string };
        const user = {
          id: uuid(),
          email: body.email,
          role: body.role,
          tenantId,
          status: 'active',
          createdAt: new Date().toISOString(),
        };
        users.push(user);
        return json(user, 201);
      }
      return json(page1(users, url));
    }

    if (method === 'GET') return json({ data: [], page: 1, pageSize: 25, total: 0 });
    return json({ title: 'Not mocked', status: 404, code: 'not_found' }, 404);
  });

  return state;
}
