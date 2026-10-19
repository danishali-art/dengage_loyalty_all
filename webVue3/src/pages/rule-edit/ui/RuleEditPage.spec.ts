/** Rule form page with only the entity api functions mocked: requests, limits checks, metadata states. */
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { flushPromises } from '@vue/test-utils';
import { createPinia } from 'pinia';
import { mountWithProviders } from '../../../../tests/utils/mount-with-providers';
import { useTenantContextStore } from '@/shared/model';
import * as accountTypeApi from '@/entities/account-type/api/account-type.api';
import * as programApi from '@/entities/program/api/program.api';
import * as eventTypeApi from '@/entities/event-type/api/event-type.api';
import * as ruleApi from '@/entities/rule/api/rule.api';
import type { AccountType } from '@/entities/account-type';
import type { Program } from '@/entities/program';
import type { Rule, RulesMetadata } from '@/entities/rule';
import RuleEditPage from './RuleEditPage.vue';

vi.mock('@/entities/account-type/api/account-type.api');
vi.mock('@/entities/program/api/program.api');
vi.mock('@/entities/event-type/api/event-type.api');
vi.mock('@/entities/rule/api/rule.api');

const points = { id: 'pts', type: 'POINTS', name: 'NovaPoints', config: { decimals: 0 } } as AccountType;
const metadata = {
  events: [
    {
      eventType: 'order.created',
      category: 'Earn',
      source: 'Behavioural',
      cardinality: 'Unlimited',
      fields: [{ path: 'amount', kind: 'Money' }],
      compatibleRuleTypes: ['SpendRule', 'FixedBonusRule'],
    },
    {
      eventType: 'reward.purchase',
      category: 'Burn',
      source: 'Behavioural',
      cardinality: 'Unlimited',
      fields: [],
      compatibleRuleTypes: [],
    },
  ],
  ruleTypes: [],
} as unknown as RulesMetadata;

function fill(id: string, value: string): void {
  const input = document.querySelector<HTMLInputElement>(id)!;
  input.value = value;
  input.dispatchEvent(new Event('input'));
}
const saveButton = (): HTMLButtonElement =>
  [...document.querySelectorAll('button')].find((b) => b.textContent?.trim() === 'Save')!;

describe('RuleEditPage', () => {
  beforeEach(() => {
    vi.mocked(programApi.getProgram).mockResolvedValue({
      id: 'p1',
      name: 'NovaPay',
      defaultRounding: 'down',
    } as Program);
    vi.mocked(accountTypeApi.listAllAccountTypes).mockResolvedValue({
      data: [points],
      page: 1,
      pageSize: 100,
      total: 1,
    });
    vi.mocked(eventTypeApi.getEventTypes).mockResolvedValue({
      builtIn: ['order.created', 'reward.purchase'],
      generic: [],
      publishable: ['order.created', 'reward.purchase'],
    });
    vi.mocked(ruleApi.getRulesMetadata).mockResolvedValue(metadata);
    vi.mocked(ruleApi.createRule).mockResolvedValue({ id: 'r1' } as Rule);
    vi.mocked(ruleApi.updateRule).mockResolvedValue({ id: 'r9' } as Rule);
  });

  afterEach(() => {
    vi.clearAllMocks();
    document.body.innerHTML = '';
  });

  async function mountPage(ruleId?: string) {
    const pinia = createPinia();
    const mounted = await mountWithProviders(RuleEditPage, {
      pinia,
      props: { programId: 'p1', ...(ruleId ? { ruleId } : {}) },
    });
    useTenantContextStore(pinia).set('novapay');
    await flushPromises();
    return mounted;
  }

  it('creates a spend rule: rate as a decimal string, blank conditions as none, defaults sent', async () => {
    await mountPage();
    fill('#name', 'Base earn');
    fill('#calcRate', '0.5');
    await flushPromises();
    saveButton().click();
    await vi.waitFor(() =>
      expect(ruleApi.createRule).toHaveBeenCalledWith('novapay', 'p1', {
        name: 'Base earn',
        trigger: 'order.created',
        targetAccountTypeId: 'pts',
        type: 'SpendRule',
        calculation: { rate: '0.5' },
        conditions: null,
        limits: null,
        priority: 0,
        stackable: true,
        configuration: { posting: 'Immediate', reversible: true, testMode: false, notifyOnAward: false },
        activeFrom: null,
        activeTo: null,
      }),
    );
  });

  it('blocks a per-day cap above the per-customer total (RuleLimitsValidator)', async () => {
    await mountPage();
    fill('#name', 'Capped');
    fill('#limitPerCustomerTotal', '100');
    fill('#limitPerCustomerPerDay', '100.0001');
    await flushPromises();
    saveButton().click();
    await vi.waitFor(() => expect(document.body.textContent).toContain('Please fix the highlighted fields.'));
    expect(ruleApi.createRule).not.toHaveBeenCalled();
  });

  it('saves an existing rule with its limits in snake_case and its version-bearing id', async () => {
    vi.mocked(ruleApi.getRule).mockResolvedValue({
      id: 'r9',
      name: 'Gold earn',
      type: 'SpendRule',
      trigger: 'order.created',
      targetAccountTypeId: 'pts',
      calculation: { rate: '2' },
      // Regression: loaded conditions come from the query cache as a reactive proxy, which
      // structuredClone refused — the form never filled in.
      conditions: {
        op: 'AND',
        groups: [
          {
            op: 'AND',
            conditions: [{ field: 'amount', operator: 'gte', value: { type: 'money', data: '100' } }],
          },
        ],
      },
      limits: { per_customer_per_period: '500', period: 'Month', reset_window: 'Rolling', on_breach: 'Skip' },
      priority: 20,
      stackable: false,
      configuration: { posting: 'Immediate', reversible: true, testMode: false, notifyOnAward: true },
      status: 'active',
    } as Rule);
    await mountPage('r9');
    saveButton().click();
    await vi.waitFor(() => expect(ruleApi.updateRule).toHaveBeenCalled());
    const body = vi.mocked(ruleApi.updateRule).mock.calls[0]![3];
    expect(body.limits).toEqual({
      on_breach: 'Skip',
      per_customer_per_period: '500',
      period: 'Month',
      reset_window: 'Rolling',
    });
    expect(body.calculation).toEqual({ rate: '2' });
    expect(body.conditions?.groups[0]?.conditions[0]).toEqual({
      field: 'amount',
      operator: 'gte',
      value: { type: 'money', data: '100' },
    });
    expect(body.configuration?.notifyOnAward).toBe(true);
  });

  it('shows the "no rule type" state, and blocks saving, for a trigger no rule type can use', async () => {
    vi.mocked(ruleApi.getRule).mockResolvedValue({
      id: 'r8',
      name: 'Old purchase rule',
      type: 'FixedBonusRule',
      trigger: 'reward.purchase',
      targetAccountTypeId: 'pts',
      calculation: { amount: '5' },
      priority: 0,
      stackable: true,
      status: 'disabled',
    } as Rule);
    await mountPage('r8');
    expect(document.body.textContent).toContain('No rule type can be used with this trigger');
    expect(saveButton().disabled).toBe(true);
  });
});
