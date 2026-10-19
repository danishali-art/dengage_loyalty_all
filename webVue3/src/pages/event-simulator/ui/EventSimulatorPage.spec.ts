/** Event Simulator with only the entity api functions mocked. */
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { flushPromises } from '@vue/test-utils';
import { createPinia } from 'pinia';
import { mountWithProviders } from '../../../../tests/utils/mount-with-providers';
import { useTenantContextStore } from '@/shared/model';
import * as eventApi from '@/entities/event/api/event.api';
import * as eventTypeApi from '@/entities/event-type/api/event-type.api';
import EventSimulatorPage from './EventSimulatorPage.vue';

vi.mock('@/entities/event/api/event.api');
vi.mock('@/entities/event-type/api/event-type.api');

describe('EventSimulatorPage', () => {
  beforeEach(() => {
    vi.mocked(eventTypeApi.getEventTypes).mockResolvedValue({
      builtIn: ['order.created'],
      generic: ['remittance.sent'],
      publishable: ['order.created', 'remittance.sent'],
    });
    vi.mocked(eventApi.simulateEvent).mockResolvedValue({ eventId: 'evt-9', status: 'accepted' });
    vi.mocked(eventApi.getEventStatus).mockResolvedValue({
      eventId: 'evt-9',
      eventType: 'order.created',
      status: 'processed',
      receivedAt: '2026-10-12T10:00:00Z',
      processedAt: '2026-10-12T10:00:01Z',
    });
  });

  afterEach(() => {
    vi.clearAllMocks();
    document.body.innerHTML = '';
  });

  async function mountPage() {
    const pinia = createPinia();
    const mounted = await mountWithProviders(EventSimulatorPage, { pinia });
    useTenantContextStore(pinia).set('novapay');
    await flushPromises();
    return mounted;
  }

  async function pickType(label: string) {
    document.querySelector<HTMLElement>('#eventType')!.click();
    await flushPromises();
    [...document.querySelectorAll<HTMLElement>('[role="option"]')]
      .find((o) => o.textContent?.trim() === label)!
      .click();
    await flushPromises();
  }
  const textarea = () => document.querySelector<HTMLTextAreaElement>('#eventData')!;
  const sendButton = () =>
    [...document.querySelectorAll<HTMLButtonElement>('button')].find(
      (b) => b.textContent?.trim() === 'Send event',
    )!;

  it('swaps in the chosen type’s sample, sends it, and shows the processed status', async () => {
    await mountPage();
    await pickType('order.created');
    expect(JSON.parse(textarea().value)).toEqual({
      contact_key: 'cust_test_1',
      amount: '1250',
      channel: 'web',
    });

    sendButton().click();
    await vi.waitFor(() =>
      expect(eventApi.simulateEvent).toHaveBeenCalledWith('novapay', 'order.created', {
        contact_key: 'cust_test_1',
        amount: '1250',
        channel: 'web',
      }),
    );
    await vi.waitFor(() => expect(eventApi.getEventStatus).toHaveBeenCalledWith('novapay', 'evt-9'));
    await vi.waitFor(() => expect(document.body.textContent).toContain('evt-9'));
    expect(document.body.textContent).toContain('processed');
  });

  it('keeps an edited payload when the type changes, and refuses JSON that is not an object', async () => {
    await mountPage();
    await pickType('order.created');
    textarea().value = '[1, 2]';
    textarea().dispatchEvent(new Event('input'));
    await flushPromises();
    await pickType('remittance.sent');
    expect(textarea().value).toBe('[1, 2]');

    sendButton().click();
    await flushPromises();
    expect(document.body.textContent).toContain('Event data must be valid JSON');
    expect(eventApi.simulateEvent).not.toHaveBeenCalled();
  });
});
