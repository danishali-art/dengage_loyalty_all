import { z } from 'zod';
import { translate } from '@/shared/i18n';
import {
  REWARD_DISPLAY_NAME_MAX,
  REWARD_NAME_MAX,
  type CashbackConfig,
  type CreatableAcquisition,
  type CreatableRewardType,
  type CreateRewardRequest,
  type Reward,
  type TierUpgradeConfig,
  type UpdateRewardRequest,
} from './types';

/**
 * The reward form (port of reward-form.dialog.ts), checked against RewardsValidators and
 * RewardTypeRegistry: a price and points wallet for a points purchase; an amount and CASH wallet
 * for a cashback; a target tier for a tier upgrade. Money and points stay decimal strings —
 * up to 16 integer + 4 fractional digits (numeric(20,4)) — never JS numbers.
 */
const POSITIVE_DECIMAL = /^\d{1,16}(\.\d{1,4})?$/;
const isPositiveDecimal = (v: string): boolean => POSITIVE_DECIMAL.test(v.trim()) && Number(v) > 0;
const WHOLE = /^\d+$/;

export interface RewardFormContext {
  /** CR 2026-09-30 (A5): reward names are `{programSlug}_{suffix}`. */
  namePrefix: string;
  /** A pre-CR name without the prefix stays as it is (§3.10 grandfathering) — not validated. */
  nameGrandfathered: boolean;
}

export function rewardFormSchema(context: RewardFormContext) {
  return z
    .object({
      nameSuffix: z.string().trim(),
      displayName: z
        .string()
        .trim()
        .min(1, translate('rewards.form.errors.displayNameRequired'))
        .max(
          REWARD_DISPLAY_NAME_MAX,
          translate('rewards.form.errors.tooLong', { max: REWARD_DISPLAY_NAME_MAX }),
        ),
      acquisition: z.enum(['points_purchase', 'streak_completion']),
      rewardType: z.enum(['cashback', 'tier_upgrade']),
      pointsPrice: z.string(),
      pointsAccountTypeId: z.string().nullable(),
      cashbackAmount: z.string(),
      cashbackCurrency: z.string(),
      cashAccountTypeId: z.string().nullable(),
      targetTierId: z.string().nullable(),
      durationDays: z.string(),
    })
    .superRefine((v, ctx) => {
      const issue = (path: string, key: string, params?: Record<string, unknown>): void =>
        ctx.addIssue({ code: 'custom', path: [path], message: translate(key, params) });

      if (!context.nameGrandfathered) {
        if (!/^[a-z0-9_]+$/.test(v.nameSuffix)) issue('nameSuffix', 'rewards.form.errors.nameSuffix');
        else if (context.namePrefix.length + v.nameSuffix.length > REWARD_NAME_MAX) {
          issue('nameSuffix', 'rewards.form.errors.tooLong', { max: REWARD_NAME_MAX });
        }
      }
      // RewardType.IsAllowedWith: a points purchase can't carry a tier upgrade.
      if (v.acquisition === 'points_purchase' && v.rewardType !== 'cashback') {
        issue('rewardType', 'rewards.form.tierNeedsStreak');
      }
      if (v.acquisition === 'points_purchase') {
        if (!isPositiveDecimal(v.pointsPrice)) issue('pointsPrice', 'rewards.form.errors.positiveDecimal');
        if (!v.pointsAccountTypeId) issue('pointsAccountTypeId', 'rewards.form.errors.required');
      }
      if (v.rewardType === 'cashback') {
        if (!isPositiveDecimal(v.cashbackAmount))
          issue('cashbackAmount', 'rewards.form.errors.positiveDecimal');
        if (!v.cashAccountTypeId) issue('cashAccountTypeId', 'rewards.form.errors.required');
      }
      if (v.rewardType === 'tier_upgrade') {
        if (!v.targetTierId) issue('targetTierId', 'rewards.form.errors.required');
        if (v.durationDays.trim() !== '' && !(WHOLE.test(v.durationDays) && Number(v.durationDays) >= 1)) {
          issue('durationDays', 'rewards.form.errors.durationDays');
        }
      }
    });
}
export type RewardFormValues = z.infer<ReturnType<typeof rewardFormSchema>>;

export function rewardFormValues(
  existing: Reward | undefined,
  namePrefix: string,
  defaultCurrency: string,
): RewardFormValues {
  const cashback = (existing?.typeConfig ?? {}) as Partial<CashbackConfig>;
  const tierUpgrade = (existing?.typeConfig ?? {}) as Partial<TierUpgradeConfig>;
  const name = existing?.name ?? '';
  return {
    nameSuffix: name.startsWith(namePrefix) ? name.slice(namePrefix.length) : name,
    displayName: existing?.displayName ?? '',
    acquisition: (existing?.acquisition as CreatableAcquisition | undefined) ?? 'points_purchase',
    rewardType: (existing?.rewardType as CreatableRewardType | undefined) ?? 'cashback',
    pointsPrice: existing?.pointsPrice ?? '100',
    pointsAccountTypeId: existing?.pointsAccountTypeId ?? null,
    cashbackAmount: cashback.amount ?? '10.00',
    cashbackCurrency: cashback.currency ?? defaultCurrency,
    cashAccountTypeId: cashback.cash_account_type_id ?? null,
    targetTierId: tierUpgrade.target_tier_id ?? null,
    durationDays: tierUpgrade.duration_days === undefined ? '' : String(tierUpgrade.duration_days),
  };
}

function typeConfig(v: RewardFormValues): CashbackConfig | TierUpgradeConfig {
  if (v.rewardType === 'cashback') {
    return {
      amount: v.cashbackAmount.trim() as CashbackConfig['amount'],
      currency: v.cashbackCurrency,
      cash_account_type_id: v.cashAccountTypeId!,
    };
  }
  const config: TierUpgradeConfig = { target_tier_id: v.targetTierId! };
  if (v.durationDays.trim() !== '') config.duration_days = Number(v.durationDays);
  return config;
}

function pricing(v: RewardFormValues): Pick<CreateRewardRequest, 'pointsPrice' | 'pointsAccountTypeId'> {
  const purchase = v.acquisition === 'points_purchase';
  return {
    pointsPrice: purchase ? v.pointsPrice.trim() : null,
    pointsAccountTypeId: purchase ? v.pointsAccountTypeId : null,
  };
}

export function toCreateRewardRequest(v: RewardFormValues, context: RewardFormContext): CreateRewardRequest {
  return {
    name: `${context.namePrefix}${v.nameSuffix}`,
    displayName: v.displayName,
    acquisition: v.acquisition,
    rewardType: v.rewardType,
    ...pricing(v),
    typeConfig: typeConfig(v),
    isActive: true,
  };
}

/** The name is only sent when it changed, and never for a grandfathered name. */
export function toUpdateRewardRequest(
  v: RewardFormValues,
  existing: Reward,
  context: RewardFormContext,
): UpdateRewardRequest {
  const name = `${context.namePrefix}${v.nameSuffix}`;
  return {
    ...(context.nameGrandfathered || name === existing.name ? {} : { name }),
    displayName: v.displayName,
    ...pricing(v),
    typeConfig: typeConfig(v),
  };
}
