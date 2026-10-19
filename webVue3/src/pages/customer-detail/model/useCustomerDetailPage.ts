import { computed, ref, toRef } from 'vue';
import { useI18n } from 'vue-i18n';
import type { Crumb } from '@/shared/ui';
import { useCustomerProfileQuery } from '@/entities/customer';

export const CUSTOMER_TABS = [
  'overview',
  'activity',
  'events',
  'rules',
  'streaks',
  'rewards',
  'tiers',
  'cardBuckets',
  'messages',
] as const;
export type CustomerTab = (typeof CUSTOMER_TABS)[number];

/** What a tab asks the page to open in the drawer: an event, and the posting to highlight. */
export interface OpenEventRequest {
  eventId: string;
  postingId?: string;
}

/**
 * Customer 360 (port of customer-detail.page.ts, CR 2026-10-02): header with the tabs, the
 * selected tab's content, and the event drawer any tab can open.
 */
export function useCustomerDetailPage(props: { contactKey: string }) {
  const { t } = useI18n();
  const contactKey = toRef(props, 'contactKey');
  const profile = useCustomerProfileQuery(contactKey);
  const tab = ref<CustomerTab>('overview');
  const drawer = ref<OpenEventRequest | null>(null);

  return {
    contactKey,
    crumbs: computed<Crumb[]>(() => [
      { label: t('nav.customers'), to: '/customers' },
      { label: contactKey.value },
    ]),
    tabs: computed(() => CUSTOMER_TABS.map((value) => ({ value, label: t(`customers.tab.${value}`) }))),
    tab,
    loading: profile.isLoading,
    /** A failed profile load reads as "not found", as in the Angular page. */
    profile: computed(() => profile.data.value ?? null),
    drawer,
    openEvent: (request: OpenEventRequest) => (drawer.value = request),
    copyContactKey(): void {
      void navigator.clipboard?.writeText(contactKey.value).catch(() => undefined);
    },
  };
}
