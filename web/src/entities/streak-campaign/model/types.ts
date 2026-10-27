import type { ApiSchema } from '@/shared/api';

/**
 * A streak campaign (StreakCampaignsDtos.cs). `config` is the engine's own DSL and keeps its
 * snake_case keys (`[JsonPropertyName]` in StreakConfig.cs win over the API's camelCase policy) —
 * never "fix" the casing. `conditions` is the flat ConditionClause DSL, not the rules' tree.
 */
export type StreakCampaign = ApiSchema<'StreakCampaignResponse'>;
export type CreateStreakCampaignRequest = ApiSchema<'CreateStreakCampaignRequest'>;
export type UpdateStreakCampaignRequest = ApiSchema<'UpdateStreakCampaignRequest'>;
export type StreakConfig = ApiSchema<'StreakConfig'>;
export type StreakPeriod = StreakConfig['period'];
export type StreakWeekStart = StreakConfig['week_start'];
export type StreakOnComplete = StreakConfig['on_complete'];
export type StreakMetric = StreakConfig['aggregate']['metric'];
export type StreakRewardKind = StreakConfig['reward']['kind'];

export const STREAK_PERIODS: readonly StreakPeriod[] = ['day', 'week', 'month'];
export const STREAK_METRICS: readonly StreakMetric[] = ['sum', 'count'];
export const STREAK_ON_COMPLETE: readonly StreakOnComplete[] = ['restart', 'stop'];

/** StreakCampaignsValidators: name ≤ 255, trigger ≤ 100. */
export const STREAK_NAME_MAX = 255;
export const STREAK_TRIGGER_MAX = 100;
