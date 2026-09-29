using dEngage.Loyalty.Api.Framework.Auth;
using dEngage.Loyalty.Schema.Entities;
using dEngage.Loyalty.Shared;

namespace dEngage.Loyalty.IntegrationTests.Fixtures;

// Mints bearer tokens the same way the real login flow does (via the app's own
// IJwtTokenService), so tests exercise the actual signing/claims path instead of
// hand-rolling a JWT.
public static class JwtIssuingHelper
{
    public static string IssuePlatformAdminToken(IJwtTokenService jwt, string email = "platform-admin@test.local") =>
        jwt.IssueToken(new AdminUser
        {
            Id = Guid.NewGuid(),
            Email = email,
            PasswordHash = "unused",
            Role = Roles.PlatformAdmin,
            Status = EntityStatus.Active,
            CreatedAt = DateTime.UtcNow,
            Tenant = null
        });

    public static string IssueTenantAdminToken(IJwtTokenService jwt, Tenant tenant, string email = "tenant-admin@test.local") =>
        jwt.IssueToken(new AdminUser
        {
            Id = Guid.NewGuid(),
            Email = email,
            PasswordHash = "unused",
            Role = Roles.TenantAdmin,
            TenantId = tenant.Id,
            Status = EntityStatus.Active,
            CreatedAt = DateTime.UtcNow,
            Tenant = tenant
        });
}
