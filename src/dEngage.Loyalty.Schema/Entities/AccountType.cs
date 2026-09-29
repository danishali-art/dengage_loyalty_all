namespace dEngage.Loyalty.Schema.Entities;

public class AccountType : ITenantScopedEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid ProgramId { get; set; }
    public string Type { get; set; } = default!;
    public string Name { get; set; } = default!;
    public string Config { get; set; } = "{}";

    // 1.3.CL item 1: the wallet whose balance drives tier qualification. Replaces
    // Program.QualifyingAccountTypeId (deprecated, no longer written). POINTS only, and at most
    // one per program — enforced by ux_account_types_tier_qualifying.
    public bool IsTierQualifying { get; set; }

    public DateTime CreatedAt { get; set; }

    public Program Program { get; set; } = default!;
}
