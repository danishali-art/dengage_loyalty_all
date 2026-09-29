namespace dEngage.Loyalty.RuleEngine.Models;

public class ConditionContext
{
    public static readonly ConditionContext Empty = new();

    public string? CustomerTierName { get; init; }

    // event_type → most recent occurred_at for this contact (excluding the current event)
    public IReadOnlyDictionary<string, DateTime> LatestEventAt { get; init; } =
        new Dictionary<string, DateTime>();
}
