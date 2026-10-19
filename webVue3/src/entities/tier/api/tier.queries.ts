import { computed, toValue, type MaybeRefOrGetter } from 'vue';
import { useMutation, useQuery, useQueryClient } from '@tanstack/vue-query';
import { useTenantContextStore } from '@/shared/model';
import { sortTiers, type CreateTierRequest, type UpdateTierRequest } from '../model/types';
import { createTier, deleteTier, listTiers, reorderTiers, updateTier } from './tier.api';

export const tierKeys = {
  all: (tenantId: string | null, programId: string | null) =>
    ['tenants', tenantId, 'programs', programId, 'tiers'] as const,
};

/** Every tier of a program, in display order. */
export function useTiersQuery(programId: MaybeRefOrGetter<string | null>) {
  const tenant = useTenantContextStore();
  return useQuery({
    queryKey: computed(() => tierKeys.all(tenant.activeTenantId, toValue(programId))),
    queryFn: async () => sortTiers((await listTiers(tenant.require(), toValue(programId) as string)).data),
    enabled: computed(() => tenant.hasTenant && !!toValue(programId)),
  });
}

function useInvalidate(programId: MaybeRefOrGetter<string>) {
  const tenant = useTenantContextStore();
  const queryClient = useQueryClient();
  return () =>
    queryClient.invalidateQueries({ queryKey: tierKeys.all(tenant.activeTenantId, toValue(programId)) });
}

export function useCreateTierMutation(programId: MaybeRefOrGetter<string>) {
  const tenant = useTenantContextStore();
  const invalidate = useInvalidate(programId);
  return useMutation({
    mutationFn: (body: CreateTierRequest) => createTier(tenant.require(), toValue(programId), body),
    onSuccess: invalidate,
  });
}

export function useUpdateTierMutation(programId: MaybeRefOrGetter<string>) {
  const tenant = useTenantContextStore();
  const invalidate = useInvalidate(programId);
  return useMutation({
    mutationFn: (args: { id: string; body: UpdateTierRequest }) =>
      updateTier(tenant.require(), toValue(programId), args.id, args.body),
    onSuccess: invalidate,
  });
}

/** The server's order wins afterwards, success or failure. */
export function useReorderTiersMutation(programId: MaybeRefOrGetter<string>) {
  const tenant = useTenantContextStore();
  const invalidate = useInvalidate(programId);
  return useMutation({
    mutationFn: (tierIds: string[]) => reorderTiers(tenant.require(), toValue(programId), tierIds),
    onSettled: invalidate,
  });
}

export function useDeleteTierMutation(programId: MaybeRefOrGetter<string>) {
  const tenant = useTenantContextStore();
  const invalidate = useInvalidate(programId);
  return useMutation({
    mutationFn: (tierId: string) => deleteTier(tenant.require(), toValue(programId), tierId),
    onSuccess: invalidate,
  });
}
