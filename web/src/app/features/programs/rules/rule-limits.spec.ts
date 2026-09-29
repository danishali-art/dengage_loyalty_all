import {
  LimitValues,
  limitFormatError,
  usesOnBreach,
  usesPeriod,
  validateLimits,
} from './rule-limits';

// Same cases as RuleLimitsValidatorTests (IntegrationTests/Api) — the portal must accept exactly what the
// API accepts.
describe('rule limits', () => {
  const blank: LimitValues = {
    limitPerCustomerTotal: '',
    limitPerCustomerPerDay: '',
    limitPerCustomerPerPeriod: '',
    limitMaxPerEvent: '',
    limitMinEventAmount: '',
    limitCooldownHours: '',
    limitRuleBudgetTotal: '',
    limitRuleBudgetPerPeriod: '',
    limitMaxCustomers: '',
    limitPeriod: '',
    limitResetWindow: 'Calendar',
  };

  it.each([
    ['points', '250', null],
    ['points', '12.5', null],
    ['points', '0.0001', null],
    ['points', '1.23456', 'rules.limits.errors.format.points'],
    ['points', '-5', 'rules.limits.errors.format.points'],
    ['points', '1e3', 'rules.limits.errors.format.points'],
    ['points', '10,5', 'rules.limits.errors.format.points'],
    ['points', 'abc', 'rules.limits.errors.format.points'],
    ['points', '0', 'rules.limits.errors.positive'],
    ['amount', '49.99', null],
    ['hours', '1.5', null],
    ['hours', '1.25', null],
    ['hours', '1.255', 'rules.limits.errors.format.hours'],
    ['count', '1000', null],
    ['count', '10.5', 'rules.limits.errors.format.count'],
    ['count', '0', 'rules.limits.errors.atLeastOne'],
    ['points', '', null],
    ['points', '  ', null],
  ] as const)('%s "%s" → %s', (kind, raw, expected) => {
    expect(limitFormatError(kind, raw)).toBe(expected);
  });

  it('accepts a blank section — no limits at all', () => {
    expect(validateLimits(blank)).toEqual({});
  });

  it('rejects a per-day cap above the per-customer total, compared exactly', () => {
    expect(
      validateLimits({
        ...blank,
        limitPerCustomerTotal: '100',
        limitPerCustomerPerDay: '100.0001',
      }),
    ).toEqual({
      limitPerCustomerPerDay: 'rules.limits.errors.aboveCustomerTotal',
    });
    expect(
      validateLimits({ ...blank, limitPerCustomerTotal: '100', limitPerCustomerPerDay: '100' }),
    ).toEqual({});
  });

  it('rejects a per-period budget above the total budget', () => {
    expect(
      validateLimits({
        ...blank,
        limitRuleBudgetTotal: '500',
        limitRuleBudgetPerPeriod: '600',
        limitPeriod: 'Month',
      }),
    ).toEqual({ limitRuleBudgetPerPeriod: 'rules.limits.errors.aboveBudgetTotal' });
  });

  it('requires a period when a per-period cap is set, instead of silently using Day', () => {
    expect(validateLimits({ ...blank, limitPerCustomerPerPeriod: '50' })).toEqual({
      limitPeriod: 'rules.limits.errors.periodRequired',
    });
    expect(
      validateLimits({ ...blank, limitPerCustomerPerPeriod: '50', limitPeriod: 'Week' }),
    ).toEqual({});
  });

  it('skips cross-field checks when a value is itself malformed', () => {
    expect(
      validateLimits({ ...blank, limitPerCustomerTotal: 'x', limitPerCustomerPerDay: '5' }),
    ).toEqual({
      limitPerCustomerTotal: 'rules.limits.errors.format.points',
    });
  });

  it('shows Period only for per-period caps and On breach only for clampable caps', () => {
    expect(usesPeriod({ ...blank, limitMaxPerEvent: '10' })).toBe(false);
    expect(usesPeriod({ ...blank, limitRuleBudgetPerPeriod: '10' })).toBe(true);
    expect(usesOnBreach({ ...blank, limitCooldownHours: '2' })).toBe(false);
    expect(usesOnBreach({ ...blank, limitRuleBudgetTotal: '10' })).toBe(true);
  });
});
