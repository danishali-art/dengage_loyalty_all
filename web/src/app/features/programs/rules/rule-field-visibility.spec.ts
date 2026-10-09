import { fieldVisibility, isConfigVisible, isLimitVisible } from './rule-field-visibility';
import { EventMetadata } from './rule.model';

const kyc: EventMetadata = {
  eventType: 'kyc.completed',
  category: 'Earn',
  source: 'Behavioural',
  cardinality: 'OncePerCustomer',
  period: null,
  fields: [],
  compatibleRuleTypes: ['FixedBonusRule'],
  applicableFields: [
    {
      ruleType: 'FixedBonusRule',
      configuration: ['rounding', 'expiryOverrideDays', 'notifyOnAward'],
      limits: ['max_customers', 'rule_budget_total'],
    },
  ],
};

describe('fieldVisibility (CR 2026-10-06 Phase 2)', () => {
  it('shows only the fields the API lists for a new rule of that type', () => {
    const visibility = fieldVisibility(kyc, 'FixedBonusRule', false);

    expect(isLimitVisible(visibility, 'min_event_amount')).toBe(false);
    expect(isLimitVisible(visibility, 'per_customer_total')).toBe(false);
    expect(isLimitVisible(visibility, 'max_customers')).toBe(true);
    expect(isConfigVisible(visibility, 'posting')).toBe(false);
    expect(isConfigVisible(visibility, 'notifyOnAward')).toBe(true);
  });

  it('shows every field when editing an existing rule (D5)', () => {
    const visibility = fieldVisibility(kyc, 'FixedBonusRule', true);

    expect(visibility).toBeNull();
    expect(isLimitVisible(visibility, 'min_event_amount')).toBe(true);
  });

  it('shows every field for a tenant-defined trigger the catalog does not know (D7)', () => {
    expect(fieldVisibility(null, 'SpendRule', false)).toBeNull();
  });

  it('shows every field when the API sent no list for the type', () => {
    expect(
      fieldVisibility({ ...kyc, applicableFields: undefined }, 'FixedBonusRule', false),
    ).toBeNull();
  });
});
