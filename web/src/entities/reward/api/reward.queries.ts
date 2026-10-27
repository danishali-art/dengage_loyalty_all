import { computed, toValue, type MaybeRefOrGetter } from 'vue';
import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/vue-query';
import { useTenantContextStore } from '@/shared/model';
import type { CreateRewardRequest, UpdateRewardRequest } from '../model/types';
import { approveReward, createReward, listAllRewards, listRewards, updateReward } from './reward.api';

export const rewardKeys = {
  all: (tenantId: string | null, programId: string | null) =>
    ['tenants', tenantId, 'programs', programId, 'rewards'] as const,
  page: (tenantId: string | null, programId: string | null, page: number, pageSize: number) =>
    [...rewardKeys.all(tenantId, programId), 'page', { page, pageSize }] as const,
  everything: (tenantId: string | null, programId: string | null) =>
    [...rewardKeys.all(tenantId, programId), 'all'] as const,
};

/** Every reward of a program (pickers). */
export function useAllRewardsQuery(programId: MaybeRefOrGetter<string>) {
  const tenant = useTenantContextStore();
  return useQuery({
    queryKey: computed(() => rewardKeys.everything(tenant.activeTenantId, toValue(programId))),
    queryFn: () => listAllRewards(tenant.require(), toValue(programId)),
    enabled: computed(() => tenant.hasTenant),
  });
}

export function useRewardsPageQuery(
  programId: MaybeRefOrGetter<string>,
  page: MaybeRefOrGetter<number>,
  pageSize: number,
) {
  const tenant = useTenantContextStore();
  return useQuery({
    queryKey: computed(() =>
      rewardKeys.page(tenant.activeTenantId, toValue(programId), toValue(page), pageSize),
    ),
    queryFn: () => listRewards(tenant.require(), toValue(programId), toValue(page), pageSize),
    enabled: computed(() => tenant.hasTenant),
    placeholderData: keepPreviousData,
  });
}

function useInvalidate(programId: MaybeRefOrGetter<string>) {
  const tenant = useTenantContextStore();
  const queryClient = useQueryClient();
  return () =>
    queryClient.invalidateQueries({ queryKey: rewardKeys.all(tenant.activeTenantId, toValue(programId)) });
}

export function useCreateRewardMutation(programId: MaybeRefOrGetter<string>) {
  const tenant = useTenantContextStore();
  const invalidate = useInvalidate(programId);
  return useMutation({
    mutationFn: (body: CreateRewardRequest) => createReward(tenant.require(), toValue(programId), body),
    onSuccess: invalidate,
  });
}

export function useUpdateRewardMutation(programId: MaybeRefOrGetter<string>) {
  const tenant = useTenantContextStore();
  const invalidate = useInvalidate(programId);
  return useMutation({
    mutationFn: (args: { id: string; body: UpdateRewardRequest }) =>
      updateReward(tenant.require(), toValue(programId), args.id, args.body),
    onSuccess: invalidate,
  });
}

export function useApproveRewardMutation(programId: MaybeRefOrGetter<string>) {
  const tenant = useTenantContextStore();
  const invalidate = useInvalidate(programId);
  return useMutation({
    mutationFn: (rewardId: string) => approveReward(tenant.require(), toValue(programId), rewardId),
    onSuccess: invalidate,
  });
}
