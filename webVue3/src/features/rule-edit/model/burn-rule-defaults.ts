// Ported from web/src/app/features/programs/rules/burn-rule-defaults.ts. The one change: the form
// fields are decimal strings here (the Angular form held numbers), so the values are strings.
import { pointsConfig, type AccountType } from '@/entities/account-type';
import type { RuleType } from '@/entities/rule';

/** The rule-form fields a POINTS wallet's own settings can pre-fill. */
export interface BurnRuleDefaults {
  calcCashPerPoint?: string;
  calcMinRedeem?: string;
  calcCashAccountTypeId?: string;
  calcMaxPerDay?: string;
}

/**
 * CR 2026-10-05 (S4): the POINTS wallet's `redemption` / `transfer` settings are only a value
 * provider for a NEW redeem or transfer rule — copied once into the rule's calculation, which
 * then owns its values (events never read the wallet's). Empty when the wallet has none.
 */
export function burnRuleDefaults(type: RuleType, wallet: AccountType | undefined): BurnRuleDefaults {
  if (wallet?.type !== 'POINTS') return {};
  const config = pointsConfig(wallet);
  if (type === 'RedemptionRule' && config.redemption) {
    const r = config.redemption;
    return {
      ...(r.rate != null ? { calcCashPerPoint: String(r.rate) } : {}),
      ...(r.min_points != null ? { calcMinRedeem: String(r.min_points) } : {}),
      ...(r.target_account_type_id ? { calcCashAccountTypeId: r.target_account_type_id } : {}),
    };
  }
  if (type === 'TransferRule' && config.transfer?.daily_limit != null) {
    return { calcMaxPerDay: String(config.transfer.daily_limit) };
  }
  return {};
}
