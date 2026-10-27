import { apiClient, unwrap, type Page } from '@/shared/api';
import type { ConfigVersionDetail, ConfigVersionSummary } from '../model/types';

const ALL_PAGE_SIZE = 100;

/** `/tenants/{tenantId}/config-versions` (ConfigVersionsController.cs) — newest first per entity. */
export function listConfigVersions(
  tenantId: string,
  entityType: string,
  entityId: string,
): Promise<Page<ConfigVersionSummary>> {
  return unwrap(
    apiClient().GET('/api/v1/tenants/{tenantId}/config-versions', {
      params: { path: { tenantId }, query: { page: 1, pageSize: ALL_PAGE_SIZE, entityType, entityId } },
    }),
  );
}

export function getConfigVersion(tenantId: string, versionId: string): Promise<ConfigVersionDetail> {
  return unwrap(
    apiClient().GET('/api/v1/tenants/{tenantId}/config-versions/{versionId}', {
      params: { path: { tenantId, versionId } },
    }),
  );
}
