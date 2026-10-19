/** New streak campaign page with only the entity api functions mocked. */
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { flushPromises } from '@vue/test-utils';
import { createPinia } from 'pinia';
import { mountWithProviders } from '../../../../tests/utils/mount-with-providers';
import { useTenantContextStore } from '@/shared/model';
import * as accountTypeApi from '@/entities/account-type/api/account-type.api';
import * as programApi from '@/entities/program/api/program.api';
import * as rewardApi from '@/entities/reward/api/reward.api';
import * as eventTypeApi from '@/entities/event-type/api/event-type.api';
import * as streakApi from '@/entities/streak-campaign/api/streak-campaign.api';
import type { AccountType } from '@/entities/account-type';
import type { Program } from '@/entities/program';
import type { StreakCampaign } from '@/entities/streak-campaign';
import StreakCampaignEditPage from './StreakCampaignEditPage.vue';

vi.mock('@/entities/account-type/api/account-type.api');
vi.mock('@/entities/program/api/program.api');
vi.mock('@/entities/reward/api/reward.api');
vi.mock('@/entities/event-type/api/event-type.api');
vi.mock('@/entities/streak-campaign/api/streak-campaign.api');

const points = { id: 'pts', type: 'POINTS', name: 'NovaPoints', config: { decimals: 0 } } as AccountType;

function fill(id: string, value: string): void {
  const input = document.querySelector<HTMLInputElement>(id)!;
  input.value = value;
  input.dispatchEvent(new Event('input'));
}
const save = (): void =>
  [...document.querySelectorAll('button')].find((b) => b.textContent?.trim() === 'Save')!.click();

describe('StreakCampaignEditPage (new)', () => {
  beforeEach(() => {
    vi.mocked(programApi.getProgram).mockResolvedValue({ id: 'p1', name: 'NovaPay' } as Program);
    vi.mocked(accountTypeApi.listAllAccountTypes).mockResolvedValue({
      data: [points],
      page: 1,
      pageSize: 100,
      total: 1,
    });
    vi.mocked(rewardApi.listAllRewards).mockResolvedValue([]);
    vi.mocked(eventTypeApi.getEventTypes).mockResolvedValue({
      builtIn: ['order.created'],
      generic: [],
      publishable: ['order.created'],
    });
    vi.mocked(streakApi.createStreakCampaign).mockResolvedValue({ id: 's1' } as StreakCampaign);
  });

  afterEach(() => {
    vi.clearAllMocks();
    document.body.innerHTML = '';
  });

  async function mountPage() {
    const pinia = createPinia();
    const mounted = await mountWithProviders(StreakCampaignEditPage, {
      pinia,
      props: { programId: 'p1' },
    });
    useTenantContextStore(pinia).set('novapay');
    await flushPromises();
    return mounted;
  }

  it('creates a campaign with the engine DSL config in snake_case and amounts as strings', async () => {
    await mountPage();
    fill('#name', 'Weekly coffee');
    await flushPromises();
    save();
    await vi.waitFor(() =>
      expect(streakApi.createStreakCampaign).toHaveBeenCalledWith('novapay', 'p1', {
        name: 'Weekly coffee',
        trigger: 'order.created',
        targetAccountTypeId: 'pts',
        conditions: null,
        config: {
          period: 'week',
          week_start: 'monday',
          target_periods: 4,
          aggregate: { metric: 'count', threshold: '1' },
          timezone: 'UTC',
          on_complete: 'restart',
          reward: { kind: 'fixed_bonus', amount: '10' },
        },
        activeFrom: null,
        activeTo: null,
      }),
    );
  });

  it("shows the engine's own message for a config it would reject (StreakConfig.Validate)", async () => {
    await mountPage();
    fill('#name', 'Broken');
    fill('#threshold', '0');
    fill('#targetPeriods', '60');
    await flushPromises();
    save();
    await vi.waitFor(() => expect(document.body.textContent).toContain('aggregate.threshold must be > 0'));
    expect(document.body.textContent).toContain('target_periods 60 exceeds the 12-month retention limit');
    expect(streakApi.createStreakCampaign).not.toHaveBeenCalled();
  });
});
