import { DecimalString } from '../../../shared/money/decimal-string';

/**
 * Mirrors RewardAcquisition.cs. All stored values stay in the union because the API still returns
 * retired rows; only CREATABLE_ACQUISITIONS can be chosen for a new reward (CR 2026-09-30, A1).
 */
export type RewardAcquisition = 'points_purchase' | 'stamp_completion' | 'streak_completion';

/** Mirrors RewardType.cs — the same rule as RewardAcquisition: retired values are read-only. */
export type RewardType =
  'points_bonus' | 'discount' | 'cashback' | 'free_product' | 'gift_card' | 'tier_upgrade';

export type CreatableAcquisition = 'points_purchase' | 'streak_completion';
export type CreatableRewardType = 'cashback' | 'tier_upgrade';

export const CREATABLE_ACQUISITIONS: readonly CreatableAcquisition[] = [
  'points_purchase',
  'streak_completion',
];
export const CREATABLE_REWARD_TYPES: readonly CreatableRewardType[] = ['cashback', 'tier_upgrade'];

/** Mirrors RewardType.IsAllowedWith: cashback with either acquisition, tier_upgrade only through a streak. */
export function rewardTypesFor(acquisition: CreatableAcquisition): readonly CreatableRewardType[] {
  return acquisition === 'streak_completion' ? CREATABLE_REWARD_TYPES : ['cashback'];
}

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

/** Mirrors RewardStatus.cs (A4): a cashback reward needs a second admin's approval. */
export type RewardStatus = 'active' | 'pending_approval';

/** Backend-validated shape per RewardType (RewardTypeRegistry.cs) — extra fields are accepted but ignored. */
export interface CashbackConfig {
  amount: DecimalString;
  /** One of SUPPORTED_CURRENCIES, and equal to the selected wallet's currency. */
  currency: string;
  /** The CASH account type the engine credits. */
  cash_account_type_id: string;
}

export interface TierUpgradeConfig {
  target_tier_id: string;
  /** Omitted = "until next tier review" (normal lifecycle); otherwise days the tier is locked. */
  duration_days?: number;
}

export type RewardTypeConfig = CashbackConfig | TierUpgradeConfig | Record<string, unknown>;

export interface Reward {
  id: string;
  name: string;
  displayName: string;
  acquisition: RewardAcquisition;
  rewardType: RewardType;
  pointsPrice: string | null;
  pointsAccountTypeId: string | null;
  typeConfig: RewardTypeConfig;
  isActive: boolean;
  createdAt: string;
  status: RewardStatus;
  createdBy: string | null;
  approvedBy: string | null;
}

export interface CreateRewardRequest {
  name: string;
  displayName: string;
  acquisition: CreatableAcquisition;
  rewardType: CreatableRewardType;
  pointsPrice?: string | null;
  pointsAccountTypeId?: string | null;
  typeConfig: RewardTypeConfig;
  isActive: boolean;
}

export interface UpdateRewardRequest {
  name?: string;
  displayName?: string;
  pointsPrice?: string | null;
  pointsAccountTypeId?: string | null;
  typeConfig?: RewardTypeConfig;
}
