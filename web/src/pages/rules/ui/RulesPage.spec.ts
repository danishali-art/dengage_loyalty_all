/** Rules page with only the entity api functions mocked: grouping, ranks, approve and toggle. */
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { flushPromises } from '@vue/test-utils';
import { createPinia } from 'pinia';
import { mountWithProviders } from '../../../../tests/utils/mount-with-providers';
import { useTenantContextStore } from '@/shared/model';
import { apiErrorFrom } from '@/shared/api';
import * as accountTypeApi from '@/entities/account-type/api/account-type.api';
import * as programApi from '@/entities/program/api/program.api';
import * as ruleApi from '@/entities/rule/api/rule.api';
import type { AccountType } from '@/entities/account-type';
import type { Program } from '@/entities/program';
import type { Rule } from '@/entities/rule';
import RulesPage from './RulesPage.vue';

vi.mock('@/entities/account-type/api/account-type.api');
vi.mock('@/entities/program/api/program.api');
vi.mock('@/entities/rule/api/rule.api');

const points = { id: 'pts', type: 'POINTS', name: 'NovaPoints', config: { decimals: 0 } } as AccountType;
const rule = (id: string, extra: Partial<Rule>): Rule =>
  ({
    id,
    name: id,
    type: 'SpendRule',
    trigger: 'order.created',
    targetAccountTypeId: 'pts',
    priority: 10,
    stackable: false,
    status: 'active',
    ...extra,
  }) as Rule;

describe('RulesPage', () => {
  beforeEach(() => {
    vi.mocked(programApi.getProgram).mockResolvedValue({ id: 'p1', name: 'NovaPay' } as Program);
    vi.mocked(accountTypeApi.listAllAccountTypes).mockResolvedValue({
      data: [points],
      page: 1,
      pageSize: 100,
      total: 1,
    });
    vi.mocked(ruleApi.listAllRules).mockResolvedValue([
      rule('base', { priority: 10 }),
      rule('gold', { priority: 20 }),
      rule('bonus', { stackable: true, type: 'FixedBonusRule' }),
      rule('cashback', { status: 'pending_approval', priority: 5 }),
      rule('redeem', { type: 'RedemptionRule', trigger: 'points.redeem' }),
    ]);
  });

  afterEach(() => {
    vi.clearAllMocks();
    document.body.innerHTML = '';
  });

  async function mountPage() {
    const pinia = createPinia();
    const mounted = await mountWithProviders(RulesPage, { pinia, props: { programId: 'p1' } });
    useTenantContextStore(pinia).set('novapay');
    await flushPromises();
    return mounted;
  }

  it('groups rules by trigger and wallet, ranks the exclusive ones, and lists burn rules apart', async () => {
    const { wrapper } = await mountPage();
    const group = wrapper.find('section[aria-labelledby="rule-group-0"]');
    expect(group.text()).toContain('order.created');
    expect(group.text()).toContain('NovaPoints (POINTS)');
    const exclusive = group
      .findAll('table')[0]!
      .findAll('tbody tr')
      .map((r) => r.text());
    expect(exclusive[0]).toContain('gold');
    expect(exclusive[0]).toContain('Wins when it matches');
    expect(exclusive[1]).toContain('Fallback');
    // Pending approval doesn't compete.
    expect(exclusive[2]).toContain('cashback');
    expect(exclusive[2]).not.toContain('Fallback');
    expect(group.findAll('table')[1]!.text()).toContain('bonus');
    expect(wrapper.find('section[aria-labelledby="rule-group-outside"]').text()).toContain('redeem');
  });

  it("toasts the server's refusal when the creator approves their own rule (CR-04)", async () => {
    vi.mocked(ruleApi.approveRule).mockRejectedValue(
      apiErrorFrom(409, {
        title: 'You cannot approve a rule you created.',
        code: 'self_approval_not_allowed',
      }),
    );
    const { wrapper } = await mountPage();
    await wrapper
      .findAll('button')
      .find((b) => b.text() === 'Approve')!
      .trigger('click');
    await vi.waitFor(() => expect(ruleApi.approveRule).toHaveBeenCalledWith('novapay', 'p1', 'cashback'));
    await vi.waitFor(() =>
      expect(document.body.textContent).toContain('You cannot approve a rule you created.'),
    );
  });

  it('disables an active rule', async () => {
    vi.mocked(ruleApi.setRuleStatus).mockResolvedValue(rule('gold', { status: 'disabled' }));
    const { wrapper } = await mountPage();
    await wrapper.findAll('button[aria-label="Disable rule"]')[0]!.trigger('click');
    await vi.waitFor(() =>
      expect(ruleApi.setRuleStatus).toHaveBeenCalledWith('novapay', 'p1', 'gold', 'disabled'),
    );
  });
});
