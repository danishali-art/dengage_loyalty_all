namespace dEngage.Loyalty.Schema.Entities;

public class StreakLog
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid CampaignId { get; set; }
    public string ContactKey { get; set; } = default!;
    public int CompletionNo { get; set; }
    public DateOnly CompletedPeriod { get; set; }
    public int Periods { get; set; }
    public string RewardKind { get; set; } = default!;
    public string? RewardRef { get; set; }
    public string SourceEventId { get; set; } = default!;
    public DateTime CreatedAt { get; set; }
}
