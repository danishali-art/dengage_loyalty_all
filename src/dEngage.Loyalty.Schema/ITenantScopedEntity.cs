namespace dEngage.Loyalty.Schema;

// Marker interface for the API layer's generic repository (dEngage.Loyalty.Api.Framework.Data) —
// lives here, not in Api.Framework, since Framework already references Schema and Schema cannot
// reference back.
public interface ITenantScopedEntity
{
    Guid Id { get; }
    Guid TenantId { get; }
}
