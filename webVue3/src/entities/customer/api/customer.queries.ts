import { computed, ref, toValue, watch, type MaybeRefOrGetter } from 'vue';
import { keepPreviousData, useQueries, useQuery } from '@tanstack/vue-query';
import { useTenantContextStore } from '@/shared/model';
import { CursorPager } from '../model/paging';
import type { CursorPage, EventQuery, LedgerQuery, MessageQuery, RuleFireQuery } from '../model/types';
import {
  getCustomerCapUsage,
  getCustomerCardBuckets,
  getCustomerEvent,
  getCustomerEvents,
  getCustomerLedger,
  getCustomerMessages,
  getCustomerProfile,
  getCustomerRewards,
  getCustomerRuleFires,
  getCustomerStreaks,
  getCustomerTierHistory,
  listCustomers,
} from './customer.api';

export const customerKeys = {
  all: (tenantId: string | null) => ['tenants', tenantId, 'customers'] as const,
  page: (tenantId: string | null, page: number, pageSize: number, search: string) =>
    [...customerKeys.all(tenantId), 'page', { page, pageSize, search }] as const,
  one: (tenantId: string | null, contactKey: string) => [...customerKeys.all(tenantId), contactKey] as const,
  part: (tenantId: string | null, contactKey: string, part: string, params?: unknown) =>
    [...customerKeys.one(tenantId, contactKey), part, params ?? null] as const,
};

export function useCustomersPageQuery(
  page: MaybeRefOrGetter<number>,
  pageSize: MaybeRefOrGetter<number>,
  search: MaybeRefOrGetter<string>,
) {
  const tenant = useTenantContextStore();
  return useQuery({
    queryKey: computed(() =>
      customerKeys.page(tenant.activeTenantId, toValue(page), toValue(pageSize), toValue(search)),
    ),
    queryFn: () => listCustomers(tenant.require(), toValue(page), toValue(pageSize), toValue(search)),
    enabled: computed(() => tenant.hasTenant),
    placeholderData: keepPreviousData,
  });
}

export function useCustomerProfileQuery(contactKey: MaybeRefOrGetter<string>) {
  const tenant = useTenantContextStore();
  return useQuery({
    queryKey: computed(() => customerKeys.part(tenant.activeTenantId, toValue(contactKey), 'profile')),
    queryFn: () => getCustomerProfile(tenant.require(), toValue(contactKey)),
    enabled: computed(() => tenant.hasTenant),
    retry: false,
  });
}

/** One profile per visible list row (the list API has no program count or first-seen date). */
export function useCustomerProfilesQuery(contactKeys: MaybeRefOrGetter<readonly string[]>) {
  const tenant = useTenantContextStore();
  return useQueries({
    queries: computed(() =>
      toValue(contactKeys).map((contactKey) => ({
        queryKey: customerKeys.part(tenant.activeTenantId, contactKey, 'profile'),
        queryFn: () => getCustomerProfile(tenant.require(), contactKey),
        enabled: tenant.hasTenant,
        retry: false,
      })),
    ),
  });
}

/** A whole list the API returns in one go (tier history, cap usage, streaks, rewards, buckets). */
function useCustomerListQuery<T>(
  contactKey: MaybeRefOrGetter<string>,
  part: string,
  fetch: (tenantId: string, contactKey: string) => Promise<T[]>,
) {
  const tenant = useTenantContextStore();
  return useQuery({
    queryKey: computed(() => customerKeys.part(tenant.activeTenantId, toValue(contactKey), part)),
    queryFn: () => fetch(tenant.require(), toValue(contactKey)),
    enabled: computed(() => tenant.hasTenant),
  });
}

export const useCustomerTierHistoryQuery = (contactKey: MaybeRefOrGetter<string>) =>
  useCustomerListQuery(contactKey, 'tier-history', getCustomerTierHistory);
export const useCustomerCapUsageQuery = (contactKey: MaybeRefOrGetter<string>) =>
  useCustomerListQuery(contactKey, 'cap-usage', getCustomerCapUsage);
export const useCustomerStreaksQuery = (contactKey: MaybeRefOrGetter<string>) =>
  useCustomerListQuery(contactKey, 'streaks', getCustomerStreaks);
export const useCustomerRewardsQuery = (contactKey: MaybeRefOrGetter<string>) =>
  useCustomerListQuery(contactKey, 'rewards', getCustomerRewards);
export const useCustomerCardBucketsQuery = (contactKey: MaybeRefOrGetter<string>) =>
  useCustomerListQuery(contactKey, 'card-buckets', getCustomerCardBuckets);

