import { computed, onBeforeUnmount, ref, watch } from 'vue';
import { useRouter } from 'vue-router';
import {
  CUSTOMER_PAGE_SIZE,
  CUSTOMER_PAGE_SIZES,
  customerListRow,
  useCustomerProfilesQuery,
  useCustomersPageQuery,
  type CustomerListRow,
} from '@/entities/customer';

const SEARCH_DEBOUNCE_MS = 350;

/**
 * Customers list, Layout A: one search box (Enter on an exact
 * contact key opens the profile), the table and the paginator. Program count and first seen come
 * from the profile endpoint, fetched for the visible rows only — headline numbers, segments and
 * sorting need list-API changes (a scope change).
 */
export function useCustomersPage() {
  const router = useRouter();
  const search = ref('');
  /** The search the list shows — typing applies it after a pause, Enter at once. */
  const applied = ref('');
  const page = ref(1);
  const pageSize = ref(CUSTOMER_PAGE_SIZE);
  const list = useCustomersPageQuery(page, pageSize, applied);
  const rows = computed(() => list.data.value?.data ?? []);
  const profiles = useCustomerProfilesQuery(() => rows.value.map((c) => c.contactKey));

  let debounce: ReturnType<typeof setTimeout> | undefined;
  onBeforeUnmount(() => clearTimeout(debounce));
  watch(search, (value) => {
    clearTimeout(debounce);
    debounce = setTimeout(() => apply(value), SEARCH_DEBOUNCE_MS);
  });
  function apply(value: string): void {
    applied.value = value.trim();
    page.value = 1;
  }

  const open = (contactKey: string): Promise<unknown> =>
    router.push(`/customers/${encodeURIComponent(contactKey)}`);

  return {
    search,
    rows,
    /** Per-row facts from the profile endpoint, by contact key; a row whose profile failed has none. */
    details: computed<Record<string, CustomerListRow>>(() =>
      Object.fromEntries(
        profiles.value.flatMap((q, i) => {
          const key = rows.value[i]?.contactKey;
          return q.data && key ? [[key, customerListRow(q.data)]] : [];
        }),
      ),
    ),
    page,
    pageSize,
    pageSizes: CUSTOMER_PAGE_SIZES,
    total: computed(() => list.data.value?.total ?? 0),
    loading: list.isFetching,
    searched: computed(() => applied.value !== ''),
    /** Enter / Search: an exact contact-key match opens that profile; otherwise it filters. */
    async submit(): Promise<void> {
      clearTimeout(debounce);
      apply(search.value);
      const key = applied.value;
      const result = await list.refetch();
      if (key && result.data?.data.some((c) => c.contactKey === key)) await open(key);
    },
    reset(): void {
      clearTimeout(debounce);
      search.value = '';
      apply('');
    },
    changePageSize(size: number): void {
      pageSize.value = size;
      page.value = 1;
    },
    open,
  };
}
