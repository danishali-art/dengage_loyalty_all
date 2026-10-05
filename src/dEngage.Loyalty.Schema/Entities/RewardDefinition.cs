using dEngage.Loyalty.Shared;

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
    public decimal? PointsPrice { get; set; }
    public Guid? PointsAccountTypeId { get; set; }
    public string TypeConfig { get; set; } = "{}";
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }

    // CR 2026-09-30 (A4): cashback rewards start PendingApproval; a different admin than
    // CreatedBy approves them (same identity check as CASH rules, CR-04).
    public string Status { get; set; } = RewardStatus.Active;
    public string? CreatedBy { get; set; }
    public string? ApprovedBy { get; set; }

    public Program Program { get; set; } = default!;
    public AccountType? PointsAccountType { get; set; }
}
