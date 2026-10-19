import { describe, expect, it } from 'vitest';
import { moveTier, sortTiers, tierDeleteBlockedReason, type Tier } from './types';
import { tierFormSchema, tierFormValues, toCreateTierRequest, toUpdateTierRequest } from './tier.schema';

const tier = (id: string, sortOrder: number, extra: Partial<Tier> = {}) =>
  ({ id, sortOrder, ...extra }) as Tier;

describe('tier model', () => {
  it('sorts by sortOrder and swaps neighbours for reordering', () => {
    const sorted = sortTiers([tier('b', 1), tier('a', 0), tier('c', 2)]);
    expect(sorted.map((t) => t.id)).toEqual(['a', 'b', 'c']);
    expect(moveTier(sorted, 1, -1)).toEqual(['b', 'a', 'c']);
    expect(moveTier(sorted, 2, 1)).toBeNull();
    expect(moveTier(sorted, 0, -1)).toBeNull();
  });

  it('blocks deleting a tier with customers, or while the program is active', () => {
    expect(tierDeleteBlockedReason(tier('a', 0, { hasAssignedAccounts: true }), false)).toBe(
      'tiers.delete.hasAccounts',
    );
    expect(tierDeleteBlockedReason(tier('a', 0, { hasAssignedAccounts: false }), true)).toBe(
      'tiers.delete.programActive',
    );
    expect(tierDeleteBlockedReason(tier('a', 0, { hasAssignedAccounts: false }), false)).toBeNull();
  });
});

describe('tier form', () => {
  it('sends a periodic tier with its period, and a lifetime tier without one', () => {
    const periodic = tierFormSchema().parse({
      ...tierFormValues(),
      name: 'gold',
      displayName: 'Gold',
      minPoints: '20000.5',
      qualifyingModel: 'periodic',
    });
    expect(toCreateTierRequest(periodic, 3)).toEqual({
      name: 'gold',
      displayName: 'Gold',
      minPoints: '20000.5',
      qualifyingModel: 'periodic',
      qualifyingPeriodDays: 365,
      graceDays: 0,
      sortOrder: 3,
    });
    const lifetime = tierFormSchema().parse({ ...tierFormValues(), name: 'b', displayName: 'B' });
    expect(toUpdateTierRequest(lifetime).qualifyingPeriodDays).toBeNull();
  });

  it('keeps minimum points a decimal string and rejects malformed or negative values', () => {
    for (const minPoints of ['-1', '1e5', '1.23456', 'abc', '']) {
      expect(
        tierFormSchema().safeParse({ ...tierFormValues(), name: 'n', displayName: 'N', minPoints }).success,
      ).toBe(false);
    }
  });

  it('requires a positive qualifying period for a periodic tier', () => {
    const values = {
      ...tierFormValues(),
      name: 'n',
      displayName: 'N',
      qualifyingModel: 'periodic' as const,
      qualifyingPeriodDays: '0',
    };
    expect(tierFormSchema().safeParse(values).success).toBe(false);
  });
});
