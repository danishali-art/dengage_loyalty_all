import { apiClient, unwrap, type ApiSchema, type Page } from '@/shared/api';

export type ApiKey = ApiSchema<'ApiKeyResponse'>;
/** The create response carries the raw key — shown once, never retrievable again. */
export type CreatedApiKey = ApiSchema<'CreateApiKeyResponse'>;

/** `/platform/tenants/{tenantId}/api-keys` (TenantsController.cs) — platform_admin only. */
export function listApiKeys(tenantId: string, page: number, pageSize: number): Promise<Page<ApiKey>> {
  return unwrap(
    apiClient().GET('/api/v1/platform/tenants/{tenantId}/api-keys', {
      params: { path: { tenantId }, query: { page, pageSize } },
    }),
  );
}

export function createApiKey(tenantId: string): Promise<CreatedApiKey> {
  return unwrap(
    apiClient().POST('/api/v1/platform/tenants/{tenantId}/api-keys', { params: { path: { tenantId } } }),
  );
}

/** A client still using the key starts getting 401s at once. */
export function revokeApiKey(tenantId: string, keyId: string): Promise<void> {
  return unwrap(
    apiClient().DELETE('/api/v1/platform/tenants/{tenantId}/api-keys/{keyId}', {
      params: { path: { tenantId, keyId } },
    }),
  );
}
