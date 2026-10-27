import { computed, reactive } from 'vue';
import { useI18n } from 'vue-i18n';
import { useProgramsQuery } from '@/entities/program';
import { useDashboardSummaryQuery, type DashboardFilter, type DashboardSummary } from '@/entities/dashboard';

export interface StatTile {
  labelKey: string;
  value: string;
  icon: string;
  to?: string;
}

/** Display only: a decimal string with up to 2 decimals. */
function formatBalance(value: string): string {
  const n = Number(value);
  return Number.isFinite(n) ? n.toLocaleString(undefined, { maximumFractionDigits: 2 }) : value;
}

export function dashboardTiles(s: DashboardSummary): StatTile[] {
  return [
    { labelKey: 'dashboard.tiles.programs', icon: '▦', value: `${s.programCount}`, to: '/programs' },
    { labelKey: 'dashboard.tiles.tiers', icon: '🏅', value: `${s.tierCount}` },
    {
      labelKey: 'dashboard.tiles.customerAccounts',
      icon: '☻',
      value: `${s.customerAccountCount}`,
      to: '/customers',
    },
    { labelKey: 'dashboard.tiles.totalBalance', icon: '⭐', value: formatBalance(s.totalBalance) },
    { labelKey: 'dashboard.tiles.redemptions', icon: '🎁', value: `${s.redemptionCount}` },
    { labelKey: 'dashboard.tiles.activeStreaks', icon: '🔥', value: `${s.streakActiveCount}` },
    { labelKey: 'dashboard.tiles.streaksCompleted', icon: '✓', value: `${s.streakCompletedInRange}` },
  ];
}

/** Dashboard: tenant-wide tiles, filtered by program and date range. */
export function useDashboardPage() {
  const { t } = useI18n();
  const filter = reactive<DashboardFilter>({ programId: null, fromDate: null, toDate: null });
  const summary = useDashboardSummaryQuery(() => ({ ...filter }));
  const programs = useProgramsQuery(1, 100);

  return {
    filter,
    programOptions: computed(() => [
      { value: '', label: t('dashboard.allPrograms') },
      ...(programs.data.value?.data ?? []).map((p) => ({ value: p.id, label: p.name })),
    ]),
    filtered: computed(() => !!(filter.programId || filter.fromDate || filter.toDate)),
    clear(): void {
      filter.programId = null;
      filter.fromDate = null;
      filter.toDate = null;
    },
    loading: summary.isLoading,
    tiles: computed(() => (summary.data.value ? dashboardTiles(summary.data.value) : [])),
  };
}
