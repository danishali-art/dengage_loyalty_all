import { apiClient, unwrap, withFlags, type Page } from '@/shared/api';
import type {
  StreakCampaign,
  CreateStreakCampaignRequest,
  UpdateStreakCampaignRequest,
} from '../model/types';

/** `/tenants/{tenantId}/programs/{programId}/streak-campaigns` (StreakCampaignsModule.cs). */
export function listStreakCampaigns(
  tenantId: string,
  programId: string,
  page: number,
  pageSize: number,
): Promise<Page<StreakCampaign>> {
  return unwrap(
    apiClient().GET('/api/v1/tenants/{tenantId}/programs/{programId}/streak-campaigns', {
      params: { path: { tenantId, programId }, query: { page, pageSize } },
    }),
  );
}

export function getStreakCampaign(
  tenantId: string,
  programId: string,
  campaignId: string,
): Promise<StreakCampaign> {
  return unwrap(
    apiClient().GET('/api/v1/tenants/{tenantId}/programs/{programId}/streak-campaigns/{campaignId}', {
      params: { path: { tenantId, programId, campaignId } },
    }),
  );
}

export function createStreakCampaign(
  tenantId: string,
  programId: string,
  body: CreateStreakCampaignRequest,
): Promise<StreakCampaign> {
  return unwrap(
    apiClient().POST('/api/v1/tenants/{tenantId}/programs/{programId}/streak-campaigns', {
      ...withFlags({ skipErrorToast: true }),
      params: { path: { tenantId, programId } },
      body,
    }),
  );
}

/** PATCH semantics: a null field keeps the campaign's current value (StreakCampaignsAppService.UpdateAsync). */
export function updateStreakCampaign(
  tenantId: string,
  programId: string,
  campaignId: string,
  body: UpdateStreakCampaignRequest,
): Promise<StreakCampaign> {
  return unwrap(
    apiClient().PATCH('/api/v1/tenants/{tenantId}/programs/{programId}/streak-campaigns/{campaignId}', {
      ...withFlags({ skipErrorToast: true }),
      params: { path: { tenantId, programId, campaignId } },
      body,
    }),
  );
}

export function setStreakCampaignStatus(
  tenantId: string,
  programId: string,
  campaignId: string,
  status: 'active' | 'disabled',
): Promise<StreakCampaign> {
  return unwrap(
    apiClient().PATCH(
      '/api/v1/tenants/{tenantId}/programs/{programId}/streak-campaigns/{campaignId}/status',
      {
        params: { path: { tenantId, programId, campaignId } },
        body: { status },
      },
    ),
  );
}

/** A soft delete (status `deleted`), like rules. */
export function deleteStreakCampaign(tenantId: string, programId: string, campaignId: string): Promise<void> {
  return unwrap(
    apiClient().DELETE('/api/v1/tenants/{tenantId}/programs/{programId}/streak-campaigns/{campaignId}', {
      params: { path: { tenantId, programId, campaignId } },
    }),
  );
}
