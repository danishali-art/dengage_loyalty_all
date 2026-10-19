import { computed, reactive, ref, toRaw, type Ref } from 'vue';
import { useI18n } from 'vue-i18n';
import {
  CUSTOMER_PAGE_SIZE,
  INBOX_STATUSES,
  OUTBOX_STATUSES,
  REASON_GROUPS,
  capTile,
  defaultActivityFilter,
  defaultEventFilter,
  defaultMessageFilter,
  defaultRuleFireFilter,
  pageSlice,
  programOptions,
  toEventQuery,
  toLedgerQuery,
  toMessageQuery,
  toRuleFireQuery,
  useCustomerCapUsageQuery,
  useCustomerCardBucketsQuery,
  useCustomerEventsQuery,
  useCustomerLedgerQuery,
  useCustomerMessagesQuery,
  useCustomerRewardsQuery,
  useCustomerRuleFiresQuery,
  useCustomerStreaksQuery,
  useCustomerTierHistoryQuery,
  walletOptions,
  type CustomerProfile,
  type CustomerStreak,
  type LedgerQuery,
} from '@/entities/customer';
import { meterPercent } from '@/shared/ui';
import { useEventTypesQuery } from '@/entities/event-type';

/** "All" plus the given options, for the filter selects ('' = no filter). */
function withAll(t: (key: string) => string, options: { value: string; label: string }[]) {
  return [{ value: '', label: t('customers.filter.all') }, ...options];
}

/**
 * A filter bar's two copies: what the admin is editing, and what the list was searched with.
 * Search applies the draft (and refetches even when nothing changed, as the Angular bar did).
 */
function useFilter<V extends object, Q>(initial: () => V, toQuery: (value: V) => Q) {
  const draft = reactive(initial()) as V;
  const applied = ref(toQuery(structuredClone(toRaw(draft)))) as Ref<Q>;
  return {
    draft,
    applied,
    apply(refetch: () => unknown): void {
      const next = toQuery(structuredClone(toRaw(draft)));
      if (JSON.stringify(next) === JSON.stringify(applied.value)) void refetch();
      else applied.value = next;
    },
    restore(): void {
      Object.assign(draft, initial());
    },
  };
}

/** Activity: every posting, with where it came from (CR 2026-10-02 P1). */
export function useActivityTab(contactKey: Ref<string>, profile: Ref<CustomerProfile>) {
  const { t } = useI18n();
  const filter = useFilter(defaultActivityFilter, toLedgerQuery);
  const list = useCustomerLedgerQuery(contactKey, filter.applied);
  return {
    filter: filter.draft,
    list,
    programOptions: computed(() =>
      withAll(
        t,
        programOptions(profile.value.balances).map((p) => ({ value: p.id, label: p.name })),
      ),
    ),
    walletOptions: computed(() =>
      withAll(
        t,
        walletOptions(profile.value.balances, filter.draft.programId).map((w) => ({
          value: w.accountTypeId,
          label: w.accountTypeName,
        })),
      ),
    ),
    reasonOptions: computed(() =>
      withAll(
        t,
        REASON_GROUPS.map((g) => ({ value: g, label: t(`customers.reasonGroup.${g}`) })),
      ),
    ),
    search: () => filter.apply(list.refetch),
    reset(): void {
      filter.restore();
      filter.apply(list.refetch);
    },
  };
}

/** Events: what the customer sent (received after the CR). */
export function useEventsTab(contactKey: Ref<string>) {
  const { t } = useI18n();
  const types = useEventTypesQuery();
  const filter = useFilter(defaultEventFilter, toEventQuery);
  const list = useCustomerEventsQuery(contactKey, filter.applied);
  return {
    filter: filter.draft,
    list,
    typeOptions: computed(() =>
      withAll(
        t,
        [...(types.data.value?.builtIn ?? []), ...(types.data.value?.generic ?? [])].map((type) => ({
          value: type,
          label: type,
        })),
      ),
    ),
    statusOptions: computed(() =>
      withAll(
        t,
        INBOX_STATUSES.map((s) => ({ value: s, label: t(`customers.status.${s}`) })),
      ),
    ),
    search: () => filter.apply(list.refetch),
    reset(): void {
      filter.restore();
      filter.apply(list.refetch);
    },
  };
}

