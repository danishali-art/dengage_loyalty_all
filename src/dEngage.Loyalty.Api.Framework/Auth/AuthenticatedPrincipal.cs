namespace dEngage.Loyalty.Api.Framework.Auth;

public enum PrincipalKind
{
    AdminJwt,
    ApiKey
}

// TenantId is null only for a platform_admin JWT — every other principal is bound to exactly one tenant.
public sealed class AuthenticatedPrincipal
{
    public required PrincipalKind Kind { get; init; }
    public required string Role { get; init; }
    public string? TenantId { get; init; }
    public required string SubjectId { get; init; }

    public bool IsPlatformAdmin => Role == Roles.PlatformAdmin;

    public bool CanActOnTenant(string tenantId) =>
        IsPlatformAdmin || string.Equals(TenantId, tenantId, StringComparison.Ordinal);
}

public static class Roles
{
    public const string PlatformAdmin = "platform_admin";
    public const string TenantAdmin = "tenant_admin";
}
