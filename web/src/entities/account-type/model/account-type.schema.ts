import { z } from 'zod';
import { translate } from '@/shared/i18n';
import {
  ACCOUNT_TYPE_NAME_MAX,
  DEFAULT_CURRENCY,
  SUPPORTED_CURRENCIES,
  pointsConfig,
  type AccountType,
  type CashConfig,
  type CreatableAccountTypeKind,
  type CreateAccountTypeRequest,
  type UpdateAccountTypeRequest,
} from './types';

/**
 * The account-type form. It must never submit what
 * AccountTypeConfigValidators.cs rejects: decimals 0–4, 0 < warning_days < expiration_days,
 * no CASH expiry, a whitelisted currency, the tier flag on POINTS only, a redemption target when
 * redemption is on, and a positive transfer limit when transfer is on.
 *
 * Numeric inputs stay strings in the form (what the admin typed); the mapper turns them into the
 * JSON numbers the config validator requires.
 */
const WHOLE = /^\d+$/;
const DECIMAL = /^\d{1,16}(\.\d{1,4})?$/;
const filled = (v: string): boolean => v.trim() !== '';

export function accountTypeFormSchema() {
  return z
    .object({
      name: z
        .string()
        .trim()
        .min(1, translate('accountTypes.form.errors.nameRequired'))
        .max(
          ACCOUNT_TYPE_NAME_MAX,
          translate('accountTypes.form.errors.nameTooLong', { max: ACCOUNT_TYPE_NAME_MAX }),
        ),
      type: z.enum(['POINTS', 'CASH']),
      decimals: z
        .string()
        .refine((v) => WHOLE.test(v) && Number(v) <= 4, translate('accountTypes.form.errors.decimals')),
      expirationDays: z.string(),
      warningDays: z.string(),
      isTierQualifying: z.boolean(),
      redemptionEnabled: z.boolean(),
      redemptionRate: z.string(),
      redemptionMinPoints: z.string(),
      redemptionTargetAccountTypeId: z.string(),
      transferEnabled: z.boolean(),
      transferDailyLimit: z.string(),
      currency: z.enum(SUPPORTED_CURRENCIES),
    })
    .superRefine((v, ctx) => {
      if (v.type !== 'POINTS') return;
      if (filled(v.expirationDays) && !(WHOLE.test(v.expirationDays) && Number(v.expirationDays) > 0)) {
        ctx.addIssue({
          code: 'custom',
          path: ['expirationDays'],
          message: translate('accountTypes.form.errors.expiration'),
        });
      }
      const warningError = warningDaysError(v.expirationDays, v.warningDays);
      if (warningError)
        ctx.addIssue({ code: 'custom', path: ['warningDays'], message: translate(warningError) });
      if (v.redemptionEnabled) {
        if (!DECIMAL.test(v.redemptionRate.trim())) {
          ctx.addIssue({
            code: 'custom',
            path: ['redemptionRate'],
            message: translate('accountTypes.form.errors.number'),
          });
        }
        if (!DECIMAL.test(v.redemptionMinPoints.trim())) {
          ctx.addIssue({
            code: 'custom',
            path: ['redemptionMinPoints'],
            message: translate('accountTypes.form.errors.number'),
          });
        }
        if (!v.redemptionTargetAccountTypeId) {
          ctx.addIssue({
            code: 'custom',
            path: ['redemptionTargetAccountTypeId'],
            message: translate('accountTypes.form.errors.redemptionTarget'),
          });
        }
      }
      // The limit only counts while transfer is on: a stale value in the hidden field never blocks a save.
      if (v.transferEnabled && !(WHOLE.test(v.transferDailyLimit) && Number(v.transferDailyLimit) >= 1)) {
        ctx.addIssue({
          code: 'custom',
          path: ['transferDailyLimit'],
          message: translate('accountTypes.transfer.dailyLimitError'),
        });
      }
    });
}
export type AccountTypeFormValues = z.infer<ReturnType<typeof accountTypeFormSchema>>;

/**
 * Mirrors PointsAccountTypeConfigValidator (1.3.CL item 1): warning days need an expiration and
 * must satisfy 0 < warning < expiration. Returns an i18n key, or null.
 */
