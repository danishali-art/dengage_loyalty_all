namespace dEngage.Loyalty.Schema.Entities;

// Platform alignment D4: a tenant setting's value for one tenant (like the dEngage platform's
// sys.config_account_* tables). Tenant settings (LoyaltyConfigKeys.TenantSettings, enforced by a
// CHECK constraint) have no global value: every tenant has its own row, written when the tenant is
// created (PlatformAppService) and backfilled by migration for existing tenants. A missing row
// fails. Read through ILoyaltyConfig.GetForTenantAsync.
public class LoyaltyTenantConfigEntry : ITenantScopedEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
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
