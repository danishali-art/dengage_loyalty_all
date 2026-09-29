using dEngage.Loyalty.Api.Framework.Auth;

namespace dEngage.Loyalty.Api.Framework.Tenancy;

public sealed class TenantContext
{
    public required string CorrelationId { get; init; }
    public AuthenticatedPrincipal? Principal { get; init; }

    // The tenant the current route path segment addresses, once a module has resolved it —
    // distinct from Principal.TenantId, since a platform_admin's principal carries no tenant of its own.
    public string? RouteTenantId { get; set; }
}
