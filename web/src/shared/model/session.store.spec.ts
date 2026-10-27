import { beforeEach, describe, expect, it } from 'vitest';
import { createPinia, setActivePinia } from 'pinia';
import { useSessionStore } from './session.store';

function base64url(json: unknown): string {
  return btoa(JSON.stringify(json)).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '');
}

function fakeJwt(claims: Record<string, unknown>): string {
  const now = Math.floor(Date.now() / 1000);
  return `${base64url({ alg: 'none' })}.${base64url({ iat: now, exp: now + 3600, ...claims })}.sig`;
}

describe('useSessionStore', () => {
  beforeEach(() => {
    localStorage.clear();
    setActivePinia(createPinia());
  });

  it('starts unauthenticated', () => {
    const store = useSessionStore();
    expect(store.isAuthenticated).toBe(false);
    expect(store.principal).toBeNull();
  });

  it('derives a principal from a platform_admin token', () => {
    const store = useSessionStore();
    store.setToken(fakeJwt({ sub: 'u1', role: 'platform_admin', tenant_id: null }));
    expect(store.isAuthenticated).toBe(true);
    expect(store.role).toBe('platform_admin');
    expect(store.isPlatformAdmin).toBe(true);
    expect(store.principal?.tenantId).toBeNull();
  });

  it('derives a tenant-bound principal from a tenant_admin token', () => {
    const store = useSessionStore();
    store.setToken(fakeJwt({ sub: 'u2', role: 'tenant_admin', tenant_id: 'novapay' }));
    expect(store.role).toBe('tenant_admin');
    expect(store.principal?.tenantId).toBe('novapay');
    expect(store.hasRole('tenant_admin')).toBe(true);
  });

  it('treats an expired token as no session', () => {
    const store = useSessionStore();
    store.setToken(
      fakeJwt({ sub: 'u', role: 'tenant_admin', tenant_id: 't', exp: Math.floor(Date.now() / 1000) - 3600 }),
    );
    expect(store.isAuthenticated).toBe(false);
  });

  it('clear() drops the session and the stored token', () => {
    const store = useSessionStore();
    store.setToken(fakeJwt({ sub: 'u1', role: 'platform_admin', tenant_id: null }));
    store.clear();
    expect(store.isAuthenticated).toBe(false);
    expect(localStorage.getItem('loyalty.portal.token')).toBeNull();
  });
});