/** Rules & caps (P2): per-customer cap usage (D7) and every rule decision for the customer. */
export function useRulesTab(contactKey: Ref<string>) {
  const caps = useCustomerCapUsageQuery(contactKey);
  const ruleFilter = ref<{ id: string; name: string } | null>(null);
  const filter = useFilter(defaultRuleFireFilter, (v) => v);
  const query = computed(() => toRuleFireQuery(filter.applied.value, ruleFilter.value?.id));
  const fires = useCustomerRuleFiresQuery(contactKey, query);
  /** Closest to its limit first, so a reached cap is never off-screen; card buckets have their own tab. */
  const tiles = computed(() =>
    (caps.data.value ?? [])
      .filter((c) => !c.isCardBucket)
      .map(capTile)
      .sort((a, b) => b.percent - a.percent),
  );
  return {
    tiles,
    reachedCount: computed(() => tiles.value.filter((tile) => tile.reached).length),
    capsLoading: caps.isLoading,
    capsError: caps.isError,
    filter: filter.draft,
    ruleFilter,
    fires,
    search: () => filter.apply(fires.refetch),
    reset(): void {
      filter.restore();
      ruleFilter.value = null;
      filter.apply(fires.refetch);
    },
    filterRule: (id: string, name: string) => (ruleFilter.value = { id, name }),
    clearRule: () => (ruleFilter.value = null),
  };
}

/** Streaks (P2): progress per campaign, with its completion history paged in the browser. */
export function useStreaksTab(contactKey: Ref<string>) {
  const streaks = useCustomerStreaksQuery(contactKey);
  const pageSize = ref(CUSTOMER_PAGE_SIZE);
  const pages = ref<Record<string, number>>({});
  return {
    streaks: computed(() => streaks.data.value ?? []),
    loading: streaks.isLoading,
    error: streaks.isError,
    pageSize,
    pageOf: (campaignId: string) => pages.value[campaignId] ?? 1,
    setPage: (campaignId: string, page: number) => (pages.value = { ...pages.value, [campaignId]: page }),
    historyPage: (s: CustomerStreak) => pageSlice(s.history, pages.value[s.campaignId] ?? 1, pageSize.value),
    /** One page view for every campaign's history; a new size starts each at page 1. */
    changePageSize(size: number): void {
      pageSize.value = size;
      pages.value = {};
    },
  };
}

/** A whole list paged in the browser (rewards, tier history). */
function useSlicedList<T>(list: Ref<readonly T[]>) {
  const page = ref(1);
  const pageSize = ref(CUSTOMER_PAGE_SIZE);
  return {
    page,
    pageSize,
    rows: computed(() => pageSlice(list.value, page.value, pageSize.value)),
    total: computed(() => list.value.length),
    changePageSize(size: number): void {
      pageSize.value = size;
      page.value = 1;
    },
  };
}

/** Rewards (P2): bought with points or granted by streaks — what each cost and what it paid. */
export function useRewardsTab(contactKey: Ref<string>) {
  const rewards = useCustomerRewardsQuery(contactKey);
  return {
    loading: rewards.isLoading,
    error: rewards.isError,
    paged: useSlicedList(computed(() => rewards.data.value ?? [])),
  };
}

/** Tiers (P2): status per program (tiles), and every tier change with its cause. */
export function useTiersTab(contactKey: Ref<string>, profile: Ref<CustomerProfile>) {
  const history = useCustomerTierHistoryQuery(contactKey);
  return {
    /** One tile per program that has tiers; the ring is the progress to the next tier. */
    tiles: computed(() =>
      (profile.value.programs ?? []).flatMap((p) =>
        p.tier
          ? [
              {
                programId: p.programId,
                programName: p.programName,
                tier: p.tier,
                percent: p.tier.nextTierMinPoints
                  ? Math.round(meterPercent(p.tier.qualifyingPoints, p.tier.nextTierMinPoints))
                  : 100,
              },
            ]
          : [],
      ),
    ),
    loading: history.isLoading,
    error: history.isError,
    paged: useSlicedList(computed(() => history.data.value ?? [])),
  };
}

/** Card buckets (P3): each bucket's caps against this customer's use, and its postings on demand. */
export function useCardBucketsTab(contactKey: Ref<string>) {
  const buckets = useCustomerCardBucketsQuery(contactKey);
  const selected = ref<string | null>(null);
  // All time: a bucket's postings are few, and its own totals are all-time too.
  const query = computed<LedgerQuery>(() => ({ ruleId: selected.value ?? undefined }));
  const postings = useCustomerLedgerQuery(contactKey, query, () => !!selected.value);
  return {
    buckets: computed(() => buckets.data.value ?? []),
    loading: buckets.isLoading,
    error: buckets.isError,
    selected,
    postings,
    toggle: (ruleId: string) => (selected.value = selected.value === ruleId ? null : ruleId),
  };
}

/** Messages sent (P3): outbound events for the customer — never the payload (D5). */
export function useMessagesTab(contactKey: Ref<string>) {
  const { t } = useI18n();
  const filter = useFilter(defaultMessageFilter, toMessageQuery);
  const list = useCustomerMessagesQuery(contactKey, filter.applied);
  return {
    filter: filter.draft,
    list,
    statusOptions: computed(() =>
      withAll(
        t,
        OUTBOX_STATUSES.map((s) => ({ value: s, label: t(`customers.messages.status.${s}`) })),
      ),
    ),
    search: () => filter.apply(list.refetch),
    reset(): void {
      filter.restore();
      filter.apply(list.refetch);
    },
  };
}
