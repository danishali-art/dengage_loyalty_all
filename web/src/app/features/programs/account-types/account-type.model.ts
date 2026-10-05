/**
 * STAMP was retired by CR 2026-10-05: it can't be created and the account-type list no longer
 * returns it. It stays here because Customer 360 still shows customers' historical stamp wallets.
 */
export type AccountTypeKind = 'POINTS' | 'CASH' | 'STAMP';

/**
 * Mirrors `SupportedCurrencies.All` (src/dEngage.Loyalty.Shared/Constants/SupportedCurrencies.cs,
 * 1.3.CL item 4) — the backend rejects any other CASH currency, so keep both lists in step.
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
export type CurrencyCode = (typeof SUPPORTED_CURRENCIES)[number];
export const DEFAULT_CURRENCY: CurrencyCode = 'SAR';

/** Backend-validated shape (AccountTypeConfigValidators.cs) — extra fields are accepted but ignored. */
export interface PointsConfig {
  decimals: number;
  expiration_days?: number | null;
  /** 1.3.CL item 1: "expiring soon" warning, moved here from Program. Requires expiration_days. */
  warning_days?: number | null;
  redemption?: { rate: number; min_points: number; target_account_type_id: string } | null;
  /**
   * CR 2026-09-30 addendum A: points transfer between customers, capped per sender per UTC day
   * (PointsTransferHandler). Absent = transfers are refused (`transfer_not_configured`).
   * POINTS only — the backend rejects it on CASH and STAMP.
   */
  transfer?: { daily_limit: number } | null;
}

/** CASH has no expiration_days (1.3.CL item 3) — the backend rejects it. */
export interface CashConfig {
  currency: string;
  decimals: number;
}

export interface AccountType {
  id: string;
  type: AccountTypeKind;
  name: string;
  config: PointsConfig | CashConfig | Record<string, unknown>;
  createdAt: string;
  /** 1.3.CL item 1: this wallet drives tier qualification (POINTS only, at most one per program). */
  isTierQualifying: boolean;
}

export interface CreateAccountTypeRequest {
  type: AccountTypeKind;
  name: string;
  config: PointsConfig | CashConfig | Record<string, unknown>;
  isTierQualifying?: boolean;
}

export interface UpdateAccountTypeRequest {
  name?: string;
  config?: PointsConfig | CashConfig | Record<string, unknown>;
  isTierQualifying?: boolean;
}
