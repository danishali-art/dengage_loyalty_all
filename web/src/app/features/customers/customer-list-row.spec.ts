import { customerListRow } from './customer-list-row';
import { CustomerProfile } from './customer.model';

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
