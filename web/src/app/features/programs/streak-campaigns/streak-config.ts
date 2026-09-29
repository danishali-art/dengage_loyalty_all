/** Ported verbatim from `LoyaltySaaS.RuleEngine.Campaigns.Streak.StreakConfig.Validate`. */
import { isValidIanaTimezone } from '../../../shared/date/timezones';
import { StreakConfig } from './streak-campaign.model';

const MAX_PERIODS: Record<StreakConfig['period'], number> = { day: 365, week: 52, month: 12 };

export function validateStreakConfig(config: StreakConfig): string[] {
  const errors: string[] = [];

  if (!['day', 'week', 'month'].includes(config.period)) errors.push(`unknown period '${config.period}'`);
  if (!['monday', 'sunday'].includes(config.week_start)) errors.push(`unknown week_start '${config.week_start}'`);
  if (config.target_periods < 1) errors.push('target_periods must be >= 1');

  const maxPeriods = MAX_PERIODS[config.period] ?? Infinity;
  if (config.target_periods > maxPeriods) {
    errors.push(`target_periods ${config.target_periods} exceeds the 12-month retention limit (${maxPeriods} ${config.period}s)`);
  }

  if (!config.aggregate) {
    errors.push('aggregate is required');
  } else {
    if (!['sum', 'count'].includes(config.aggregate.metric)) {
      errors.push(`unknown aggregate.metric '${config.aggregate.metric}'`);
    }
    if (!(Number(config.aggregate.threshold) > 0)) errors.push('aggregate.threshold must be > 0');
  }

  if (!config.timezone?.trim()) errors.push('timezone is required');
  else if (!isValidIanaTimezone(config.timezone)) errors.push(`unknown timezone '${config.timezone}'`);

  if (!['restart', 'stop'].includes(config.on_complete)) errors.push(`unknown on_complete '${config.on_complete}'`);

  if (!config.reward) {
    errors.push('reward is required');
  } else if (config.reward.kind === 'fixed_bonus') {
    if (!(Number(config.reward.amount) > 0)) errors.push('reward.amount must be > 0 for fixed_bonus');
  } else if (config.reward.kind === 'reward_definition') {
    if (!config.reward.reward_definition_id) errors.push('reward.reward_definition_id is required');
  } else {
    errors.push(`unknown reward.kind '${String(config.reward.kind)}'`);
  }

  return errors;
}
