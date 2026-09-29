namespace dEngage.Loyalty.Schema.Entities;

public class StreakPeriodState
{
    public Guid TenantId { get; set; }
    public Guid CampaignId { get; set; }
    public string ContactKey { get; set; } = default!;
    public DateOnly PeriodStart { get; set; }
    public decimal AggSum { get; set; }
    public int AggCount { get; set; }
    public bool Met { get; set; }
    public DateTime UpdatedAt { get; set; }
}
