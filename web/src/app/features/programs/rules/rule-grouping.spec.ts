import { groupRulesByApplication, rankExclusiveRules } from './rule-grouping';
import { Rule, RuleType } from './rule.model';

// Mirrors WinnerSelector.cs (1.3.CL): per trigger + target account, exclusive rules compete on
// priority and stackables add on top; Transfer/Reversal skip winner selection entirely.
describe('groupRulesByApplication', () => {
  let seq = 0;
  const rule = (overrides: Partial<Rule> & { type?: RuleType }): Rule =>
    ({
      id: `r${++seq}`,
      name: `rule ${seq}`,
      type: 'SpendRule',
      trigger: 'order.created',
      targetAccountTypeId: 'points',
      calculation: null,
      conditions: null,
      limits: null,
      priority: 10,
      stackable: false,
      exclusivityGroup: null,
      stackMode: 'Additive',
      configuration: null,
      version: 1,
      activeFrom: null,
      activeTo: null,
      status: 'active',
      ...overrides,
    }) as Rule;

  it('groups by trigger and target account, splitting exclusive from stackable', () => {
    const base = rule({ name: 'base', priority: 10 });
    const gold = rule({ name: 'gold', priority: 20 });
    const bonus = rule({ name: 'bonus', stackable: true, type: 'FixedBonusRule' });
    const cash = rule({ name: 'cashback', targetAccountTypeId: 'cash' });
    const remit = rule({ name: 'remit', trigger: 'remittance' });

    const { groups } = groupRulesByApplication([base, gold, bonus, cash, remit]);

    expect(groups.map((g) => g.key)).toEqual([
      'order.created|cash',
      'order.created|points',
      'remittance|points',
    ]);
    const points = groups.find((g) => g.key === 'order.created|points')!;
    expect(points.exclusive.map((r) => r.name)).toEqual(['gold', 'base']); // highest priority first
    expect(points.stackable.map((r) => r.name)).toEqual(['bonus']);
  });

  it('puts different rule types on the same event and wallet in one group — they compete', () => {
    const spend = rule({ type: 'SpendRule', priority: 5 });
    const fixed = rule({ type: 'FixedBonusRule', priority: 9 });
    const { groups } = groupRulesByApplication([spend, fixed]);
    expect(groups).toHaveLength(1);
    expect(groups[0]!.exclusive.map((r) => r.type)).toEqual(['FixedBonusRule', 'SpendRule']);
  });

  it('lists Redemption, Transfer and Reversal rules outside winner selection', () => {
    const redemption = rule({ type: 'RedemptionRule', trigger: 'points.redeem' });
    const transfer = rule({ type: 'TransferRule' });
    const reversal = rule({
      type: 'ReversalRule',
      targetAccountTypeId: null,
      trigger: 'order.refunded',
    });
    const { groups, outsideWinnerSelection } = groupRulesByApplication([
      redemption,
      transfer,
      reversal,
    ]);
    expect(groups).toHaveLength(0);
    expect(outsideWinnerSelection.map((r) => r.type)).toEqual(
      expect.arrayContaining(['RedemptionRule', 'TransferRule', 'ReversalRule']),
    );
  });

  it('keeps inactive rules in their group so the page can show them dimmed', () => {
    const disabled = rule({ status: 'disabled' });
    const pending = rule({ status: 'pending_approval', stackable: true });
    const { groups } = groupRulesByApplication([disabled, pending]);
    expect(groups[0]!.exclusive).toContain(disabled);
    expect(groups[0]!.stackable).toContain(pending);
  });

  it('orders groups on the same trigger by account name', () => {
    const { groups } = groupRulesByApplication(
      [rule({ targetAccountTypeId: 'b-id' }), rule({ targetAccountTypeId: 'a-id' })],
      (id) => (id === 'a-id' ? 'Zeta' : 'Alpha'),
    );
    expect(groups.map((g) => g.targetAccountTypeId)).toEqual(['b-id', 'a-id']);
  });
});

describe('rankExclusiveRules', () => {
  const r = (id: string, priority: number, status: Rule['status'] = 'active'): Rule =>
    ({ id, name: id, priority, status }) as Rule;

  it('marks the highest-priority active rule as the winner and the rest as fallbacks', () => {
    const ranks = rankExclusiveRules([r('gold', 20), r('base', 10), r('low', 1)]);
    expect([...ranks]).toEqual([
      ['gold', 'top'],
      ['base', 'fallback'],
      ['low', 'fallback'],
    ]);
  });

  it('skips inactive rules — a disabled higher-priority rule does not win', () => {
    const ranks = rankExclusiveRules([
      r('off', 99, 'disabled'),
      r('pending', 50, 'pending_approval'),
      r('base', 10),
    ]);
    expect(ranks.get('off')).toBe('inactive');
    expect(ranks.get('pending')).toBe('inactive');
    expect(ranks.get('base')).toBe('top');
  });

  it('flags equal top priorities as a tie', () => {
    const ranks = rankExclusiveRules([r('a', 10), r('b', 10), r('c', 5)]);
    expect(ranks.get('a')).toBe('tie');
    expect(ranks.get('b')).toBe('tie');
    expect(ranks.get('c')).toBe('fallback');
  });
});
