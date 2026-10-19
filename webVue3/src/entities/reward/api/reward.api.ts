import { apiClient, unwrap, withFlags, type Page } from '@/shared/api';
import type { CreateRewardRequest, Reward, UpdateRewardRequest } from '../model/types';

/** `/tenants/{tenantId}/programs/{programId}/rewards` (RewardsModule.cs). */
export function listRewards(
  tenantId: string,
  programId: string,
  page: number,
  pageSize: number,
): Promise<Page<Reward>> {
  return unwrap(
    apiClient().GET('/api/v1/tenants/{tenantId}/programs/{programId}/rewards', {
      params: { path: { tenantId, programId }, query: { page, pageSize } },
    }),
  );
}

/** The API's page-size cap (PageRequest clamps to 100). */
const ALL_PAGE_SIZE = 100;

/** Every reward of a program, paging through the 100-row cap (the streak form's reward picker). */
export async function listAllRewards(tenantId: string, programId: string): Promise<Reward[]> {
  const all: Reward[] = [];
  for (let page = 1; ; page++) {
    const result = await listRewards(tenantId, programId, page, ALL_PAGE_SIZE);
    all.push(...result.data);
    if (result.data.length === 0 || all.length >= result.total) return all;
  }
}

export function createReward(
  tenantId: string,
  programId: string,
  body: CreateRewardRequest,
): Promise<Reward> {
  return unwrap(
    apiClient().POST('/api/v1/tenants/{tenantId}/programs/{programId}/rewards', {
      ...withFlags({ skipErrorToast: true }),
      params: { path: { tenantId, programId } },
      body,
    }),
  );
}

export function updateReward(
  tenantId: string,
  programId: string,
  rewardId: string,
  body: UpdateRewardRequest,
): Promise<Reward> {
  return unwrap(
    apiClient().PATCH('/api/v1/tenants/{tenantId}/programs/{programId}/rewards/{rewardId}', {
      ...withFlags({ skipErrorToast: true }),
      params: { path: { tenantId, programId, rewardId } },
      body,
    }),
  );
}

/** CR 2026-09-30 (A4): a different admin than the creator — the API rejects self-approval. */
export function approveReward(tenantId: string, programId: string, rewardId: string): Promise<Reward> {
  return unwrap(
    apiClient().PATCH('/api/v1/tenants/{tenantId}/programs/{programId}/rewards/{rewardId}/approve', {
      ...withFlags({ skipErrorToast: true }),
      params: { path: { tenantId, programId, rewardId } },
    }),
  );
}
