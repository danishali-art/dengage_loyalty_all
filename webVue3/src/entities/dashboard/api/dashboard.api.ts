import { apiClient, unwrap, type ApiSchema } from '@/shared/api';

/** Tenant-wide figures; `totalBalance` is a decimal string, formatted for display only. */
export type DashboardSummary = ApiSchema<'DashboardSummaryResponse'>;

export interface DashboardFilter {
  programId: string | null;
  /** `yyyy-mm-dd` from a date input; sent as is (DashboardController reads a date). */
  fromDate: string | null;
  toDate: string | null;
}

/** `/tenants/{tenantId}/dashboard/summary` (DashboardController.cs); empty filters are left out. */
export function getDashboardSummary(tenantId: string, filter: DashboardFilter): Promise<DashboardSummary> {
  return unwrap(
    apiClient().GET('/api/v1/tenants/{tenantId}/dashboard/summary', {
      params: {
        path: { tenantId },
        query: {
          programId: filter.programId || undefined,
          fromDate: filter.fromDate || undefined,
          toDate: filter.toDate || undefined,
        },
      },
    }),
  );
}
