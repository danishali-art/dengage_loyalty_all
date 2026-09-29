import { inject } from '@angular/core';
import { ResolveFn } from '@angular/router';
import { PlatformTenantContextStore } from '../../core/platform/platform-tenant-context.store';
import { Tenant } from '../../core/tenant/tenant.model';
import { PlatformService } from './platform.service';

/**
 * Resolves once per `/platform/tenants/:tenantId` entry — see `PlatformTenantContextStore`'s
 * doc. Lives here, not in `core/`, because it needs `PlatformService` — lower layers must never
 * depend on a feature.
 */
export const platformTenantContextResolver: ResolveFn<Tenant> = async (route) => {
  const tenantId = route.paramMap.get('tenantId')!;
  const platform = inject(PlatformService);
  const store = inject(PlatformTenantContextStore);

  const tenant = await platform.getTenant(tenantId);
  store.setTenant(tenant);
  return tenant;
};
