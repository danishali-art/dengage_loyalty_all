import { computed, toValue, type MaybeRefOrGetter } from 'vue';
import { useQuery } from '@tanstack/vue-query';
import { useTenantContextStore } from '@/shared/model';
import { getConfigVersion, listConfigVersions } from './config-version.api';

export const configVersionKeys = {
  all: (tenantId: string | null) => ['tenants', tenantId, 'config-versions'] as const,
  list: (tenantId: string | null, entityType: string, entityId: string) =>
    [...configVersionKeys.all(tenantId), 'list', entityType, entityId] as const,
  detail: (tenantId: string | null, versionId: string | null) =>
    [...configVersionKeys.all(tenantId), 'detail', versionId] as const,
};

/** Every recorded version of one entity (e.g. a program's edits or its publications). */
export function useConfigVersionsQuery(entityType: string, entityId: MaybeRefOrGetter<string>) {
  const tenant = useTenantContextStore();
  return useQuery({
    queryKey: computed(() => configVersionKeys.list(tenant.activeTenantId, entityType, toValue(entityId))),
    queryFn: async () => [
      ...(await listConfigVersions(tenant.require(), entityType, toValue(entityId))).data,
    ],
    enabled: computed(() => tenant.hasTenant),
  });
}

/** One version with its snapshot — fetched when it is opened. */
export function useConfigVersionQuery(versionId: MaybeRefOrGetter<string | null>) {
  const tenant = useTenantContextStore();
  return useQuery({
    queryKey: computed(() => configVersionKeys.detail(tenant.activeTenantId, toValue(versionId))),
    queryFn: () => getConfigVersion(tenant.require(), toValue(versionId) as string),
    enabled: computed(() => tenant.hasTenant && !!toValue(versionId)),
    // A recorded version never changes.
    staleTime: Infinity,
  });
}
