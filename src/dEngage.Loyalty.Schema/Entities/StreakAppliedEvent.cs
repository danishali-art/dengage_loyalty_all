namespace dEngage.Loyalty.Schema.Entities;

public class StreakAppliedEvent
{
    public Guid TenantId { get; set; }
    public Guid CampaignId { get; set; }
    public string EventId { get; set; } = default!;
    public DateTime AppliedAt { get; set; }
}
