using dEngage.Loyalty.Shared;

namespace dEngage.Loyalty.Schema.Entities;

// Role is "platform_admin" (TenantId null — may act on any tenant) or "tenant_admin" (TenantId required).
public class AdminUser
{
    public Guid Id { get; set; }
    public string Email { get; set; } = default!;
    public string PasswordHash { get; set; } = default!;
    public string Role { get; set; } = default!;
    public Guid? TenantId { get; set; }
    public string Status { get; set; } = EntityStatus.Active;
    public DateTime CreatedAt { get; set; }

    public Tenant? Tenant { get; set; }
}
