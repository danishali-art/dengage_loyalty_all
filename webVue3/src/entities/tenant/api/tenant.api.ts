import { apiClient, unwrap, withFlags, type Page } from '@/shared/api';
import type { CreateTenantRequest, Tenant, UpdateTenantRequest } from '../model/types';

/** `/platform/tenants` (TenantsModule.cs) — platform_admin only. */
export function searchTenants(page: number, pageSize: number, search: string): Promise<Page<Tenant>> {
  return unwrap(
    apiClient().GET('/api/v1/platform/tenants', {
      params: { query: { page, pageSize, search: search || undefined } },
    }),
  );
}

export function getTenant(tenantId: string): Promise<Tenant> {
  return unwrap(apiClient().GET('/api/v1/platform/tenants/{tenantId}', { params: { path: { tenantId } } }));
}

/** The caller shows a failure itself (the create dialog maps field errors). */
export function createTenant(body: CreateTenantRequest): Promise<Tenant> {
  return unwrap(
    apiClient().POST('/api/v1/platform/tenants', { ...withFlags({ skipErrorToast: true }), body }),
  );
}

/** Rename or suspend / reactivate (PlatformValidators). */
export function updateTenant(tenantId: string, body: UpdateTenantRequest): Promise<Tenant> {
  return unwrap(
    apiClient().PATCH('/api/v1/platform/tenants/{tenantId}', { params: { path: { tenantId } }, body }),
  );
}
