namespace dEngage.Loyalty.Schema.Entities;

// Platform alignment D4: runtime settings that used to be hard-coded (rate limits, batch sizes,
// cache lifetimes, ...). Same shape as the dEngage platform's sys.global_config — module_name +
// parameter_name, a scalar `Value` or a JSON `ValueJson` — plus an optional TenantId: a tenant
// row overrides the global row (TenantId null) for that tenant. Read through ILoyaltyConfig.
// Not an ITenantScopedEntity: global rows have no tenant.
public class LoyaltyConfigEntry
{
    public long Id { get; set; }
    public string ModuleName { get; set; } = default!;
    public string ParameterName { get; set; } = default!;
    public Guid? TenantId { get; set; }
    public string? Value { get; set; }
    public string? ValueJson { get; set; }

    // Value holds an ENC(...) token, decrypted by ISecretProtector when read.
    public bool IsSecret { get; set; }

    public string? Description { get; set; }
    public DateTime UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }
}
