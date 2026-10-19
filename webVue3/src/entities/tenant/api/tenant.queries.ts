import { computed, toValue, type MaybeRefOrGetter } from 'vue';
import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/vue-query';
import { useSessionStore } from '@/shared/model';
import type { CreateTenantRequest, Tenant, UpdateTenantRequest } from '../model/types';
import { createTenant, getTenant, searchTenants, updateTenant } from './tenant.api';

export const tenantKeys = {
  all: ['platform', 'tenants'] as const,
  search: (search: string, page: number, pageSize: number) =>
    [...tenantKeys.all, 'search', { search, page, pageSize }] as const,
  detail: (tenantId: string | null) => [...tenantKeys.all, 'detail', tenantId] as const,
};

/**
 * Tenant picker results. Only a platform_admin may list tenants; `active` keeps a picker that
 * isn't shown (the topbar's closed dialog) from fetching.
 */
export function useTenantSearchQuery(
  search: MaybeRefOrGetter<string>,
  page: MaybeRefOrGetter<number>,
  pageSize: number,
  active: MaybeRefOrGetter<boolean> = true,
) {
  const session = useSessionStore();
  return useQuery({
    queryKey: computed(() => tenantKeys.search(toValue(search), toValue(page), pageSize)),
    queryFn: () => searchTenants(toValue(page), pageSize, toValue(search)),
    enabled: computed(() => session.isPlatformAdmin && toValue(active)),
    placeholderData: keepPreviousData,
  });
}

/** One tenant's record — the switcher's label. Only a platform_admin can read it. */
export function useTenantQuery(tenantId: MaybeRefOrGetter<string | null>) {
  const session = useSessionStore();
  return useQuery({
    queryKey: computed(() => tenantKeys.detail(toValue(tenantId))),
    queryFn: () => getTenant(toValue(tenantId) as string),
    enabled: computed(() => session.isPlatformAdmin && !!toValue(tenantId)),
    staleTime: 5 * 60_000,
  });
}

/** After a write: the server's copy wins for the detail, and every tenant list refetches. */
function useTenantCacheUpdate() {
  const queryClient = useQueryClient();
  return (tenant: Tenant) => {
    queryClient.setQueryData(tenantKeys.detail(tenant.id), tenant);
    void queryClient.invalidateQueries({ queryKey: [...tenantKeys.all, 'search'] });
  };
}

export function useCreateTenantMutation() {
  const update = useTenantCacheUpdate();
  return useMutation({ mutationFn: (body: CreateTenantRequest) => createTenant(body), onSuccess: update });
}

export function useUpdateTenantMutation(tenantId: MaybeRefOrGetter<string>) {
  const update = useTenantCacheUpdate();
  return useMutation({
    mutationFn: (body: UpdateTenantRequest) => updateTenant(toValue(tenantId), body),
    onSuccess: update,
  });
}
