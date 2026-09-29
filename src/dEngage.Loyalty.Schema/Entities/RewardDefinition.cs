namespace dEngage.Loyalty.Schema.Entities;

public class RewardDefinition : ITenantScopedEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid ProgramId { get; set; }
    public string Name { get; set; } = default!;
    public string DisplayName { get; set; } = default!;
    public string Acquisition { get; set; } = default!;
    public string RewardType { get; set; } = default!;
    public Guid? StampAccountTypeId { get; set; }
    public decimal? PointsPrice { get; set; }
    public Guid? PointsAccountTypeId { get; set; }
    public string TypeConfig { get; set; } = "{}";
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }

    public Program Program { get; set; } = default!;
    public AccountType? StampAccountType { get; set; }
    public AccountType? PointsAccountType { get; set; }
}
