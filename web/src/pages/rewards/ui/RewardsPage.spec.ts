/** Rewards page with only the entity api functions mocked: list, approve, retired view-only. */
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { flushPromises } from '@vue/test-utils';
import { createPinia } from 'pinia';
import { mountWithProviders } from '../../../../tests/utils/mount-with-providers';
import { apiErrorFrom } from '@/shared/api';
import { useTenantContextStore } from '@/shared/model';
import * as rewardApi from '@/entities/reward/api/reward.api';
import * as programApi from '@/entities/program/api/program.api';
import * as accountTypeApi from '@/entities/account-type/api/account-type.api';
import * as tierApi from '@/entities/tier/api/tier.api';
import type { Reward } from '@/entities/reward';
import type { Program } from '@/entities/program';
import RewardsPage from './RewardsPage.vue';

vi.mock('@/entities/reward/api/reward.api');
vi.mock('@/entities/program/api/program.api');
vi.mock('@/entities/account-type/api/account-type.api');
vi.mock('@/entities/tier/api/tier.api');

const reward = (id: string, extra: Partial<Reward>): Reward =>
  ({
    id,
    name: `novapay_${id}`,
    displayName: id,
    acquisition: 'points_purchase',
    rewardType: 'cashback',
    isActive: true,
    status: 'active',
    createdAt: '2026-01-01T00:00:00Z',
    typeConfig: {},
    ...extra,
  }) as Reward;

describe('RewardsPage', () => {
  beforeEach(() => {
    vi.mocked(programApi.getProgram).mockResolvedValue({
      id: 'p1',
      name: 'NovaPay',
      slug: 'novapay',
    } as Program);
    vi.mocked(accountTypeApi.listAllAccountTypes).mockResolvedValue({
      data: [],
      page: 1,
      pageSize: 100,
      total: 0,
    });
    vi.mocked(tierApi.listTiers).mockResolvedValue({ data: [], page: 1, pageSize: 100, total: 0 });
    vi.mocked(rewardApi.listRewards).mockResolvedValue({
      data: [
        reward('cb50', { status: 'pending_approval' }),
        reward('old_discount', { rewardType: 'discount' }),
      ],
      page: 1,
      pageSize: 12,
      total: 2,
    });
  });

  afterEach(() => {
    vi.clearAllMocks();
    document.body.innerHTML = '';
  });

  async function mountPage() {
    const pinia = createPinia();
    const mounted = await mountWithProviders(RewardsPage, { pinia, props: { programId: 'p1' } });
    useTenantContextStore(pinia).set('novapay');
    await flushPromises();
    return mounted;
  }

  it('shows pending approval and retired rewards as such', async () => {
    const { wrapper } = await mountPage();
    const rows = wrapper.findAll('tbody tr').map((r) => r.text());
    expect(rows[0]).toContain('Pending approval');
    expect(rows[0]).toContain('Approve');
    expect(rows[1]).toContain('retired');
    expect(rows[1]).toContain('View');
  });

  it('approves a pending reward, and shows the server refusal of a self-approval', async () => {
    vi.mocked(rewardApi.approveReward).mockRejectedValueOnce(
      apiErrorFrom(409, { title: 'You cannot approve a reward you created.', code: 'self_approval' }),
    );
    const { wrapper } = await mountPage();
    await wrapper
      .findAll('button')
      .find((b) => b.text() === 'Approve')!
      .trigger('click');
    await vi.waitFor(() => expect(rewardApi.approveReward).toHaveBeenCalledWith('novapay', 'p1', 'cb50'));
    await vi.waitFor(() =>
      expect(document.body.textContent).toContain('You cannot approve a reward you created.'),
    );
  });

  it('opens a retired reward read-only, without a save button', async () => {
    const { wrapper } = await mountPage();
    await wrapper
      .findAll('button')
      .find((b) => b.text() === 'View')!
      .trigger('click');
    await flushPromises();
    const buttons = [...document.querySelectorAll('[role="dialog"] button')].map((b) =>
      b.textContent?.trim(),
    );
    expect(buttons).toContain('Close');
    expect(buttons).not.toContain('Save');
  });
});
