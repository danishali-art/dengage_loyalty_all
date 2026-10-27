import { computed } from 'vue';
import { useQuery } from '@tanstack/vue-query';
import { useTenantContextStore } from '@/shared/model';
import { getEventTypes } from './event-type.api';

export const eventTypeKeys = {
  catalog: (tenantId: string | null) => ['tenants', tenantId, 'event-types'] as const,
};

/** The deployment's event-type catalog; it changes only with configuration, so it's kept a while. */
export function useEventTypesQuery() {
  const tenant = useTenantContextStore();
  return useQuery({
    queryKey: computed(() => eventTypeKeys.catalog(tenant.activeTenantId)),
    queryFn: () => getEventTypes(tenant.require()),
    enabled: computed(() => tenant.hasTenant),
    staleTime: 5 * 60_000,
    retry: false,
  });
}
