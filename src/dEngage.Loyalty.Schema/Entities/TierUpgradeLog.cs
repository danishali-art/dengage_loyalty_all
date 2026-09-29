namespace dEngage.Loyalty.Schema.Entities;

public class TierUpgradeLog
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string ContactKey { get; set; } = default!;
    public Guid? FromTierId { get; set; }
    public Guid ToTierId { get; set; }
    public decimal QualifyingPts { get; set; }
    public string SourceEventId { get; set; } = default!;
    public DateTime CreatedAt { get; set; }

    public TierDefinition? FromTier { get; set; }
    public TierDefinition ToTier { get; set; } = default!;
}
