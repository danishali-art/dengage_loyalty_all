/**
 * Ported verbatim from `src/LoyaltySaaS.RuleEngine/Campaigns/Streak/StreakConfig.cs`. Streak DSL
 * types carry explicit [JsonPropertyName] snake_case attributes in C# that win over the outer
 * DTO's camelCase policy — so `config` stays snake_case even though the rest of the campaign is
 * camelCase. Do not "fix" the casing to match the rest of the app; it must match the engine's own
 * DSL exactly.
 */
import { ConditionClause } from '../../../shared/forms/condition-dsl';

export type StreakPeriod = 'day' | 'week' | 'month';
export type StreakWeekStart = 'monday' | 'sunday';
export type StreakOnComplete = 'restart' | 'stop';
export type StreakMetric = 'sum' | 'count';
export type StreakRewardKind = 'fixed_bonus' | 'reward_definition';

export interface StreakAggregate {
  metric: StreakMetric;
  threshold: string;
}

export interface StreakReward {
  kind: StreakRewardKind;
  amount?: string | null;
  reward_definition_id?: string | null;
}

export interface StreakConfig {
  period: StreakPeriod;
  week_start: StreakWeekStart;
  target_periods: number;
  aggregate: StreakAggregate;
  timezone: string;
  on_complete: StreakOnComplete;
  reward: StreakReward;
}

export type StreakCampaignStatus = 'active' | 'disabled' | 'deleted';

export interface StreakCampaign {
  id: string;
  name: string;
  trigger: string;
  targetAccountTypeId: string;
  conditions: ConditionClause[] | null;
  config: StreakConfig;
  activeFrom: string | null;
  activeTo: string | null;
  status: StreakCampaignStatus;
  createdAt: string;
  updatedAt: string;
}

export interface CreateStreakCampaignRequest {
  name: string;
  trigger: string;
  targetAccountTypeId: string;
  conditions?: ConditionClause[] | null;
  config: StreakConfig;
  activeFrom?: string | null;
  activeTo?: string | null;
}

export interface UpdateStreakCampaignRequest {
  name?: string;
  trigger?: string;
  targetAccountTypeId?: string;
  conditions?: ConditionClause[] | null;
  config?: StreakConfig;
  activeFrom?: string | null;
  activeTo?: string | null;
}

export interface StreakCampaignListFilter {
  event?: string;
  status?: StreakCampaignStatus;
}
