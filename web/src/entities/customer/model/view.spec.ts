import { describe, expect, it } from 'vitest';
import type { DecimalString } from '@/shared/lib/money';
import type { CustomerProfile, RuleCapUsage } from './types';
import { capTile, customerListRow } from './view';

// Customers list, Layout A (UI-only): the per-row facts built from the profile.
describe('customerListRow', () => {
  const program = (programId: string, programName: string) => ({
    programId,
    programName,
    wallets: [],
    tier: null,
    streaks: [],
  });
  const profile: CustomerProfile = {
    contactKey: 'burn_a',
    balances: [],
    tierProgress: null,
    summary: {
      firstSeenAt: '2026-10-01T10:00:00Z',
      lastActivityAt: '2026-10-03T10:00:00Z',
      failedEventsLast7Days: 0,
      activeStreakCount: 0,
    },
    programs: [program('p1', 'FinPay Rewards'), program('p3', 'third one')],
  };

  it('lists the programs the customer is in, and when they were first seen', () => {
    expect(customerListRow(profile)).toEqual({
      programs: ['FinPay Rewards', 'third one'],
      firstSeenAt: '2026-10-01T10:00:00Z',
    });
  });

  it('copes with a profile without programs or summary', () => {
    expect(customerListRow({ contactKey: 'x', balances: [], tierProgress: null })).toEqual({
      programs: [],
      firstSeenAt: null,
    });
  });
});

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
