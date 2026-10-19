import { computed, ref, toRef } from 'vue';
import { useI18n } from 'vue-i18n';
import type { Crumb, TableColumn } from '@/shared/ui';
import { useTenantQuery } from '@/entities/tenant';
import { useApiKeysPageQuery } from '@/entities/api-key';
import { useApiKeyManage } from '@/features/api-key-manage';

const PAGE_SIZE = 15;

/** A tenant's API keys (port of api-keys-list.page.ts): paging, create (shown once), revoke. */
export function useApiKeysPage(props: { tenantId: string }) {
  const { t } = useI18n();
  const tenantId = toRef(props, 'tenantId');
  const tenant = useTenantQuery(tenantId);
  const page = ref(1);
  const keys = useApiKeysPageQuery(tenantId, page, PAGE_SIZE);
  const manage = useApiKeyManage(tenantId);

  return {
    crumbs: computed<Crumb[]>(() => [
      { label: t('platform.heading'), to: '/platform/tenants' },
      { label: tenant.data.value?.name ?? tenantId.value, to: `/platform/tenants/${tenantId.value}` },
      { label: t('platform.apiKeys.heading') },
    ]),
    page,
    pageSize: PAGE_SIZE,
    rows: computed(() => keys.data.value?.data ?? []),
    total: computed(() => keys.data.value?.total ?? 0),
    loading: keys.isFetching,
    columns: computed<TableColumn[]>(() => [
      { key: 'prefix', header: t('platform.apiKeys.col.prefix') },
      { key: 'createdAt', header: t('platform.apiKeys.col.created') },
      { key: 'lastUsedAt', header: t('platform.apiKeys.col.lastUsed') },
      { key: 'actions', header: t('platform.apiKeys.col.actions'), align: 'right' },
    ]),
    manage,
  };
}
