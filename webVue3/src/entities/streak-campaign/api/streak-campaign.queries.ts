import { computed, toValue, type MaybeRefOrGetter } from 'vue';
import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/vue-query';
import { useTenantContextStore } from '@/shared/model';
import type {
  StreakCampaign,
  CreateStreakCampaignRequest,
  UpdateStreakCampaignRequest,
} from '../model/types';
import {
  createStreakCampaign,
  deleteStreakCampaign,
  getStreakCampaign,
  listStreakCampaigns,
  setStreakCampaignStatus,
  updateStreakCampaign,
} from './streak-campaign.api';

export const streakCampaignKeys = {
  all: (tenantId: string | null, programId: string | null) =>
    ['tenants', tenantId, 'programs', programId, 'streak-campaigns'] as const,
  page: (tenantId: string | null, programId: string | null, page: number, pageSize: number) =>
    [...streakCampaignKeys.all(tenantId, programId), 'page', { page, pageSize }] as const,
  detail: (tenantId: string | null, programId: string | null, campaignId: string | null) =>
    [...streakCampaignKeys.all(tenantId, programId), 'detail', campaignId] as const,
};

export function useStreakCampaignsPageQuery(
  programId: MaybeRefOrGetter<string>,
  page: MaybeRefOrGetter<number>,
  pageSize: number,
) {
  const tenant = useTenantContextStore();
  return useQuery({
    queryKey: computed(() =>
      streakCampaignKeys.page(tenant.activeTenantId, toValue(programId), toValue(page), pageSize),
    ),
    queryFn: () => listStreakCampaigns(tenant.require(), toValue(programId), toValue(page), pageSize),
    enabled: computed(() => tenant.hasTenant),
    placeholderData: keepPreviousData,
  });
}

/** One campaign; disabled while `campaignId` is null (a new campaign). */
export function useStreakCampaignQuery(
  programId: MaybeRefOrGetter<string>,
  campaignId: MaybeRefOrGetter<string | null>,
) {
  const tenant = useTenantContextStore();
  return useQuery({
    queryKey: computed(() =>
      streakCampaignKeys.detail(tenant.activeTenantId, toValue(programId), toValue(campaignId)),
    ),
    queryFn: () => getStreakCampaign(tenant.require(), toValue(programId), toValue(campaignId) as string),
    enabled: computed(() => tenant.hasTenant && !!toValue(campaignId)),
  });
}

/** After any write: the server's copy wins for the detail, and every list refetches. */
function useCacheUpdate(programId: MaybeRefOrGetter<string>) {
  const tenant = useTenantContextStore();
  const queryClient = useQueryClient();
  return (campaign?: StreakCampaign) => {
    if (campaign)
      queryClient.setQueryData(
        streakCampaignKeys.detail(tenant.activeTenantId, toValue(programId), campaign.id),
        campaign,
      );
    void queryClient.invalidateQueries({
      queryKey: [...streakCampaignKeys.all(tenant.activeTenantId, toValue(programId)), 'page'],
    });
  };
}

export function useCreateStreakCampaignMutation(programId: MaybeRefOrGetter<string>) {
  const tenant = useTenantContextStore();
  const update = useCacheUpdate(programId);
  return useMutation({
    mutationFn: (body: CreateStreakCampaignRequest) =>
      createStreakCampaign(tenant.require(), toValue(programId), body),
    onSuccess: update,
  });
}

export function useUpdateStreakCampaignMutation(programId: MaybeRefOrGetter<string>) {
  const tenant = useTenantContextStore();
  const update = useCacheUpdate(programId);
  return useMutation({
    mutationFn: (args: { id: string; body: UpdateStreakCampaignRequest }) =>
      updateStreakCampaign(tenant.require(), toValue(programId), args.id, args.body),
    onSuccess: update,
  });
}

export function useSetStreakCampaignStatusMutation(programId: MaybeRefOrGetter<string>) {
  const tenant = useTenantContextStore();
  const update = useCacheUpdate(programId);
  return useMutation({
    mutationFn: (args: { id: string; status: 'active' | 'disabled' }) =>
      setStreakCampaignStatus(tenant.require(), toValue(programId), args.id, args.status),
    onSuccess: update,
  });
}

export function useDeleteStreakCampaignMutation(programId: MaybeRefOrGetter<string>) {
  const tenant = useTenantContextStore();
  const update = useCacheUpdate(programId);
  return useMutation({
    mutationFn: (campaignId: string) =>
      deleteStreakCampaign(tenant.require(), toValue(programId), campaignId),
    onSuccess: () => update(),
  });
}
