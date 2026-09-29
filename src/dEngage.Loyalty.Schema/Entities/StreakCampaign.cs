using dEngage.Loyalty.Shared;

namespace dEngage.Loyalty.Schema.Entities;

// A streak campaign's own table — deliberately separate from Rule. Unlike an earn rule
// (Spend/Stamp/FixedBonus), a campaign never competes for a winner slot and has no
// Calculation/Limits/Priority/Stackable — those fields would be meaningless here.
public class StreakCampaign : ITenantScopedEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid ProgramId { get; set; }
    public string Name { get; set; } = default!;
    public string Trigger { get; set; } = default!;
    public Guid TargetAccountTypeId { get; set; }
    public string? Conditions { get; set; }
    public string Config { get; set; } = default!;
    public DateTime? ActiveFrom { get; set; }
    public DateTime? ActiveTo { get; set; }
    public string Status { get; set; } = RuleStatus.Active;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public Program Program { get; set; } = default!;
    public AccountType TargetAccountType { get; set; } = default!;
}
