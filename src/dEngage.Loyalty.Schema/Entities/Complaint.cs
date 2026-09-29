using dEngage.Loyalty.Shared;

namespace dEngage.Loyalty.Schema.Entities;

public class Complaint : ITenantScopedEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid? ProgramId { get; set; }

    // Matches the ContactKey convention used across the schema (CustomerAccount etc.) —
    // there is no first-class Customer entity, customers are identified by this string.
    public string? CustomerKey { get; set; }

    public string Subject { get; set; } = default!;
    public string? Description { get; set; }
    public string Status { get; set; } = ComplaintStatus.Open;
    public DateTime CreatedAt { get; set; }
    public DateTime? ResolvedAt { get; set; }

    public Program? Program { get; set; }
}
