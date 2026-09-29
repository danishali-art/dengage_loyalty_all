import { inject } from '@angular/core';
import { CanActivateFn, Router, UrlTree } from '@angular/router';
import { SessionStore } from '../auth/session.store';
import { TenantContext } from './tenant-context';
import { TenantStore } from './tenant.store';

/**
 * Guarantees an active tenant before any tenant-scoped route renders.
 * - tenant_admin: adopt the JWT `tenant_id` claim.
 * - platform_admin with no selection: bounce to the tenant picker.
 */
export const tenantResolvedGuard: CanActivateFn = (): boolean | UrlTree => {
  const ctx = inject(TenantContext);
  const store = inject(TenantStore);
  const session = inject(SessionStore);
  const router = inject(Router);

  if (ctx.hasTenant()) return true;

  if (session.role() === 'tenant_admin') {
    store.initFromSession();
    if (ctx.hasTenant()) return true;
  }

  return router.createUrlTree(['/select-tenant']);
};
