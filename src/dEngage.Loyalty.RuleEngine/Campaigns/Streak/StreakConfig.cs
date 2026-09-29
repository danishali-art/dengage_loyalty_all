using System.Text.Json;
using System.Text.Json.Serialization;
using dEngage.Loyalty.Shared;

namespace dEngage.Loyalty.RuleEngine.Campaigns.Streak;

public class StreakConfig
{
    [JsonPropertyName("period")]
    public string Period { get; set; } = default!;

    [JsonPropertyName("week_start")]
    public string WeekStart { get; set; } = StreakWeekStart.Monday;

    [JsonPropertyName("target_periods")]
    public int TargetPeriods { get; set; }

    [JsonPropertyName("aggregate")]
    public StreakAggregate Aggregate { get; set; } = default!;

    [JsonPropertyName("timezone")]
    public string Timezone { get; set; } = default!;

    [JsonPropertyName("on_complete")]
    public string OnComplete { get; set; } = StreakOnComplete.Restart;

    [JsonPropertyName("reward")]
    public StreakReward Reward { get; set; } = default!;

    public static StreakConfig Parse(string json)
    {
        var config = JsonSerializer.Deserialize<StreakConfig>(json)
            ?? throw new InvalidOperationException("invalid_streak_config: null");
        config.Validate();
        return config;
    }

    public void Validate()
    {
        if (Period is not (StreakPeriod.Day or StreakPeriod.Week or StreakPeriod.Month))
            throw new InvalidOperationException($"invalid_streak_config: unknown period '{Period}'");
        if (WeekStart is not (StreakWeekStart.Monday or StreakWeekStart.Sunday))
            throw new InvalidOperationException($"invalid_streak_config: unknown week_start '{WeekStart}'");
        if (TargetPeriods < 1)
            throw new InvalidOperationException("invalid_streak_config: target_periods must be >= 1");

        // event_log/period_state retention is 12 months — longer streaks would lose data
        var maxPeriods = Period switch
        {
            StreakPeriod.Day => RetentionPolicy.MaxDailyStreakPeriods,
            StreakPeriod.Week => RetentionPolicy.MaxWeeklyStreakPeriods,
            _ => RetentionPolicy.MaxMonthlyStreakPeriods
        };
        if (TargetPeriods > maxPeriods)
            throw new InvalidOperationException(
                $"invalid_streak_config: target_periods {TargetPeriods} exceeds {RetentionPolicy.EventRetentionMonths}-month retention limit ({maxPeriods} {Period}s)");

        if (Aggregate is null)
            throw new InvalidOperationException("invalid_streak_config: aggregate missing");
        if (Aggregate.Metric is not (StreakMetric.Sum or StreakMetric.Count))
            throw new InvalidOperationException($"invalid_streak_config: unknown aggregate.metric '{Aggregate.Metric}'");
        if (Aggregate.Threshold <= 0)
            throw new InvalidOperationException("invalid_streak_config: aggregate.threshold must be > 0");

        if (string.IsNullOrWhiteSpace(Timezone))
            throw new InvalidOperationException("invalid_streak_config: timezone missing");
        try { _ = TimeZoneInfo.FindSystemTimeZoneById(Timezone); }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            throw new InvalidOperationException($"invalid_streak_config: unknown timezone '{Timezone}'");
        }

        if (OnComplete is not (StreakOnComplete.Restart or StreakOnComplete.Stop))
            throw new InvalidOperationException($"invalid_streak_config: unknown on_complete '{OnComplete}'");

        if (Reward is null)
            throw new InvalidOperationException("invalid_streak_config: reward missing");
        switch (Reward.Kind)
        {
            case StreakRewardKind.FixedBonus:
                if (Reward.Amount is null or <= 0)
                    throw new InvalidOperationException("invalid_streak_config: reward.amount must be > 0 for fixed_bonus");
                break;
            case StreakRewardKind.RewardDefinition:
                if (Reward.RewardDefinitionId is null)
                    throw new InvalidOperationException("invalid_streak_config: reward.reward_definition_id required");
                break;
            default:
                throw new InvalidOperationException($"invalid_streak_config: unknown reward.kind '{Reward.Kind}'");
        }
    }
}

public class StreakAggregate
{
    [JsonPropertyName("metric")]
    public string Metric { get; set; } = default!;

    [JsonPropertyName("threshold")]
    public decimal Threshold { get; set; }
}

public class StreakReward
{
    [JsonPropertyName("kind")]
    public string Kind { get; set; } = default!;

    [JsonPropertyName("amount")]
    public decimal? Amount { get; set; }

    [JsonPropertyName("reward_definition_id")]
    public Guid? RewardDefinitionId { get; set; }
}

public static class StreakPeriod
{
    public const string Day = "day";
    public const string Week = "week";
    public const string Month = "month";
}

public static class StreakWeekStart
{
    public const string Monday = "monday";
    public const string Sunday = "sunday";
}

public static class StreakOnComplete
{
    public const string Restart = "restart";
    public const string Stop = "stop";
}

public static class StreakMetric
{
    public const string Sum = "sum";
    public const string Count = "count";
}

public static class StreakRewardKind
{
    public const string FixedBonus = "fixed_bonus";
    public const string RewardDefinition = "reward_definition";
}
