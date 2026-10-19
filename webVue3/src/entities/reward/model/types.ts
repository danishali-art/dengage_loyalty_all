import type { ApiSchema } from '@/shared/api';
import type { DecimalString } from '@/shared/lib/money';

export type Reward = ApiSchema<'RewardResponse'>;
export type RewardAcquisition = Reward['acquisition'];
export type RewardType = Reward['rewardType'];
export type CreateRewardRequest = ApiSchema<'CreateRewardRequest'>;
export type UpdateRewardRequest = ApiSchema<'UpdateRewardRequest'>;
/** Only these can be chosen for a new reward (CR 2026-09-30, A1); the rest are retired, read-only. */
export type CreatableAcquisition = CreateRewardRequest['acquisition'];
export type CreatableRewardType = CreateRewardRequest['rewardType'];

export const CREATABLE_ACQUISITIONS: readonly CreatableAcquisition[] = [
  'points_purchase',
  'streak_completion',
];
export const CREATABLE_REWARD_TYPES: readonly CreatableRewardType[] = ['cashback', 'tier_upgrade'];

/** Mirrors RewardType.IsAllowedWith: cashback with either acquisition, tier_upgrade only through a streak. */
export function rewardTypesFor(acquisition: CreatableAcquisition): readonly CreatableRewardType[] {
  return acquisition === 'streak_completion' ? CREATABLE_REWARD_TYPES : ['cashback'];
}

/** A1: a retired acquisition, type, or combination is view-only (the API returns reward_type_retired). */
export function isRetired(reward: Pick<Reward, 'acquisition' | 'rewardType'>): boolean {
  const acquisitionOk = (CREATABLE_ACQUISITIONS as readonly string[]).includes(reward.acquisition);
  const typeOk = (CREATABLE_REWARD_TYPES as readonly string[]).includes(reward.rewardType);
  return (
    !acquisitionOk ||
    !typeOk ||
    !rewardTypesFor(reward.acquisition as CreatableAcquisition).includes(
      reward.rewardType as CreatableRewardType,
    )
  );
}

/** `typeConfig` per reward type — validated by RewardTypeRegistry.cs; extra keys are ignored there. */
export interface CashbackConfig {
  amount: DecimalString;
  /** One of the supported currencies, equal to the chosen CASH wallet's currency. */
  currency: string;
  /** The CASH account type the engine credits. */
  cash_account_type_id: string;
}

export interface TierUpgradeConfig {
  target_tier_id: string;
  /** Omitted = "until next tier review"; otherwise days the tier is locked. */
  duration_days?: number;
}

/** RewardsValidators: name ≤ 100, display name ≤ 255. */
export const REWARD_NAME_MAX = 100;
export const REWARD_DISPLAY_NAME_MAX = 255;
