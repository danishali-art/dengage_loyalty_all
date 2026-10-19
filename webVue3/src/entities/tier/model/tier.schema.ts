import { z } from 'zod';
import { translate } from '@/shared/i18n';
import { isDecimalString } from '@/shared/lib/money';
import {
  TIER_DISPLAY_NAME_MAX,
  TIER_NAME_MAX,
  type CreateTierRequest,
  type QualifyingModel,
  type Tier,
  type UpdateTierRequest,
} from './types';

/**
 * The tier form (port of tier-form.dialog.ts), checked against TiersValidators: names, minimum
 * points ≥ 0 (a decimal string — points are numeric(20,4), never a JS number), a positive
 * qualifying period when periodic, grace days ≥ 0.
 */
const WHOLE = /^\d+$/;

export function tierFormSchema() {
  return z
    .object({
      name: z
        .string()
        .trim()
        .min(1, translate('tiers.form.errors.nameRequired'))
        .max(TIER_NAME_MAX, translate('tiers.form.errors.tooLong', { max: TIER_NAME_MAX })),
      displayName: z
        .string()
        .trim()
        .min(1, translate('tiers.form.errors.displayNameRequired'))
        .max(TIER_DISPLAY_NAME_MAX, translate('tiers.form.errors.tooLong', { max: TIER_DISPLAY_NAME_MAX })),
      minPoints: z
        .string()
        .trim()
        .refine((v) => isDecimalString(v) && !v.startsWith('-'), translate('tiers.form.errors.minPoints')),
      qualifyingModel: z.enum(['lifetime', 'periodic']),
      qualifyingPeriodDays: z.string(),
      graceDays: z.string().refine((v) => WHOLE.test(v), translate('tiers.form.errors.graceDays')),
    })
    .superRefine((v, ctx) => {
      if (
        v.qualifyingModel === 'periodic' &&
        !(WHOLE.test(v.qualifyingPeriodDays) && Number(v.qualifyingPeriodDays) > 0)
      ) {
        ctx.addIssue({
          code: 'custom',
          path: ['qualifyingPeriodDays'],
          message: translate('tiers.form.errors.periodDays'),
        });
      }
    });
}
export type TierFormValues = z.infer<ReturnType<typeof tierFormSchema>>;

export function tierFormValues(existing?: Tier): TierFormValues {
  return {
    name: existing?.name ?? '',
    displayName: existing?.displayName ?? '',
    minPoints: existing?.minPoints ?? '0',
    qualifyingModel: (existing?.qualifyingModel ?? 'lifetime') as QualifyingModel,
    qualifyingPeriodDays: String(existing?.qualifyingPeriodDays ?? 365),
    graceDays: String(existing?.graceDays ?? 0),
  };
}

function common(values: TierFormValues) {
  const periodic = values.qualifyingModel === 'periodic';
  return {
    name: values.name,
    displayName: values.displayName,
    minPoints: values.minPoints,
    qualifyingModel: values.qualifyingModel,
    qualifyingPeriodDays: periodic ? Number(values.qualifyingPeriodDays) : null,
    graceDays: Number(values.graceDays),
  };
}

export function toCreateTierRequest(values: TierFormValues, sortOrder: number): CreateTierRequest {
  return { ...common(values), sortOrder };
}

export function toUpdateTierRequest(values: TierFormValues): UpdateTierRequest {
  return common(values);
}
