import { DecimalString } from '../../../shared/money/decimal-string';

export type RewardAcquisition = 'points_purchase' | 'stamp_completion' | 'streak_completion';

export type RewardType = 'points_bonus' | 'discount' | 'cashback' | 'free_product' | 'gift_card' | 'tier_upgrade';

/** Backend-validated shape per RewardType (RewardTypeRegistry.cs) — extra fields are accepted but ignored. */
export interface PointsBonusConfig {
  amount: DecimalString;
  account_type_id: string;
}

export interface DiscountConfig {
  discount_kind: 'percentage' | 'fixed';
  value: DecimalString;
  min_purchase_amount?: DecimalString;
  max_discount_amount?: DecimalString;
}

export interface CashbackConfig {
  amount: DecimalString;
  currency: string;
}

export interface FreeProductConfig {
  product_sku: string;
  quantity: number;
}

export interface GiftCardConfig {
  value: DecimalString;
  currency: string;
}

export interface TierUpgradeConfig {
  target_tier_id: string;
  duration_days?: number;
}

export type RewardTypeConfig =
  | PointsBonusConfig
  | DiscountConfig
  | CashbackConfig
  | FreeProductConfig
  | GiftCardConfig
  | TierUpgradeConfig
  | Record<string, unknown>;

export interface Reward {
  id: string;
  name: string;
  displayName: string;
  acquisition: RewardAcquisition;
  rewardType: RewardType;
  stampAccountTypeId: string | null;
  pointsPrice: string | null;
  pointsAccountTypeId: string | null;
  typeConfig: RewardTypeConfig;
  isActive: boolean;
  createdAt: string;
}

export interface CreateRewardRequest {
  name: string;
  displayName: string;
  acquisition: RewardAcquisition;
  rewardType: RewardType;
  stampAccountTypeId?: string | null;
  pointsPrice?: string | null;
  pointsAccountTypeId?: string | null;
  typeConfig: RewardTypeConfig;
  isActive: boolean;
}

export interface UpdateRewardRequest {
  name?: string;
  displayName?: string;
  stampAccountTypeId?: string | null;
  pointsPrice?: string | null;
  pointsAccountTypeId?: string | null;
  typeConfig?: RewardTypeConfig;
}
