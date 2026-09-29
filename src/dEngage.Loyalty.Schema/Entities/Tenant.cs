using dEngage.Loyalty.Shared;

namespace dEngage.Loyalty.Schema.Entities;

public class Tenant
{
    public Guid Id { get; set; }
    // Human-chosen slug — the external identity everywhere (API URLs, JWT claims, RabbitMQ
    // envelopes, X-Api-Key prefixes, partition table names for ledger_entries/event_inbox/
    // event_log). Id is the internal join key every other tenant-scoped table's FK targets.
    public string Slug { get; set; } = default!;
    public string Name { get; set; } = default!;
    public string Status { get; set; } = TenantStatus.Active;
    public DateTime CreatedAt { get; set; }
}
