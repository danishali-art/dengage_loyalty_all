import { apiClient, unwrap, withFlags, type Page } from '@/shared/api';
import type { CreateTierRequest, Tier, UpdateTierRequest } from '../model/types';

/**
 * A program has a handful of tiers, and reordering needs the complete set on one page — so the
 * list always asks for everything (the endpoint still pages like every other list).
 */
const ALL_PAGE_SIZE = 100;

/** `/tenants/{tenantId}/programs/{programId}/tiers` (TiersModule.cs). */
export function listTiers(tenantId: string, programId: string): Promise<Page<Tier>> {
  return unwrap(
    apiClient().GET('/api/v1/tenants/{tenantId}/programs/{programId}/tiers', {
      params: { path: { tenantId, programId }, query: { page: 1, pageSize: ALL_PAGE_SIZE } },
    }),
  );
}

export function createTier(tenantId: string, programId: string, body: CreateTierRequest): Promise<Tier> {
  return unwrap(
    apiClient().POST('/api/v1/tenants/{tenantId}/programs/{programId}/tiers', {
      ...withFlags({ skipErrorToast: true }),
      params: { path: { tenantId, programId } },
      body,
    }),
  );
}

export function updateTier(
  tenantId: string,
  programId: string,
  tierId: string,
  body: UpdateTierRequest,
): Promise<Tier> {
  return unwrap(
    apiClient().PATCH('/api/v1/tenants/{tenantId}/programs/{programId}/tiers/{tierId}', {
      ...withFlags({ skipErrorToast: true }),
      params: { path: { tenantId, programId, tierId } },
      body,
    }),
  );
}

export function reorderTiers(tenantId: string, programId: string, tierIds: string[]): Promise<void> {
  return unwrap(
    apiClient().POST('/api/v1/tenants/{tenantId}/programs/{programId}/tiers/reorder', {
      params: { path: { tenantId, programId } },
      body: { tierIds },
    }),
  );
}

/** The caller toasts the outcome itself. */
export function deleteTier(tenantId: string, programId: string, tierId: string): Promise<void> {
  return unwrap(
    apiClient().DELETE('/api/v1/tenants/{tenantId}/programs/{programId}/tiers/{tierId}', {
      ...withFlags({ skipErrorToast: true }),
      params: { path: { tenantId, programId, tierId } },
    }),
  );
}
