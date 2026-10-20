namespace dEngage.Loyalty.Schema.Entities;

// Platform alignment D4: global runtime settings (batch sizes, cache lifetimes, ...). Same shape as
// the dEngage platform's sys.global_config — module_name + parameter_name, a scalar `Value` or a
// JSON `ValueJson`. One row per setting; the table is the only source (no defaults in code or
// appsettings). Per-tenant settings live in loyalty_tenant_config instead (CHECK constraint keeps
// them out of here). Read through ILoyaltyConfig.GetAsync.
public class LoyaltyConfigEntry
{
    public long Id { get; set; }
    public string ModuleName { get; set; } = default!;
    public string ParameterName { get; set; } = default!;
    public string? Value { get; set; }
    public string? ValueJson { get; set; }

    // Value holds an ENC(...) token, decrypted by ISecretProtector when read.
    public bool IsSecret { get; set; }

    public string? Description { get; set; }
    public DateTime UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }
}
