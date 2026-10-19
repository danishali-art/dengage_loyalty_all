import { beforeEach, describe, expect, it } from 'vitest';
import { createPinia, setActivePinia } from 'pinia';
import type { RouteLocationNormalized } from 'vue-router';
import { useSessionStore, useTenantContextStore } from '@/shared/model';
import { authGuard, roleGuard, tenantResolvedGuard } from './guards';

function jwt(claims: Record<string, unknown>): string {
  const b64 = (o: unknown) => btoa(JSON.stringify(o)).replace(/=+$/, '');
  return `${b64({ alg: 'none' })}.${b64({ exp: Math.floor(Date.now() / 1000) + 3600, ...claims })}.sig`;
}

const to = { fullPath: '/programs/p1' } as RouteLocationNormalized;
const from = {} as RouteLocationNormalized;
// Guards are plain functions here; vue-router's third `next` argument isn't used by them.
const run = (guard: (...args: never[]) => unknown) =>
  (guard as (t: unknown, f: unknown) => unknown)(to, from);

describe('route guards', () => {
  beforeEach(() => {
    localStorage.clear();
    setActivePinia(createPinia());
  });

  it('sends an anonymous visitor to /login with a return url', () => {
    expect(run(authGuard)).toEqual({ path: '/login', query: { returnUrl: '/programs/p1' } });
  });

  it('lets a signed-in user through', () => {
    useSessionStore().setToken(jwt({ sub: 'u', role: 'platform_admin', tenant_id: null }));
    expect(run(authGuard)).toBe(true);
  });

  it('forbids the platform area to a tenant_admin', () => {
    useSessionStore().setToken(jwt({ sub: 'u', role: 'tenant_admin', tenant_id: 'novapay' }));
    expect(run(roleGuard('platform_admin'))).toEqual({ path: '/forbidden' });
  });

  it("adopts a tenant_admin's JWT tenant", () => {
    useSessionStore().setToken(jwt({ sub: 'u', role: 'tenant_admin', tenant_id: 'novapay' }));
    expect(run(tenantResolvedGuard)).toBe(true);
    expect(useTenantContextStore().activeTenantId).toBe('novapay');
  });

  it('sends a platform_admin without a tenant to the picker', () => {
    useSessionStore().setToken(jwt({ sub: 'u', role: 'platform_admin', tenant_id: null }));
    expect(run(tenantResolvedGuard)).toEqual({ path: '/select-tenant' });
  });
});