export function warningDaysError(expirationDays: string, warningDays: string): string | null {
  if (!filled(warningDays)) return null;
  if (!filled(expirationDays)) return 'accountTypes.warningDays.needsExpiry';
  const warning = Number(warningDays);
  if (!Number.isInteger(warning) || warning <= 0) return 'accountTypes.warningDays.positive';
  if (warning >= Number(expirationDays)) return 'accountTypes.warningDays.beforeExpiry';
  return null;
}

/** The form's starting values: an existing account type, or a new POINTS wallet. */
export function accountTypeFormValues(existing?: AccountType): AccountTypeFormValues {
  const points = pointsConfig(existing);
  const cash = existing?.type === 'CASH' ? ((existing.config ?? {}) as Partial<CashConfig>) : {};
  const str = (n: number | null | undefined): string => (n === null || n === undefined ? '' : String(n));
  return {
    name: existing?.name ?? '',
    type: existing?.type === 'CASH' ? 'CASH' : 'POINTS',
    decimals: str(points.decimals ?? cash.decimals ?? 0),
    expirationDays: str(points.expiration_days),
    warningDays: str(points.warning_days),
    isTierQualifying: existing?.isTierQualifying ?? false,
    redemptionEnabled: !!points.redemption,
    redemptionRate: str(points.redemption?.rate ?? 0.01),
    redemptionMinPoints: str(points.redemption?.min_points ?? 0),
    redemptionTargetAccountTypeId: points.redemption?.target_account_type_id ?? '',
    transferEnabled: points.transfer?.daily_limit !== undefined && points.transfer !== null,
    transferDailyLimit: str(points.transfer?.daily_limit ?? 1000),
    currency: (SUPPORTED_CURRENCIES as readonly string[]).includes(cash.currency ?? '')
      ? (cash.currency as AccountTypeFormValues['currency'])
      : DEFAULT_CURRENCY,
  };
}

/** POINTS config keys the form edits; any other stored key is carried through unchanged. */
const POINTS_FORM_KEYS = ['decimals', 'expiration_days', 'warning_days', 'redemption', 'transfer'];

/**
 * Builds the config. It starts from the stored config minus the keys this form owns, so a setting
 * the form doesn't show survives a save — rebuilding from the form alone used to drop `transfer`
 * on every edit (regression test in the spec).
 */
export function toAccountTypeConfig(
  values: AccountTypeFormValues,
  existing?: AccountType,
): Record<string, unknown> {
  if (values.type === 'CASH') return { currency: values.currency, decimals: Number(values.decimals) };
  const kept = Object.fromEntries(
    Object.entries(existing?.type === 'POINTS' ? (existing.config as Record<string, unknown>) : {}).filter(
      ([key]) => !POINTS_FORM_KEYS.includes(key),
    ),
  );
  const config: Record<string, unknown> = { ...kept, decimals: Number(values.decimals) };
  if (filled(values.expirationDays)) {
    config['expiration_days'] = Number(values.expirationDays);
    // A warning only means something before expiry — dropped with it (the validator agrees).
    if (filled(values.warningDays)) config['warning_days'] = Number(values.warningDays);
  }
  if (values.redemptionEnabled) {
    config['redemption'] = {
      rate: Number(values.redemptionRate),
      min_points: Number(values.redemptionMinPoints),
      target_account_type_id: values.redemptionTargetAccountTypeId,
    };
  }
  if (values.transferEnabled) config['transfer'] = { daily_limit: Number(values.transferDailyLimit) };
  return config;
}

/** Only POINTS can carry the tier flag (the backend rejects it elsewhere). */
function tierFlag(values: AccountTypeFormValues): boolean | undefined {
  return values.type === 'POINTS' ? values.isTierQualifying : undefined;
}

export function toCreateAccountTypeRequest(values: AccountTypeFormValues): CreateAccountTypeRequest {
  const type: CreatableAccountTypeKind = values.type;
  return { name: values.name, type, config: toAccountTypeConfig(values), isTierQualifying: tierFlag(values) };
}

export function toUpdateAccountTypeRequest(
  values: AccountTypeFormValues,
  existing: AccountType,
): UpdateAccountTypeRequest {
  return {
    name: values.name,
    config: toAccountTypeConfig(values, existing),
    isTierQualifying: tierFlag(values),
  };
}
