/** Tiers page with only the entity api functions mocked: order, reorder, delete guard, create. */
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { flushPromises } from '@vue/test-utils';
import { createPinia } from 'pinia';
import { mountWithProviders } from '../../../../tests/utils/mount-with-providers';
import { useTenantContextStore } from '@/shared/model';
import * as tierApi from '@/entities/tier/api/tier.api';
import * as programApi from '@/entities/program/api/program.api';
import type { Tier } from '@/entities/tier';
import type { Program } from '@/entities/program';
import TiersPage from './TiersPage.vue';

vi.mock('@/entities/tier/api/tier.api');
vi.mock('@/entities/program/api/program.api');

const tier = (id: string, sortOrder: number, extra: Partial<Tier> = {}): Tier =>
  ({
    id,
    name: id,
    displayName: id.toUpperCase(),
    minPoints: String(sortOrder * 1000),
    qualifyingModel: 'lifetime',
    graceDays: 0,
    sortOrder,
    createdAt: '2026-01-01T00:00:00Z',
    hasAssignedAccounts: false,
    ...extra,
  }) as Tier;

describe('TiersPage', () => {
  beforeEach(() => {
    vi.mocked(programApi.getProgram).mockResolvedValue({
      id: 'p1',
      name: 'NovaPay',
      status: 'inactive',
    } as Program);
    vi.mocked(tierApi.listTiers).mockResolvedValue({
      // Out of order on purpose: the page shows them by sortOrder.
      data: [tier('silver', 1), tier('bronze', 0, { hasAssignedAccounts: true }), tier('gold', 2)],
      page: 1,
      pageSize: 100,
      total: 3,
    });
    vi.mocked(tierApi.reorderTiers).mockResolvedValue(undefined);
  });

  afterEach(() => {
    vi.clearAllMocks();
    document.body.innerHTML = '';
  });

  async function mountPage() {
    const pinia = createPinia();
    const mounted = await mountWithProviders(TiersPage, { pinia, props: { programId: 'p1' } });
    useTenantContextStore(pinia).set('novapay');
    await flushPromises();
    return mounted;
  }

  it('lists tiers in sortOrder and reorders by swapping neighbours', async () => {
    const { wrapper } = await mountPage();
    const names = wrapper.findAll('tbody tr').map((r) => r.text());
    expect(names[0]).toContain('BRONZE');
    expect(names[2]).toContain('GOLD');

    // "Move down" on the first row (bronze) swaps it with silver.
    await wrapper.findAll('button[aria-label="Move down"]')[0]!.trigger('click');
    await vi.waitFor(() =>
      expect(tierApi.reorderTiers).toHaveBeenCalledWith('novapay', 'p1', ['silver', 'bronze', 'gold']),
    );
  });

  it('disables Delete for a tier with customers, explaining why', async () => {
    const { wrapper } = await mountPage();
    const bronzeDelete = wrapper
      .findAll('tbody tr')[0]!
      .findAll('button')
      .find((b) => b.text() === 'Delete')!;
    expect(bronzeDelete.attributes('disabled')).toBeDefined();
    expect(bronzeDelete.attributes('title')).toContain('customer accounts');
  });

  it('creates a tier at the end of the order, with points as a decimal string', async () => {
    vi.mocked(tierApi.createTier).mockResolvedValue(tier('platinum', 3));
    const { wrapper } = await mountPage();
    await wrapper
      .findAll('button')
      .find((b) => b.text() === 'Add tier')!
      .trigger('click');
    await flushPromises();
    const fill = (id: string, value: string) => {
      const input = document.querySelector<HTMLInputElement>(id)!;
      input.value = value;
      input.dispatchEvent(new Event('input'));
    };
    fill('#tierName', 'platinum');
    fill('#displayName', 'Platinum');
    fill('#minPoints', '50000.25');
    await flushPromises();
    [...document.querySelectorAll('button')].find((b) => b.textContent?.trim() === 'Create')!.click();
    await vi.waitFor(() =>
      expect(tierApi.createTier).toHaveBeenCalledWith('novapay', 'p1', {
        name: 'platinum',
        displayName: 'Platinum',
        minPoints: '50000.25',
        qualifyingModel: 'lifetime',
        qualifyingPeriodDays: null,
        graceDays: 0,
        sortOrder: 3,
      }),
    );
  });
});
