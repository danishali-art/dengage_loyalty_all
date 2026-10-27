import { computed, ref, toRef } from 'vue';
import { useI18n } from 'vue-i18n';
import { notify, type Crumb, type TableColumn } from '@/shared/ui';
import { useTenantQuery } from '@/entities/tenant';
import { useAdminUsersPageQuery } from '@/entities/admin-user';
import { useCreateAdminUserForm } from '@/features/admin-user-create';

const PAGE_SIZE = 15;

/** A tenant's admins: paging and creating. */
export function useAdminUsersPage(props: { tenantId: string }) {
  const { t } = useI18n();
  const tenantId = toRef(props, 'tenantId');
  const tenant = useTenantQuery(tenantId);
  const page = ref(1);
  const admins = useAdminUsersPageQuery(tenantId, page, PAGE_SIZE);
  const createOpen = ref(false);

  const create = useCreateAdminUserForm(tenantId, (user) => {
    createOpen.value = false;
    notify.success(t('platform.admins.created', { email: user.email }));
  });

  function openCreate(): void {
    create.reset();
    createOpen.value = true;
  }

  const rows = computed(() => admins.data.value?.data ?? []);

  return {
    crumbs: computed<Crumb[]>(() => [
      { label: t('platform.heading'), to: '/platform/tenants' },
      { label: tenant.data.value?.name ?? tenantId.value, to: `/platform/tenants/${tenantId.value}` },
      { label: t('platform.admins.heading') },
    ]),
    page,
    pageSize: PAGE_SIZE,
    rows,
    total: computed(() => admins.data.value?.total ?? 0),
    loading: admins.isFetching,
    /** The empty state replaces the table only once the first page has loaded empty. */
    empty: computed(() => admins.isSuccess.value && rows.value.length === 0 && page.value === 1),
    columns: computed<TableColumn[]>(() => [
      { key: 'email', header: t('platform.admins.col.email') },
      { key: 'status', header: t('platform.admins.col.status') },
      { key: 'createdAt', header: t('platform.admins.col.created') },
    ]),
    createOpen,
    openCreate,
    create,
  };
}
