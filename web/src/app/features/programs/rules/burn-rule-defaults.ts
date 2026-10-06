import { AccountType, PointsConfig } from '../account-types/account-type.model';
import { RuleType } from './rule.model';

/** The rule-form fields a POINTS wallet's own settings can pre-fill. */
export interface BurnRuleDefaults {
  calcCashPerPoint?: number;
  calcMinRedeem?: number;
  calcCashAccountTypeId?: string;
  calcMaxPerDay?: number;
}

/**
 * CR 2026-10-05 (S4): the POINTS wallet's `redemption` / `transfer` settings are only a value
 * provider for a NEW redeem or transfer rule — copied once into the rule's calculation, which
 * then owns its values (events never read the wallet's). Empty when the wallet has none.
 */
export function burnRuleDefaults(
  type: RuleType,
  wallet: AccountType | undefined,
): BurnRuleDefaults {
  if (wallet?.type !== 'POINTS') return {};
  const config = wallet.config as Partial<PointsConfig>;
  if (type === 'RedemptionRule' && config.redemption) {
    const r = config.redemption;
    return {
      ...(r.rate != null ? { calcCashPerPoint: Number(r.rate) } : {}),
      ...(r.min_points != null ? { calcMinRedeem: Number(r.min_points) } : {}),
      ...(r.target_account_type_id ? { calcCashAccountTypeId: r.target_account_type_id } : {}),
    };
  }
  if (type === 'TransferRule' && config.transfer?.daily_limit != null) {
    return { calcMaxPerDay: Number(config.transfer.daily_limit) };
  }
  return {};
}
