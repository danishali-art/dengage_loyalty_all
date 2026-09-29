import { TestBed } from '@angular/core/testing';
import { SessionStore } from './session.store';
import { TokenStorage, InMemoryTokenStorage } from './token-storage';
import { makeFakeJwt } from './stub-auth.service';

describe('SessionStore', () => {
  let store: SessionStore;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [SessionStore, { provide: TokenStorage, useClass: InMemoryTokenStorage }],
    });
    store = TestBed.inject(SessionStore);
  });

  it('starts unauthenticated', () => {
    expect(store.isAuthenticated()).toBe(false);
    expect(store.principal()).toBeNull();
  });

  it('derives a principal from a platform_admin token', () => {
    store.setToken(makeFakeJwt({ sub: 'u1', role: 'platform_admin', tenant_id: null }));
    expect(store.isAuthenticated()).toBe(true);
    expect(store.role()).toBe('platform_admin');
    expect(store.isPlatformAdmin()).toBe(true);
    expect(store.principal()?.tenantId).toBeNull();
  });

  it('derives a tenant-bound principal from a tenant_admin token', () => {
    store.setToken(makeFakeJwt({ sub: 'u2', role: 'tenant_admin', tenant_id: 'novapay' }));
    expect(store.role()).toBe('tenant_admin');
    expect(store.principal()?.tenantId).toBe('novapay');
    expect(store.hasRole('tenant_admin')).toBe(true);
  });

  it('treats an expired token as no session', () => {
    const header = btoa(JSON.stringify({ alg: 'none' }));
    const past = Math.floor(Date.now() / 1000) - 3600;
    const payload = btoa(JSON.stringify({ sub: 'u', role: 'tenant_admin', tenant_id: 't', exp: past }));
    store.setToken(`${header}.${payload}.x`);
    expect(store.isAuthenticated()).toBe(false);
  });

  it('clear() drops the session', () => {
    store.setToken(makeFakeJwt({ sub: 'u1', role: 'platform_admin', tenant_id: null }));
    store.clear();
    expect(store.isAuthenticated()).toBe(false);
  });
});
