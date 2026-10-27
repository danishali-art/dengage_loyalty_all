import { computed } from 'vue';
import { useRoute } from 'vue-router';
import { isFeatureEnabled } from '@/shared/config/app-config';
import { useSessionStore } from '@/shared/model';
import { useProgramQuery } from '@/entities/program';
import { useTenantQuery } from '@/entities/tenant';

export interface NavItem {
  labelKey: string;
  to: string;
  icon: string;
}

export interface SubNavItem {
  labelKey: string;
  to: string;
}

const PROGRAM_SUB_NAV: { labelKey: string; segment: string }[] = [
  { labelKey: 'nav.program.overview', segment: '' },
  { labelKey: 'programs.overview.cards.accountTypes.label', segment: 'account-types' },
  { labelKey: 'programs.overview.cards.tiers.label', segment: 'tiers' },
  { labelKey: 'programs.overview.cards.rules.label', segment: 'rules' },
  { labelKey: 'programs.overview.cards.streakCampaigns.label', segment: 'streak-campaigns' },
  { labelKey: 'programs.overview.cards.cardBuckets.label', segment: 'card-buckets' },
  { labelKey: 'programs.overview.cards.rewards.label', segment: 'rewards' },
];

const TENANT_SUB_NAV: { labelKey: string; segment: string }[] = [
  { labelKey: 'nav.tenant.overview', segment: '' },
  { labelKey: 'nav.tenant.apiKeys', segment: 'api-keys' },
  { labelKey: 'nav.tenant.admins', segment: 'admin-users' },
];

/**
 * The sidebar's items. The program sub-nav reads the open
 * program from the route and its name from the query cache — no separate "program context" store.
 * The tenant sub-nav (platform screens) works the same way from `:tenantId`.
 */
export function useSidebarNav() {
  const route = useRoute();
  const session = useSessionStore();

  const items = computed<NavItem[]>(() =>
    [
      { labelKey: 'nav.dashboard', to: '/dashboard', icon: '◱', show: true },
      { labelKey: 'nav.programs', to: '/programs', icon: '▦', show: true },
      { labelKey: 'nav.customers', to: '/customers', icon: '☻', show: true },
      { labelKey: 'nav.events', to: '/events', icon: '⚡', show: isFeatureEnabled('eventSimulator') },
      { labelKey: 'nav.platform', to: '/platform', icon: '⚙', show: session.isPlatformAdmin },
      { labelKey: 'nav.reports', to: '/reports', icon: '▤', show: isFeatureEnabled('reports') },
      { labelKey: 'nav.settings', to: '/settings', icon: '⚑', show: isFeatureEnabled('settings') },
    ]
      .filter((i) => i.show)
      .map(({ labelKey, to, icon }) => ({ labelKey, to, icon })),
  );

  const programId = computed(() => {
    const id = route.params['programId'];
    return typeof id === 'string' ? id : null;
  });
  const program = useProgramQuery(programId);

  const programSubNav = computed(() => {
    if (!programId.value) return null;
    return {
      programName: program.data.value?.name ?? '',
      items: PROGRAM_SUB_NAV.map<SubNavItem>((s) => ({
        labelKey: s.labelKey,
        to: s.segment ? `/programs/${programId.value}/${s.segment}` : `/programs/${programId.value}`,
      })),
    };
  });

  // Platform screens only: the route's tenant, not the active tenant context.
  const platformTenantId = computed(() => {
    const id = route.params['tenantId'];
    return route.path.startsWith('/platform/') && typeof id === 'string' ? id : null;
  });
  const platformTenant = useTenantQuery(platformTenantId);

  const tenantSubNav = computed(() => {
    if (!platformTenantId.value) return null;
    const base = `/platform/tenants/${platformTenantId.value}`;
    return {
      tenantName: platformTenant.data.value?.name ?? platformTenantId.value,
      items: TENANT_SUB_NAV.map<SubNavItem>((s) => ({
        labelKey: s.labelKey,
        to: s.segment ? `${base}/${s.segment}` : base,
      })),
    };
  });

  return { items, programSubNav, tenantSubNav };
}
