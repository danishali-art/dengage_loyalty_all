using dEngage.Loyalty.Shared;

namespace dEngage.Loyalty.Schema.Entities;

public class TierDefinition : ITenantScopedEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid ProgramId { get; set; }
    public string Name { get; set; } = default!;
    public string DisplayName { get; set; } = default!;
    public decimal MinPoints { get; set; }
    public int? QualifyingDays { get; set; }
    public int GraceDays { get; set; }
    public int SortOrder { get; set; }
    public string Status { get; set; } = TierStatus.Active;
    public DateTime CreatedAt { get; set; }

    public Program Program { get; set; } = default!;
}
