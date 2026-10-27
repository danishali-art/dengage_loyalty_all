/** Customers list with only the entity api functions mocked. */
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { flushPromises } from '@vue/test-utils';
import { createPinia } from 'pinia';
import { mountWithProviders } from '../../../../tests/utils/mount-with-providers';
import { useTenantContextStore } from '@/shared/model';
import * as customerApi from '@/entities/customer/api/customer.api';
import type { CustomerProfile } from '@/entities/customer';
import CustomersPage from './CustomersPage.vue';

vi.mock('@/entities/customer/api/customer.api');

const profile = (contactKey: string, programs: string[]): CustomerProfile => ({
  contactKey,
  balances: [],
  summary: {
    firstSeenAt: '2026-10-01T10:00:00Z',
    lastActivityAt: '2026-10-03T10:00:00Z',
    failedEventsLast7Days: 0,
    activeStreakCount: 0,
  },
  programs: programs.map((name, i) => ({ programId: `p${i}`, programName: name, wallets: [], streaks: [] })),
});

describe('CustomersPage', () => {
  beforeEach(() => {
    vi.mocked(customerApi.listCustomers).mockResolvedValue({
      data: [
        { contactKey: 'burn_a', accountCount: 3, lastActivityAt: '2026-10-03T10:00:00Z' },
        { contactKey: 'burn_b', accountCount: 1, lastActivityAt: '2026-10-02T10:00:00Z' },
      ],
      page: 1,
      pageSize: 10,
      total: 2,
    });
    vi.mocked(customerApi.getCustomerProfile).mockImplementation((_tenant, key) =>
      Promise.resolve(profile(key, key === 'burn_a' ? ['FinPay Rewards', 'third one'] : ['FinPay Rewards'])),
    );
  });

  afterEach(() => {
    vi.clearAllMocks();
    document.body.innerHTML = '';
  });

  async function mountPage() {
    const pinia = createPinia();
    const routes = [
      { path: '/', component: { render: () => null } },
      { path: '/customers/:contactKey', component: { render: () => null } },
    ];
    const mounted = await mountWithProviders(CustomersPage, { pinia, routes });
    useTenantContextStore(pinia).set('novapay');
    await flushPromises();
    return mounted;
  }

  it('lists customers with the program count from each profile', async () => {
    const { wrapper } = await mountPage();
    await vi.waitFor(() => expect(wrapper.findAll('tbody tr')).toHaveLength(2));
    const first = wrapper.findAll('tbody tr')[0]!;
    expect(first.text()).toContain('burn_a');
    await vi.waitFor(() =>
      expect(first.find('[title]').attributes('title')).toBe('FinPay Rewards\nthird one'),
    );
    expect(customerApi.listCustomers).toHaveBeenCalledWith('novapay', 1, 10, '');
  });

  it('opens a customer on Enter when the search is an exact contact key', async () => {
    const { wrapper, router } = await mountPage();
    await wrapper.find('input').setValue('burn_b');
    await wrapper.find('form').trigger('submit');
    await vi.waitFor(() => expect(router.currentRoute.value.path).toBe('/customers/burn_b'));
    expect(customerApi.listCustomers).toHaveBeenLastCalledWith('novapay', 1, 10, 'burn_b');
  });
});
