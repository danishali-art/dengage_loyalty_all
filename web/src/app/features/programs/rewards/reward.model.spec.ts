import {
  CreatableAcquisition,
  RewardAcquisition,
  RewardType,
  isRetired,
  rewardTypesFor,
} from './reward.model';

// Mirrors RewardType.IsAllowedWith / RewardAcquisition.IsCreatable (CR 2026-09-30, A1 + §3.1) —
// the form must never offer a combination the API rejects.
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
    ['points_purchase', 'tier_upgrade', true], // combination the matrix forbids
    ['stamp_completion', 'cashback', true], // retired acquisition
    ['points_purchase', 'discount', true], // retired type
    ['streak_completion', 'points_bonus', true],
    ['points_purchase', 'free_product', true],
    ['points_purchase', 'gift_card', true],
  ])('treats %s + %s as retired: %s', (acquisition, rewardType, retired) => {
    expect(isRetired({ acquisition, rewardType })).toBe(retired);
  });
});
