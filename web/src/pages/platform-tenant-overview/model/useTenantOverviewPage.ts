import { computed, toRef } from 'vue';
import { useI18n } from 'vue-i18n';
import type { Crumb } from '@/shared/ui';
import { useTenantQuery } from '@/entities/tenant';
import { useTenantStatus } from '@/features/tenant-status';

const CARDS = [
  { key: 'apiKeys', icon: '🔑', segment: 'api-keys' },
  { key: 'admins', icon: '👤', segment: 'admin-users' },
];

/** One tenant's overview: status strip, links, suspend. */
export function useTenantOverviewPage(props: { tenantId: string }) {
  const { t } = useI18n();
  const tenantId = toRef(props, 'tenantId');
  const query = useTenantQuery(tenantId);
  const tenant = computed(() => query.data.value);
  const status = useTenantStatus(tenantId);

  return {
    tenant,
    loading: query.isLoading,
    notFound: computed(() => query.isError.value),
    crumbs: computed<Crumb[]>(() => [
      { label: t('platform.heading'), to: '/platform/tenants' },
      { label: tenant.value?.name ?? '' },
    ]),
    cards: computed(() =>
      CARDS.map((c) => ({
        key: c.key,
        icon: c.icon,
        to: `/platform/tenants/${tenantId.value}/${c.segment}`,
      })),
    ),
    toggling: status.pending,
    toggleStatus: () => (tenant.value ? status.toggle(tenant.value) : Promise.resolve()),
  };
}
