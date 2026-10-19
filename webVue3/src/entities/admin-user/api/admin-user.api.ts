import { apiClient, unwrap, withFlags, type Page } from '@/shared/api';
import type { AdminUser, CreateAdminUserRequest } from '../model/types';

/** `/platform/tenants/{tenantId}/admin-users` (PlatformModule.cs) — platform_admin only. */
export function listAdminUsers(tenantId: string, page: number, pageSize: number): Promise<Page<AdminUser>> {
  return unwrap(
    apiClient().GET('/api/v1/platform/tenants/{tenantId}/admin-users', {
      params: { path: { tenantId }, query: { page, pageSize } },
    }),
  );
}

/** The caller shows a failure itself (the create dialog maps field errors). */
export function createAdminUser(tenantId: string, body: CreateAdminUserRequest): Promise<AdminUser> {
  return unwrap(
    apiClient().POST('/api/v1/platform/tenants/{tenantId}/admin-users', {
      ...withFlags({ skipErrorToast: true }),
      params: { path: { tenantId } },
      body,
    }),
  );
}
