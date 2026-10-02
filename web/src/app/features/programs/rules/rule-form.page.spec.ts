import { buildTriggerOptions } from './rule-form.page';
import { EventMetadata, RulesMetadata, RuleType } from './rule.model';

// CR 2026-09-30 item 8: a trigger no rule type can use (reward.purchase — configured through its
// reward definition) is not offered, except as an existing rule's own trigger. Before the fix it
// was offered, and the form fell back to a hidden SpendRule that showed POINTS/CASH targets.
describe('buildTriggerOptions', () => {
  const event = (
    eventType: string,
    category: EventMetadata['category'],
    compatibleRuleTypes: RuleType[],
  ): EventMetadata => ({
    eventType,
    category,
    source: 'Behavioural',
    cardinality: 'Unlimited',
    period: null,
    fields: [],
    compatibleRuleTypes,
  });

  const metadata: RulesMetadata = {
    events: [
      event('order.created', 'Earn', ['SpendRule', 'FixedBonusRule']),
      event('points.transfer', 'Burn', ['TransferRule']),
      event('reward.purchase', 'Burn', []),
    ],
    ruleTypes: [],
  };
  const catalog = {
    builtIn: ['order.created', 'points.transfer', 'reward.purchase'],
    generic: ['custom.event'],
    publishable: ['order.created', 'points.transfer', 'reward.purchase', 'custom.event'],
  };

  const values = (keep: string | null): string[] =>
    buildTriggerOptions(catalog, metadata, keep).map((o) => o.value);

  it('leaves out a trigger that no rule type can use', () => {
    expect(values(null)).not.toContain('reward.purchase');
  });

  it("keeps an existing rule's own trigger even when no rule type fits it any more", () => {
    expect(values('reward.purchase')).toContain('reward.purchase');
  });

  it('keeps triggers that have at least one rule type, and unknown generic types', () => {
    expect(values(null)).toEqual(
      expect.arrayContaining(['order.created', 'points.transfer', 'custom.event']),
    );
  });
});
