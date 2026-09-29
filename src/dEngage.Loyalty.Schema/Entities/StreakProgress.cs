namespace dEngage.Loyalty.Schema.Entities;

public class StreakProgress
{
    public Guid TenantId { get; set; }
    public Guid CampaignId { get; set; }
    public string ContactKey { get; set; } = default!;
    public int StreakCount { get; set; }
    public DateOnly? LastMetPeriod { get; set; }
    public int Completions { get; set; }
    public string Status { get; set; } = StreakProgressStatus.Active;
    public DateTime UpdatedAt { get; set; }
}

public static class StreakProgressStatus
{
    public const string Active = "active";
    public const string CompletedStopped = "completed_stopped";
}
