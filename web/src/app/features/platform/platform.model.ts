export type { Tenant, TenantStatus } from '../../core/tenant/tenant.model';

export interface CreateTenantRequest {
  id: string;
  name: string;
}

export interface UpdateTenantRequest {
  name?: string;
  status?: 'active' | 'suspended';
}

/** `POST /platform/tenants/{id}/api-keys` response — the raw key is shown exactly once. */
export interface CreateApiKeyResult {
  id: string;
  prefix: string;
  rawKey: string;
  createdAt: string;
}

export interface ApiKey {
  id: string;
  prefix: string;
  createdAt: string;
  lastUsedAt: string | null;
  revokedAt: string | null;
}

export type AdminRole = 'platform_admin' | 'tenant_admin';

export interface AdminUser {
  id: string;
  email: string;
  role: AdminRole;
  tenantId: string | null;
  status: string;
  createdAt: string;
}

/** Always creates a `tenant_admin` scoped to the tenant in the URL (backend-enforced). */
export interface CreateAdminUserRequest {
  email: string;
  password: string;
  role: 'tenant_admin';
}