/** The event drawer; disabled while no event is open. */
export function useCustomerEventQuery(
  contactKey: MaybeRefOrGetter<string>,
  eventId: MaybeRefOrGetter<string | null>,
) {
  const tenant = useTenantContextStore();
  return useQuery({
    queryKey: computed(() =>
      customerKeys.part(tenant.activeTenantId, toValue(contactKey), 'event', toValue(eventId)),
    ),
    queryFn: () => getCustomerEvent(tenant.require(), toValue(contactKey), toValue(eventId) as string),
    enabled: computed(() => tenant.hasTenant && !!toValue(eventId)),
    retry: false,
  });
}

type CursorFetch<T, Q> = (
  tenantId: string,
  contactKey: string,
  query: Q,
  cursor: string | null,
  limit: number,
) => Promise<CursorPage<T>>;

/**
 * A cursor-paged customer list driven by the standard paginator (`CursorPager`): the query key
 * carries the page's cursor, and each loaded page records the cursor of the next. `query` is the
 * applied filter — a new one starts again from page 1. Nothing loads while `enabled` is false
 * (a tab not opened yet).
 */
function useCustomerCursorQuery<T, Q>(
  contactKey: MaybeRefOrGetter<string>,
  part: string,
  fetch: CursorFetch<T, Q>,
  query: MaybeRefOrGetter<Q>,
  enabled: MaybeRefOrGetter<boolean> = true,
) {
  const tenant = useTenantContextStore();
  const pager = new CursorPager();
  const page = ref(1);
  const cursor = computed(() => pager.cursorFor(page.value));
  const result = useQuery({
    queryKey: computed(() =>
      customerKeys.part(tenant.activeTenantId, toValue(contactKey), part, {
        query: toValue(query),
        cursor: cursor.value,
        limit: pager.pageSize.value,
      }),
    ),
    queryFn: () =>
      fetch(
        tenant.require(),
        toValue(contactKey),
        toValue(query),
        cursor.value ?? null,
        pager.pageSize.value,
      ),
    enabled: computed(() => tenant.hasTenant && toValue(enabled) && cursor.value !== undefined),
    placeholderData: keepPreviousData,
  });
  // Placeholder data is the previous page's: recording it would file its cursor under this page.
  watch(
    () => [result.data.value, result.isPlaceholderData.value] as const,
    ([data, placeholder]) => {
      if (data && !placeholder) pager.record(page.value, data);
    },
    { immediate: true },
  );
  // A new filter: back to page 1 with no cursors.
  watch(
    () => JSON.stringify(toValue(query)),
    () => {
      pager.reset();
      page.value = 1;
    },
  );

  return {
    rows: computed(() => result.data.value?.data ?? []),
    loading: result.isFetching,
    firstLoad: result.isLoading,
    error: result.isError,
    page,
    pageSize: pager.pageSize,
    total: pager.total,
    /** The paginator only moves one page at a time, so the cursor is always known. */
    go(next: number): void {
      if (pager.cursorFor(next) !== undefined) page.value = next;
    },
    setPageSize(size: number): void {
      pager.setPageSize(size);
      page.value = 1;
    },
    refetch: () => result.refetch(),
  };
}

export const useCustomerLedgerQuery = (
  contactKey: MaybeRefOrGetter<string>,
  query: MaybeRefOrGetter<LedgerQuery>,
  enabled?: MaybeRefOrGetter<boolean>,
) => useCustomerCursorQuery(contactKey, 'ledger', getCustomerLedger, query, enabled);

export const useCustomerEventsQuery = (
  contactKey: MaybeRefOrGetter<string>,
  query: MaybeRefOrGetter<EventQuery>,
  enabled?: MaybeRefOrGetter<boolean>,
) => useCustomerCursorQuery(contactKey, 'events', getCustomerEvents, query, enabled);

export const useCustomerRuleFiresQuery = (
  contactKey: MaybeRefOrGetter<string>,
  query: MaybeRefOrGetter<RuleFireQuery>,
  enabled?: MaybeRefOrGetter<boolean>,
) => useCustomerCursorQuery(contactKey, 'rule-fires', getCustomerRuleFires, query, enabled);

export const useCustomerMessagesQuery = (
  contactKey: MaybeRefOrGetter<string>,
  query: MaybeRefOrGetter<MessageQuery>,
  enabled?: MaybeRefOrGetter<boolean>,
) => useCustomerCursorQuery(contactKey, 'messages', getCustomerMessages, query, enabled);

export type CustomerCursorList = ReturnType<typeof useCustomerLedgerQuery>;
