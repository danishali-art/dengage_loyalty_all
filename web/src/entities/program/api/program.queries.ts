import { computed, toValue, type MaybeRefOrGetter } from 'vue';
import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/vue-query';
import { useTenantContextStore } from '@/shared/model';
import type { CreateProgramRequest, Program, UpdateProgramRequest } from '../model/types';
import {
  createProgram,
  deleteProgram,
  getProgram,
  listPrograms,
  publishProgram,
  updateProgram,
} from './program.api';

/** Every key starts with the tenant, so switching tenants never reuses another tenant's cache. */
export const programKeys = {
  all: (tenantId: string | null) => ['tenants', tenantId, 'programs'] as const,
  list: (tenantId: string | null, page: number, pageSize: number) =>
    [...programKeys.all(tenantId), 'list', { page, pageSize }] as const,
  detail: (tenantId: string | null, programId: string | null) =>
    [...programKeys.all(tenantId), 'detail', programId] as const,
};

export function useProgramsQuery(page: MaybeRefOrGetter<number>, pageSize: number) {
  const tenant = useTenantContextStore();
  return useQuery({
    queryKey: computed(() => programKeys.list(tenant.activeTenantId, toValue(page), pageSize)),
    queryFn: () => listPrograms(tenant.require(), toValue(page), pageSize),
    enabled: computed(() => tenant.hasTenant),
    placeholderData: keepPreviousData,
  });
}

export function useProgramQuery(programId: MaybeRefOrGetter<string | null>) {
  const tenant = useTenantContextStore();
  return useQuery({
    queryKey: computed(() => programKeys.detail(tenant.activeTenantId, toValue(programId))),
    queryFn: () => getProgram(tenant.require(), toValue(programId) as string),
    enabled: computed(() => tenant.hasTenant && !!toValue(programId)),
  });
}

/** After any write: the server's copy wins for the detail, and lists refetch (counts, badges). */
function useProgramCacheUpdate() {
  const tenant = useTenantContextStore();
  const queryClient = useQueryClient();
  return (program: Program) => {
    queryClient.setQueryData(programKeys.detail(tenant.activeTenantId, program.id), program);
    void queryClient.invalidateQueries({ queryKey: [...programKeys.all(tenant.activeTenantId), 'list'] });
  };
}

export function useCreateProgramMutation() {
  const tenant = useTenantContextStore();
  const update = useProgramCacheUpdate();
  return useMutation({
    mutationFn: (body: CreateProgramRequest) => createProgram(tenant.require(), body),
    onSuccess: update,
  });
}

export function useUpdateProgramMutation(programId: MaybeRefOrGetter<string>) {
  const tenant = useTenantContextStore();
  const update = useProgramCacheUpdate();
  return useMutation({
    mutationFn: (body: UpdateProgramRequest) => updateProgram(tenant.require(), toValue(programId), body),
    onSuccess: update,
  });
}

export function usePublishProgramMutation(programId: MaybeRefOrGetter<string>) {
  const tenant = useTenantContextStore();
  const update = useProgramCacheUpdate();
  return useMutation({
    mutationFn: () => publishProgram(tenant.require(), toValue(programId)),
    onSuccess: update,
  });
}

export function useDeleteProgramMutation(programId: MaybeRefOrGetter<string>) {
  const tenant = useTenantContextStore();
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: () => deleteProgram(tenant.require(), toValue(programId)),
    onSuccess: () => {
      queryClient.removeQueries({ queryKey: programKeys.detail(tenant.activeTenantId, toValue(programId)) });
      void queryClient.invalidateQueries({ queryKey: [...programKeys.all(tenant.activeTenantId), 'list'] });
    },
  });
}
