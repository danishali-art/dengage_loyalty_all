import { computed, ref } from 'vue';
import { useRouter } from 'vue-router';
import { useI18n } from 'vue-i18n';
import { notify, type TableColumn } from '@/shared/ui';
import { useTenantSearchQuery, type Tenant } from '@/entities/tenant';
import { useCreateTenantForm } from '@/features/tenant-create';

const PAGE_SIZE = 50;

/** Platform tenants list (port of tenants-list.page.ts): paging, opening, creating. */
export function useTenantsPage() {
  const { t } = useI18n();
  const router = useRouter();
  const page = ref(1);
  const tenants = useTenantSearchQuery('', page, PAGE_SIZE);
  const createOpen = ref(false);

  const open = (tenant: Tenant) => router.push(`/platform/tenants/${tenant.id}`);

  const create = useCreateTenantForm((tenant) => {
    createOpen.value = false;
    notify.success(t('platform.tenants.created', { name: tenant.name }));
    void open(tenant);
  });

  function openCreate(): void {
    create.reset();
    createOpen.value = true;
  }

  const columns = computed<TableColumn[]>(() => [
    { key: 'name', header: t('platform.tenants.col.name') },
    { key: 'id', header: t('platform.tenants.col.id') },
    { key: 'status', header: t('platform.tenants.col.status') },
    { key: 'createdAt', header: t('platform.tenants.col.created') },
  ]);

  return {
    page,
    pageSize: PAGE_SIZE,
    rows: computed(() => tenants.data.value?.data ?? []),
    total: computed(() => tenants.data.value?.total ?? 0),
    loading: tenants.isFetching,
    columns,
    open,
    createOpen,
    openCreate,
    create,
  };
}
