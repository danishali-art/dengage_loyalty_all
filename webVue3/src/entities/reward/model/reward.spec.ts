// Model cases ported from web/src/app/features/programs/rewards/reward.model.spec.ts; form cases
// mirror the Angular reward form's rules (RewardsValidators / RewardTypeRegistry).
import { describe, expect, it } from 'vitest';
import {
  isRetired,
  rewardTypesFor,
  type CreatableAcquisition,
  type Reward,
  type RewardAcquisition,
  type RewardType,
} from './types';
import {
  rewardFormSchema,
  rewardFormValues,
  toCreateRewardRequest,
  toUpdateRewardRequest,
} from './reward.schema';

describe('reward model (CR 2026-09-30)', () => {
  it.each<[CreatableAcquisition, RewardType[]]>([
    ['points_purchase', ['cashback']],
    ['streak_completion', ['cashback', 'tier_upgrade']],
  ])('offers the right reward types for %s', (acquisition, expected) => {
    expect([...rewardTypesFor(acquisition)]).toEqual(expected);
  });

  it.each<[RewardAcquisition, RewardType, boolean]>([
    ['points_purchase', 'cashback', false],
    ['streak_completion', 'cashback', false],
    ['streak_completion', 'tier_upgrade', false],
    ['points_purchase', 'tier_upgrade', true],
    ['stamp_completion', 'cashback', true],
    ['points_purchase', 'discount', true],
    ['streak_completion', 'points_bonus', true],
    ['points_purchase', 'free_product', true],
    ['points_purchase', 'gift_card', true],
  ])('treats %s + %s as retired: %s', (acquisition, rewardType, retired) => {
    expect(isRetired({ acquisition, rewardType })).toBe(retired);
  });
});

describe('reward form', () => {
  const context = { namePrefix: 'novapay_', nameGrandfathered: false };
  const base = () => ({
    ...rewardFormValues(undefined, 'novapay_', 'SAR'),
    nameSuffix: 'cashback_10',
    displayName: 'SAR 10 cashback',
    pointsAccountTypeId: 'pts',
    cashAccountTypeId: 'cash',
  });

  it('builds a points-purchase cashback with decimal strings', () => {
    const values = rewardFormSchema(context).parse({
      ...base(),
      pointsPrice: '1000',
      cashbackAmount: '10.50',
    });
    expect(toCreateRewardRequest(values, context)).toEqual({
      name: 'novapay_cashback_10',
      displayName: 'SAR 10 cashback',
      acquisition: 'points_purchase',
      rewardType: 'cashback',
      pointsPrice: '1000',
      pointsAccountTypeId: 'pts',
      typeConfig: { amount: '10.50', currency: 'SAR', cash_account_type_id: 'cash' },
      isActive: true,
    });
  });

  it('builds a streak tier upgrade without price, with optional duration', () => {
    const values = rewardFormSchema(context).parse({
      ...base(),
      acquisition: 'streak_completion',
      rewardType: 'tier_upgrade',
      targetTierId: 'gold',
      durationDays: '30',
    });
    const request = toCreateRewardRequest(values, context);
    expect(request.pointsPrice).toBeNull();
    expect(request.pointsAccountTypeId).toBeNull();
    expect(request.typeConfig).toEqual({ target_tier_id: 'gold', duration_days: 30 });
  });

  it('never accepts a tier upgrade bought with points (RewardType.IsAllowedWith)', () => {
    expect(
      rewardFormSchema(context).safeParse({ ...base(), rewardType: 'tier_upgrade', targetTierId: 'gold' })
        .success,
    ).toBe(false);
  });

  it.each(['0', '-5', '1.23456', 'ten', ''])('rejects the cashback amount %s', (cashbackAmount) => {
    expect(rewardFormSchema(context).safeParse({ ...base(), cashbackAmount }).success).toBe(false);
  });

  it('requires the wallets the reward needs', () => {
    expect(rewardFormSchema(context).safeParse({ ...base(), cashAccountTypeId: null }).success).toBe(false);
    expect(rewardFormSchema(context).safeParse({ ...base(), pointsAccountTypeId: null }).success).toBe(false);
  });

  it('rejects a name suffix outside lowercase letters, digits and underscores', () => {
    expect(rewardFormSchema(context).safeParse({ ...base(), nameSuffix: 'Bad-Name' }).success).toBe(false);
  });

  it('sends the name on update only when it changed, never for a grandfathered name', () => {
    const existing = { name: 'novapay_cashback_10' } as Reward;
    const values = rewardFormSchema(context).parse(base());
    expect(toUpdateRewardRequest(values, existing, context)).not.toHaveProperty('name');
    const renamed = rewardFormSchema(context).parse({ ...base(), nameSuffix: 'cashback_20' });
    expect(toUpdateRewardRequest(renamed, existing, context).name).toBe('novapay_cashback_20');
    const legacy = { namePrefix: 'novapay_', nameGrandfathered: true };
    expect(
      toUpdateRewardRequest(
        rewardFormSchema(legacy).parse({ ...base(), nameSuffix: 'OLD NAME' }),
        existing,
        legacy,
      ),
    ).not.toHaveProperty('name');
  });
});
