import { computed, onBeforeUnmount, ref, watch, type MaybeRefOrGetter } from 'vue';
import { useRouter } from 'vue-router';
import { useTenantContextStore } from '@/shared/model';
import { useTenantSearchQuery, type Tenant } from '@/entities/tenant';

const PICKER_PAGE_SIZE = 20;
const SEARCH_DEBOUNCE_MS = 300;

/**
 * Search, paginate and pick a tenant (port of TenantStore.searchTenants / setTenant). Used by the
 * select-tenant page and the topbar's switcher dialog.
 */
export function useTenantPicker(
  options: { onSelected?: () => void; active?: MaybeRefOrGetter<boolean> } = {},
) {
  const tenant = useTenantContextStore();
  const router = useRouter();

  const query = ref('');
  const debouncedQuery = ref('');
  const page = ref(1);
  let timer: ReturnType<typeof setTimeout> | undefined;

  watch(query, (value) => {
    clearTimeout(timer);
    timer = setTimeout(() => {
      debouncedQuery.value = value;
      page.value = 1;
    }, SEARCH_DEBOUNCE_MS);
  });
  onBeforeUnmount(() => clearTimeout(timer));

  const result = useTenantSearchQuery(debouncedQuery, page, PICKER_PAGE_SIZE, options.active ?? true);

  async function select(picked: Tenant): Promise<void> {
    options.onSelected?.();
    if (tenant.activeTenantId === picked.id) return;
    tenant.set(picked.id);
    // Routed state is tenant-specific (program ids) — start over at the programs list.
    await router.push('/programs');
  }

  return {
    query,
    page,
    pageSize: PICKER_PAGE_SIZE,
    tenants: computed(() => result.data.value?.data ?? []),
    total: computed(() => result.data.value?.total ?? 0),
    loading: result.isFetching,
    activeTenantId: computed(() => tenant.activeTenantId),
    select,
  };
}
