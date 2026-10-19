import type { ApiSchema } from '@/shared/api';

export type AccountType = ApiSchema<'AccountTypeResponse'>;
export type AccountTypeKind = AccountType['type'];
export type CreatableAccountTypeKind = ApiSchema<'CreateAccountTypeRequest'>['type'];
export type CreateAccountTypeRequest = ApiSchema<'CreateAccountTypeRequest'>;
export type UpdateAccountTypeRequest = ApiSchema<'UpdateAccountTypeRequest'>;

/**
 * The `config` jsonb is free-form in the DTO (JsonElement). Its shape is validated per type by
 * AccountTypeConfigValidators.cs — these mirror what that validator reads. Values are JSON
 * numbers there (it checks `JsonValueKind.Number`), not decimal strings.
 */
export interface PointsConfig {
  decimals: number;
  expiration_days?: number | null;
  /** 1.3.CL item 1: "expiring soon" warning. Requires expiration_days. */
  warning_days?: number | null;
  /** CR 2026-10-05: defaults only — copied into a new redeem rule. Redeem events use the rule. */
  redemption?: { rate: number; min_points: number; target_account_type_id: string } | null;
  /** CR 2026-09-30 addendum A, CR 2026-10-05: defaults only for a new transfer rule. POINTS only. */
  transfer?: { daily_limit: number } | null;
}

/** CASH has no expiration_days (1.3.CL item 3) — the backend rejects it. */
export interface CashConfig {
  currency: string;
  decimals: number;
}

/**
 * Mirrors SupportedCurrencies.All (Shared/Constants/SupportedCurrencies.cs, 1.3.CL item 4) — the
 * backend rejects any other CASH currency, so keep both lists in step.
 */
export const SUPPORTED_CURRENCIES = [
  'SAR',
  'AED',
  'KWD',
  'QAR',
  'BHD',
  'OMR',
  'USD',
  'EUR',
  'GBP',
  'TRY',
] as const;
export const DEFAULT_CURRENCY = 'SAR';

/** AccountTypesValidators: Name is required, at most 100 characters. */
export const ACCOUNT_TYPE_NAME_MAX = 100;

export function pointsConfig(accountType: AccountType | undefined): Partial<PointsConfig> {
  return accountType?.type === 'POINTS' ? ((accountType.config ?? {}) as Partial<PointsConfig>) : {};
}

export function cashCurrency(accountType: AccountType): string | null {
  return accountType.type === 'CASH' ? ((accountType.config as Partial<CashConfig>)?.currency ?? null) : null;
}

export const ACCOUNT_TYPE_ICON: Record<AccountTypeKind, string> = { POINTS: '⭐', CASH: '💳', STAMP: '🎟' };
