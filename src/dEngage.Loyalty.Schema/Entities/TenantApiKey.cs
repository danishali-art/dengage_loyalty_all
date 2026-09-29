namespace dEngage.Loyalty.Schema.Entities;

public class TenantApiKey
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string KeyPrefix { get; set; } = default!;
    public string HashedKey { get; set; } = default!;
    public DateTime CreatedAt { get; set; }
    public DateTime? LastUsedAt { get; set; }
    public DateTime? RevokedAt { get; set; }

    public Tenant Tenant { get; set; } = default!;
}
