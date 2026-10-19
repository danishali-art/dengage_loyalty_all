import { computed, toValue, type MaybeRefOrGetter } from 'vue';
import { keepPreviousData, useQuery } from '@tanstack/vue-query';
import { useTenantContextStore } from '@/shared/model';
import { getDashboardSummary, type DashboardFilter } from './dashboard.api';

export const dashboardKeys = {
  summary: (tenantId: string | null, filter: DashboardFilter) =>
    ['tenants', tenantId, 'dashboard', 'summary', filter] as const,
};

/** The summary for the chosen program and date range (each filter change refetches). */
export function useDashboardSummaryQuery(filter: MaybeRefOrGetter<DashboardFilter>) {
  const tenant = useTenantContextStore();
  return useQuery({
    queryKey: computed(() => dashboardKeys.summary(tenant.activeTenantId, { ...toValue(filter) })),
    queryFn: () => getDashboardSummary(tenant.require(), toValue(filter)),
    enabled: computed(() => tenant.hasTenant),
    placeholderData: keepPreviousData,
  });
}
