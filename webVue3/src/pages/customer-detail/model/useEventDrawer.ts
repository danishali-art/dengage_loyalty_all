import { computed, ref, watch, type Ref } from 'vue';
import { ApiError } from '@/shared/api';
import { prettyJson, useCustomerEventQuery, type RuleFire } from '@/entities/customer';
import type { OpenEventRequest } from './useCustomerDetailPage';

/**
 * The event drawer (port of customer-event.drawer.ts, CR 2026-10-02 §3.4): everything one event
 * did for this customer. A 404 means the event isn't linked to the customer; an id with no inbound
 * event is a scheduled job's postings.
 */
export function useEventDrawer(contactKey: Ref<string>, request: Ref<OpenEventRequest | null>) {
  const eventId = computed(() => request.value?.eventId ?? null);
  const query = useCustomerEventQuery(contactKey, eventId);
  const copied = ref(false);
  watch(eventId, () => (copied.value = false));

  const detail = computed(() => query.data.value ?? null);
  const payloadText = computed(() => JSON.stringify(detail.value?.event?.data ?? null, null, 2));

  return {
    detail,
    loading: query.isLoading,
    notFound: computed(() => query.error.value instanceof ApiError && query.error.value.status === 404),
    failed: computed(() => query.isError.value),
    payloadText,
    copied,
    hasEffects: computed(() => {
      const d = detail.value;
      return (
        !!d &&
        d.streaksApplied.length + d.streakCompletions.length + d.rewards.length + d.tierChanges.length > 0
      );
    }),
    snapshots: (f: RuleFire) =>
      [
        { label: 'customers.drawer.conditions', raw: f.conditionsSnapshot },
        { label: 'customers.drawer.calculation', raw: f.calculationSnapshot },
        { label: 'customers.drawer.resolution', raw: f.resolutionSnapshot },
      ]
        .filter((s): s is { label: string; raw: string } => !!s.raw)
        .map((s) => ({ label: s.label, json: prettyJson(s.raw) })),
    async copyPayload(): Promise<void> {
      try {
        await navigator.clipboard.writeText(payloadText.value);
        copied.value = true;
      } catch {
        // The clipboard can be unavailable (insecure context); the payload stays selectable.
      }
    },
  };
}
