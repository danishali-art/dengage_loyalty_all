namespace dEngage.Loyalty.Schema.Entities;

// One generic table shared by every versioned config aggregate (Program, TierDefinition, Rule,
// RewardDefinition, AccountType, StreakCampaign) instead of a table per entity type — the access
// shape is identical (append a snapshot, list by entity, fetch one), same generic-repository
// philosophy as IRepository<TEntity>. EntityType is the CLR type name of the versioned entity
// (e.g. "Program", "TierDefinition"), Snapshot is the full serialized entity state at save time.
public class ConfigVersion : ITenantScopedEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string EntityType { get; set; } = default!;
    public Guid EntityId { get; set; }
    public int VersionNumber { get; set; }
    public string Snapshot { get; set; } = default!;
    public string ChangeType { get; set; } = default!; // created | updated | deleted
    public string? ChangeSummary { get; set; }
    public string ChangedBy { get; set; } = default!;
    public DateTime ChangedAt { get; set; }
}
