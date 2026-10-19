import { z } from 'zod';
import { translate } from '@/shared/i18n';
import { cloneJson } from '@/shared/lib/forms';
import { gte, type DecimalString } from '@/shared/lib/money';
import { localInputToUtcIso, utcIsoToLocalInput } from '@/shared/lib/date';
import { validateConditionLeaf, type ConditionLeaf } from '@/shared/lib/dsl';
import {
  CARD_BUCKET_NAME_MAX,
  CARD_BUCKET_PRIORITY,
  type CardBucket,
  type CountryMode,
  type CreateCardBucketRequest,
} from './types';

/**
 * The card-bucket form (port of card-bucket-form.page.ts), checked against CardBucketsValidators:
 * name 1–255, reward amount > 0, amount max ≥ min, hours 0–23. Amounts stay decimal strings —
 * up to 16 integer + 4 fractional digits (numeric(20,4)) — never JS numbers.
 */
const DECIMAL = /^\d{1,16}(\.\d{1,4})?$/;
const HOUR = /^\d{1,2}$/;

const blank = (v: string): boolean => v.trim() === '';
const optionalDecimalOk = (v: string): boolean => blank(v) || DECIMAL.test(v.trim());
const optionalHourOk = (v: string): boolean => blank(v) || (HOUR.test(v.trim()) && Number(v) <= 23);

export function cardBucketFormSchema() {
  return z
    .object({
      name: z
        .string()
        .trim()
        .min(1, translate('cardBuckets.form.errors.nameRequired'))
        .max(
          CARD_BUCKET_NAME_MAX,
          translate('cardBuckets.form.errors.tooLong', { max: CARD_BUCKET_NAME_MAX }),
        ),
      targetAccountTypeId: z.string().nullable(),
      rewardAmount: z.string(),
      activeFrom: z.string(),
      activeTo: z.string(),
      mccCodes: z.string(),
      amountMin: z.string(),
      amountMax: z.string(),
      countryMode: z.enum(['in', 'not_in']),
      countries: z.string(),
      requireCaptured: z.boolean(),
      hourFrom: z.string(),
      hourTo: z.string(),
      daysOfWeek: z.array(z.string()),
      perCustomerPerDay: z.string(),
      perCustomerTotal: z.string(),
      additionalConditions: z.array(z.custom<ConditionLeaf>()),
    })
    .superRefine((v, ctx) => {
      const issue = (path: string, key: string): void =>
        ctx.addIssue({ code: 'custom', path: [path], message: translate(key) });

      if (!v.targetAccountTypeId) issue('targetAccountTypeId', 'cardBuckets.form.errors.required');
      if (!DECIMAL.test(v.rewardAmount.trim()) || !(Number(v.rewardAmount) > 0))
        issue('rewardAmount', 'cardBuckets.form.errors.positiveDecimal');
      for (const field of ['amountMin', 'amountMax', 'perCustomerPerDay', 'perCustomerTotal'] as const) {
        if (!optionalDecimalOk(v[field])) issue(field, 'cardBuckets.form.errors.decimal');
      }
      if (
        !blank(v.amountMin) &&
        !blank(v.amountMax) &&
        optionalDecimalOk(v.amountMin) &&
        optionalDecimalOk(v.amountMax) &&
        !gte(v.amountMax.trim(), v.amountMin.trim())
      )
        issue('amountMax', 'cardBuckets.form.errors.amountMax');
      for (const field of ['hourFrom', 'hourTo'] as const) {
        if (!optionalHourOk(v[field])) issue(field, 'cardBuckets.form.errors.hour');
      }
      // GroupedConditionDsl.Validate runs on save: reject a leaf the engine would reject.
      v.additionalConditions.forEach((leaf) => {
        const error = validateConditionLeaf(leaf);
        if (error) ctx.addIssue({ code: 'custom', path: ['additionalConditions'], message: error });
      });
    });
}
export type CardBucketFormValues = z.infer<ReturnType<typeof cardBucketFormSchema>>;

const toCsv = (values: readonly string[] | null | undefined): string => values?.join(', ') ?? '';
function fromCsv(raw: string): string[] | null {
  const values = raw
    .split(',')
    .map((s) => s.trim())
    .filter((s) => s);
  return values.length ? values : null;
}
const decimalOrNull = (v: string): DecimalString | null => (blank(v) ? null : (v.trim() as DecimalString));
const intOrNull = (v: string): number | null => (blank(v) ? null : Number(v));

export function cardBucketFormValues(
  existing: CardBucket | undefined,
  defaultAccountTypeId: string | null,
): CardBucketFormValues {
  return {
    name: existing?.name ?? '',
    targetAccountTypeId: existing?.targetAccountTypeId ?? defaultAccountTypeId,
    rewardAmount: existing?.rewardAmount ?? '1',
    activeFrom: utcIsoToLocalInput(existing?.activeFrom),
    activeTo: utcIsoToLocalInput(existing?.activeTo),
    mccCodes: toCsv(existing?.mccCodes),
    amountMin: existing?.amountMin ?? '',
    amountMax: existing?.amountMax ?? '',
    countryMode: existing?.countryMode ?? 'in',
    countries: toCsv(existing?.countries),
    requireCaptured: existing?.requireCaptured ?? true,
    hourFrom: existing?.hourFrom == null ? '' : String(existing.hourFrom),
    hourTo: existing?.hourTo == null ? '' : String(existing.hourTo),
    daysOfWeek: existing?.daysOfWeek ? [...existing.daysOfWeek] : [],
    perCustomerPerDay: existing?.perCustomerPerDay ?? '',
    perCustomerTotal: existing?.perCustomerTotal ?? '',
    additionalConditions: existing?.additionalConditions
      ? (cloneJson(existing.additionalConditions) as ConditionLeaf[])
      : [],
  };
}

/**
 * The whole bucket, for create and for edit (as the Angular form sent it). On edit the API keeps
 * a field's current value when it's null, so clearing a filter there doesn't remove it.
 */
export function toCardBucketRequest(v: CardBucketFormValues): CreateCardBucketRequest {
  const countries = fromCsv(v.countries);
  return {
    name: v.name,
    mccCodes: fromCsv(v.mccCodes),
    amountMin: decimalOrNull(v.amountMin),
    amountMax: decimalOrNull(v.amountMax),
    countryMode: countries ? (v.countryMode as CountryMode) : null,
    countries,
    requireCaptured: v.requireCaptured,
    hourFrom: intOrNull(v.hourFrom),
    hourTo: intOrNull(v.hourTo),
    daysOfWeek: v.daysOfWeek.length ? v.daysOfWeek : null,
    perCustomerPerDay: decimalOrNull(v.perCustomerPerDay),
    perCustomerTotal: decimalOrNull(v.perCustomerTotal),
    targetAccountTypeId: v.targetAccountTypeId!,
    rewardAmount: v.rewardAmount.trim(),
    priority: CARD_BUCKET_PRIORITY,
    activeFrom: localInputToUtcIso(v.activeFrom),
    activeTo: localInputToUtcIso(v.activeTo),
    additionalConditions: v.additionalConditions.length ? v.additionalConditions : null,
  };
}
