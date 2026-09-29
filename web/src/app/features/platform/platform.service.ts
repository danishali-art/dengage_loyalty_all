import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { ApiClient } from '../../core/http/api-client';
import { Page } from '../../core/http/pagination';
import { Tenant } from '../../core/tenant/tenant.model';
import {
  AdminUser,
  ApiKey,
  CreateAdminUserRequest,
  CreateApiKeyResult,
  CreateTenantRequest,
  UpdateTenantRequest,
} from './platform.model';

/** Wraps `/api/v1/platform/*` — platform_admin only. */
@Injectable({ providedIn: 'root' })
export class PlatformService {
  private readonly api = inject(ApiClient);

  listTenants(page: number, pageSize: number): Promise<Page<Tenant>> {
    return firstValueFrom(this.api.platform.getPage<Tenant>('tenants', { page, pageSize }));
  }

  getTenant(tenantId: string): Promise<Tenant> {
    return firstValueFrom(this.api.platform.get<Tenant>(`tenants/${tenantId}`));
  }

  createTenant(request: CreateTenantRequest): Promise<Tenant> {
    return firstValueFrom(this.api.platform.post<Tenant>('tenants', request, { skipErrorToast: true }));
  }

  updateTenant(tenantId: string, request: UpdateTenantRequest): Promise<Tenant> {
    return firstValueFrom(this.api.platform.patch<Tenant>(`tenants/${tenantId}`, request));
  }

  listApiKeys(tenantId: string, page = 1, pageSize = 20): Promise<Page<ApiKey>> {
    return firstValueFrom(this.api.platform.getPage<ApiKey>(`tenants/${tenantId}/api-keys`, { page, pageSize }));
  }

  createApiKey(tenantId: string): Promise<CreateApiKeyResult> {
    return firstValueFrom(this.api.platform.post<CreateApiKeyResult>(`tenants/${tenantId}/api-keys`, {}));
  }

  revokeApiKey(tenantId: string, keyId: string): Promise<void> {
    return firstValueFrom(this.api.platform.delete<void>(`tenants/${tenantId}/api-keys/${keyId}`));
  }

  listAdminUsers(tenantId: string, page = 1, pageSize = 20): Promise<Page<AdminUser>> {
    return firstValueFrom(this.api.platform.getPage<AdminUser>(`tenants/${tenantId}/admin-users`, { page, pageSize }));
  }

  createAdminUser(tenantId: string, email: string, password: string): Promise<AdminUser> {
    const request: CreateAdminUserRequest = { email, password, role: 'tenant_admin' };
    return firstValueFrom(
      this.api.platform.post<AdminUser>(`tenants/${tenantId}/admin-users`, request, { skipErrorToast: true }),
    );
  }
}
