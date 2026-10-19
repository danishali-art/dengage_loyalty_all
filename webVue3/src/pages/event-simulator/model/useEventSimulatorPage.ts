import { computed, onBeforeUnmount, ref } from 'vue';
import { useI18n } from 'vue-i18n';
import { ApiError } from '@/shared/api';
import { useTenantContextStore } from '@/shared/model';
import { notify } from '@/shared/ui';
import { useEventTypesQuery } from '@/entities/event-type';
import {
  eventHelp,
  eventSampleJson,
  eventStatusTone,
  getEventStatus,
  simulateEvent,
  type EventStatus,
} from '@/entities/event';

const STATUS_POLL_INTERVAL_MS = 1500;
const STATUS_POLL_ATTEMPTS = 6;

/**
 * Event Simulator (port of event-simulator.page.ts): publish a test event through the same
 * pipeline as a real integration, then follow its status. The payload starts as the selected
 * type's sample; an edited payload is kept when the type changes.
 */
export function useEventSimulatorPage() {
  const { t } = useI18n();
  const tenant = useTenantContextStore();
  const types = useEventTypesQuery();

  const eventType = ref<string | null>(null);
  const isGeneric = (type: string | null): boolean =>
    !type || (types.data.value?.generic.includes(type) ?? false);
  /** The selected type's sample; a tenant-defined one before a type is picked. */
  const sampleJson = computed(() => eventSampleJson(eventType.value, isGeneric(eventType.value)));
  const dataJson = ref(eventSampleJson(null, true));
  const sending = ref(false);
  const polling = ref(false);
  const formErrors = ref<string[]>([]);
  const eventId = ref<string | null>(null);
  const status = ref<EventStatus | null>(null);
  let stopped = false;
  onBeforeUnmount(() => (stopped = true));

  // The API leaves internally scheduled types out of `publishable`.
  const publishable = computed(() => types.data.value?.publishable ?? []);
  const publishableBuiltIns = computed(() => {
    const builtIn = types.data.value?.builtIn ?? [];
    return publishable.value.filter((type) => builtIn.includes(type));
  });

  /** Swaps in the new type's sample — unless the admin edited the JSON, which is kept. */
  function selectEventType(type: string | null): void {
    const untouched = dataJson.value === sampleJson.value;
    eventType.value = type;
    if (untouched) dataJson.value = sampleJson.value;
  }

  async function pollStatus(): Promise<void> {
    const id = eventId.value;
    if (!id || polling.value) return;
    polling.value = true;
    try {
      for (let attempt = 0; attempt < STATUS_POLL_ATTEMPTS && !stopped; attempt++) {
        const current = await getEventStatus(tenant.require(), id);
        status.value = current;
        if (current.status !== 'pending') return;
        await new Promise((resolve) => setTimeout(resolve, STATUS_POLL_INTERVAL_MS));
      }
    } catch {
      // The status stays whatever it last was — "Refresh status" can try again.
    } finally {
      polling.value = false;
    }
  }

  async function send(): Promise<void> {
    const type = eventType.value;
    if (!type || sending.value) return;
    let data: Record<string, unknown>;
    try {
      const parsed: unknown = JSON.parse(dataJson.value);
      if (typeof parsed !== 'object' || parsed === null || Array.isArray(parsed))
        throw new Error('not an object');
      data = parsed as Record<string, unknown>;
    } catch {
      formErrors.value = [t('events.simulator.invalidJson')];
      return;
    }
    sending.value = true;
    formErrors.value = [];
    eventId.value = null;
    status.value = null;
    try {
      const accepted = await simulateEvent(tenant.require(), type, data);
      eventId.value = accepted.eventId;
      notify.success(
        t('events.simulator.published'),
        t('events.simulator.acceptedAs', { id: accepted.eventId }),
      );
      void pollStatus();
    } catch (error) {
      formErrors.value = [error instanceof ApiError ? error.message : t('common.genericError')];
    } finally {
      sending.value = false;
    }
  }

  return {
    typesLoaded: computed(() => !!types.data.value),
    typesFailed: types.isError,
    eventTypeOptions: computed(() => publishable.value.map((type) => ({ value: type, label: type }))),
    typesHint: computed(() =>
      t('events.simulator.typesHint', {
        builtIn: publishableBuiltIns.value.join(', '),
        generic: types.data.value?.generic.join(', ') || t('events.simulator.noGeneric'),
      }),
    ),
    eventType,
    selectEventType,
    dataJson,
    sampleJson,
    useSample: () => (dataJson.value = sampleJson.value),
    help: computed(() => eventHelp(eventType.value, isGeneric(eventType.value))),
    sending,
    polling,
    formErrors,
    eventId,
    status,
    statusTone: eventStatusTone,
    send,
    pollStatus,
    reset(): void {
      eventId.value = null;
      status.value = null;
      formErrors.value = [];
    },
  };
}
