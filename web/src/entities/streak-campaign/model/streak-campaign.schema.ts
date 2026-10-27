import { z } from 'zod';
import { translate } from '@/shared/i18n';
import { cloneJson } from '@/shared/lib/forms';
import type { DecimalString } from '@/shared/lib/money';
import { localInputToUtcIso, utcIsoToLocalInput } from '@/shared/lib/date';
import { validateConditions, validateStreakConfig, type ConditionClause } from '@/shared/lib/dsl';
import {
  STREAK_NAME_MAX,
  STREAK_TRIGGER_MAX,
  type CreateStreakCampaignRequest,
  type StreakCampaign,
  type StreakConfig,
} from './types';

/**
 * The streak-campaign form, checked against
 * StreakCampaignsValidators. The streak config and conditions are then checked by the engine's
 * own validators (`streakDslErrors`) before saving.
 */
const DECIMAL = /^\d{1,16}(\.\d{1,4})?$/;
const WHOLE = /^\d{1,9}$/;

export function streakCampaignFormSchema() {
  return z
    .object({
      name: z
        .string()
        .trim()
        .min(1, translate('streaks.form.errors.nameRequired'))
        .max(STREAK_NAME_MAX, translate('streaks.form.errors.tooLong', { max: STREAK_NAME_MAX })),
      trigger: z
        .string()
        .trim()
        .min(1, translate('streaks.form.errors.required'))
        .max(STREAK_TRIGGER_MAX, translate('streaks.form.errors.tooLong', { max: STREAK_TRIGGER_MAX })),
      targetAccountTypeId: z.string().nullable(),
      activeFrom: z.string(),
      activeTo: z.string(),
      conditions: z.array(z.custom<ConditionClause>()),
      period: z.enum(['day', 'week', 'month']),
      weekStart: z.enum(['monday', 'sunday']),
      targetPeriods: z.string(),
      metric: z.enum(['sum', 'count']),
      threshold: z.string(),
      timezone: z.string(),
      onComplete: z.enum(['restart', 'stop']),
      rewardKind: z.enum(['fixed_bonus', 'reward_definition']),
      rewardAmount: z.string(),
      rewardDefinitionId: z.string().nullable(),
    })
    .superRefine((v, ctx) => {
      const issue = (path: string, key: string): void =>
        ctx.addIssue({ code: 'custom', path: [path], message: translate(key) });
      if (!v.targetAccountTypeId) issue('targetAccountTypeId', 'streaks.form.errors.required');
      if (!WHOLE.test(v.targetPeriods.trim())) issue('targetPeriods', 'streaks.form.errors.whole');
      if (!DECIMAL.test(v.threshold.trim())) issue('threshold', 'streaks.form.errors.decimal');
      if (v.rewardKind === 'fixed_bonus' && !DECIMAL.test(v.rewardAmount.trim()))
        issue('rewardAmount', 'streaks.form.errors.decimal');
      if (v.rewardKind === 'reward_definition' && !v.rewardDefinitionId)
        issue('rewardDefinitionId', 'streaks.form.errors.required');
    });
}
export type StreakCampaignFormValues = z.infer<ReturnType<typeof streakCampaignFormSchema>>;

export function streakCampaignFormValues(
  existing: StreakCampaign | undefined,
  defaults: { trigger: string; targetAccountTypeId: string | null },
): StreakCampaignFormValues {
  const c = existing?.config;
  return {
    name: existing?.name ?? '',
    trigger: existing?.trigger ?? defaults.trigger,
    targetAccountTypeId: existing?.targetAccountTypeId ?? defaults.targetAccountTypeId,
    activeFrom: utcIsoToLocalInput(existing?.activeFrom),
    activeTo: utcIsoToLocalInput(existing?.activeTo),
    // The API types a clause's value as free-form JSON; ConditionClause is its DSL shape.
    conditions: existing?.conditions ? (cloneJson(existing.conditions) as ConditionClause[]) : [],
    period: c?.period ?? 'week',
    weekStart: c?.week_start ?? 'monday',
    targetPeriods: String(c?.target_periods ?? 4),
    metric: c?.aggregate.metric ?? 'count',
    threshold: c?.aggregate.threshold ?? '1',
    timezone: c?.timezone ?? 'UTC',
    onComplete: c?.on_complete ?? 'restart',
    rewardKind: c?.reward.kind ?? 'fixed_bonus',
    rewardAmount: c?.reward.amount ?? '10',
    rewardDefinitionId: c?.reward.reward_definition_id ?? null,
  };
}

export function toStreakConfig(v: StreakCampaignFormValues): StreakConfig {
  return {
    period: v.period,
    week_start: v.weekStart,
    target_periods: Number(v.targetPeriods),
    aggregate: { metric: v.metric, threshold: v.threshold.trim() as DecimalString },
    timezone: v.timezone,
    on_complete: v.onComplete,
    reward:
      v.rewardKind === 'fixed_bonus'
        ? { kind: 'fixed_bonus', amount: v.rewardAmount.trim() as DecimalString }
        : { kind: 'reward_definition', reward_definition_id: v.rewardDefinitionId },
  };
}

/**
 * ConditionDsl + StreakConfig.Validate, ported verbatim (`shared/lib/dsl`): the engine's messages
 * for anything it would reject. Empty means the campaign can be saved.
 */
export function streakDslErrors(v: StreakCampaignFormValues): string[] {
  return [...validateConditions(v.conditions), ...validateStreakConfig(toStreakConfig(v))];
}

/** The whole campaign, for create and for edit. */
export function toStreakCampaignRequest(v: StreakCampaignFormValues): CreateStreakCampaignRequest {
  return {
    name: v.name,
    trigger: v.trigger,
    targetAccountTypeId: v.targetAccountTypeId!,
    conditions: v.conditions.length ? v.conditions : null,
    config: toStreakConfig(v),
    activeFrom: localInputToUtcIso(v.activeFrom),
    activeTo: localInputToUtcIso(v.activeTo),
  };
}
