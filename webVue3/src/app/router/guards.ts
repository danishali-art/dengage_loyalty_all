import type { NavigationGuard } from 'vue-router';
import { isFeatureEnabled, type FeatureFlag } from '@/shared/config/app-config';
import { useSessionStore, useTenantContextStore, type UserRole } from '@/shared/model';

/** Port of web core/auth/guards.ts and core/tenant/tenant.guard.ts. */

/** Requires an authenticated principal; otherwise /login with a return url. */
export const authGuard: NavigationGuard = (to) => {
  if (useSessionStore().isAuthenticated) return true;
  return { path: '/login', query: { returnUrl: to.fullPath } };
};

/** Requires a specific role; otherwise /forbidden. */
export function roleGuard(role: UserRole): NavigationGuard {
  return () => {
    const session = useSessionStore();
    if (!session.isAuthenticated) return { path: '/login' };
    return session.hasRole(role) ? true : { path: '/forbidden' };
  };
}

/**
 * Guarantees an active tenant before any tenant-scoped screen renders: a tenant_admin adopts the
 * JWT `tenant_id`; a platform_admin with no selection goes to the tenant picker.
 */
export const tenantResolvedGuard: NavigationGuard = () => {
  const tenant = useTenantContextStore();
  if (tenant.hasTenant) return true;
  if (useSessionStore().role === 'tenant_admin') {
    tenant.adoptSessionTenant();
    if (tenant.hasTenant) return true;
  }
  return { path: '/select-tenant' };
};

/** Blocks a route unless its feature flag is on. */
export function featureGuard(flag: FeatureFlag): NavigationGuard {
  return () => (isFeatureEnabled(flag) ? true : { path: '/not-found' });
}
