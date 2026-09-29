using System.Text.Json;
using dEngage.Loyalty.Api.Framework.Auth;
using dEngage.Loyalty.Schema;
using dEngage.Loyalty.Schema.Entities;
using Microsoft.EntityFrameworkCore;

namespace dEngage.Loyalty.Api.ConfigVersions;

public interface IConfigVersionService
{
    // Stages an immutable snapshot row via db.Add — no SaveChanges of its own. The caller's
    // existing SaveChangesAsync persists the snapshot and the entity mutation in one
    // transaction, matching RuleFireAuditWriter's "write alongside the thing it explains"
    // convention rather than a cross-cutting SaveChanges interceptor. Returns the staged
    // version number (Publish records it as Program.PublishedVersion).
    Task<int> StageAsync(
        LoyaltyDbContext db, Guid tenantId, string entityType, Guid entityId,
        object snapshot, string changeType, AuthenticatedPrincipal? principal, string? changeSummary, CancellationToken ct);
}

public sealed class ConfigVersionService : IConfigVersionService
{
    private static readonly JsonSerializerOptions SnapshotOptions = new() { WriteIndented = false };

    public async Task<int> StageAsync(
        LoyaltyDbContext db, Guid tenantId, string entityType, Guid entityId,
        object snapshot, string changeType, AuthenticatedPrincipal? principal, string? changeSummary, CancellationToken ct)
    {
        var maxVersion = await db.ConfigVersions
            .Where(v => v.TenantId == tenantId && v.EntityType == entityType && v.EntityId == entityId)
            .Select(v => (int?)v.VersionNumber)
            .MaxAsync(ct);
        var nextVersion = (maxVersion ?? 0) + 1;

        db.ConfigVersions.Add(new ConfigVersion
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            EntityType = entityType,
            EntityId = entityId,
            VersionNumber = nextVersion,
            Snapshot = JsonSerializer.Serialize(snapshot, SnapshotOptions),
            ChangeType = changeType,
            ChangeSummary = changeSummary,
            ChangedBy = await ResolveChangedByAsync(db, principal, ct),
            ChangedAt = DateTime.UtcNow
        });
        return nextVersion;
    }

    private static async Task<string> ResolveChangedByAsync(LoyaltyDbContext db, AuthenticatedPrincipal? principal, CancellationToken ct)
    {
        if (principal is null) return "system";
        if (principal.Kind != PrincipalKind.AdminJwt) return "api-key";

        if (Guid.TryParse(principal.SubjectId, out var adminId))
        {
            var email = await db.AdminUsers.Where(u => u.Id == adminId).Select(u => u.Email).FirstOrDefaultAsync(ct);
            if (email is not null) return email;
        }

        return principal.SubjectId;
    }
}
