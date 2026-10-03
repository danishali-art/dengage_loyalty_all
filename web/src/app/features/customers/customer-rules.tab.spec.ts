import { capTile } from './customer-rules.tab';
import { RuleCapUsage } from './customer.model';
import { DecimalString } from '../../shared/money/decimal-string';

// Rules & caps, layout Option B (2026-10-03): one tile per capped rule, its ring showing the
// tightest cap; a tile at 100% is "limit reached".
describe('capTile', () => {
  const d = (v: string) => v as DecimalString;
  const cap = (over: Partial<RuleCapUsage>): RuleCapUsage => ({
    ruleId: 'r1',
    ruleName: 'Rule',
    programId: 'p1',
    programName: 'FinPay Rewards',
    status: 'active',
    isCardBucket: false,
    usedTotal: d('0'),
    usedToday: d('0'),
    ...over,
  });

  it('marks a reached total cap', () => {
    const tile = capTile(cap({ perCustomerTotal: d('1000'), usedTotal: d('1000') }));
    expect(tile.percent).toBe(100);
    expect(tile.reached).toBe(true);
  });

  it('uses the tightest of several caps', () => {
    const tile = capTile(
      cap({
        perCustomerPerDay: d('100'),
        usedToday: d('80'),
        perCustomerTotal: d('1000'),
        usedTotal: d('200'),
      }),
    );
    expect(tile.percent).toBe(80);
    expect(tile.reached).toBe(false);
  });

  it('lists all three windows, marking the ones without a cap', () => {
    const tile = capTile(cap({ perCustomerPerDay: d('10000') }));
    expect(tile.windows.map((w) => [w.key, !!w.cap])).toEqual([
      ['total', false],
      ['today', true],
      ['period', false],
    ]);
    expect(tile.percent).toBe(0);
  });
});
