using dEngage.Loyalty.Api.Framework.ErrorHandling;
using dEngage.Loyalty.Api.Framework.Pagination;
using dEngage.Loyalty.Schema;
using Microsoft.EntityFrameworkCore;

namespace dEngage.Loyalty.Api.ConfigVersions;

public interface IConfigVersionsAppService
{
    Task<PagedResult<ConfigVersionSummary>> ListAsync(string tenantId, string entityType, Guid entityId, PageRequest page, CancellationToken ct);
    Task<ConfigVersionDetail> GetAsync(string tenantId, Guid versionId, CancellationToken ct);
}

public sealed class ConfigVersionsAppService(LoyaltyDbContext db, ITenantSlugResolver tenantSlugResolver) : IConfigVersionsAppService
{
    public async Task<PagedResult<ConfigVersionSummary>> ListAsync(string tenantId, string entityType, Guid entityId, PageRequest page, CancellationToken ct)
    {
        var tenantGuid = await tenantSlugResolver.ResolveAsync(tenantId, ct);
        var query = db.ConfigVersions
            .Where(v => v.TenantId == tenantGuid && v.EntityType == entityType && v.EntityId == entityId)
            .OrderByDescending(v => v.VersionNumber);

        var total = await query.CountAsync(ct);
        var items = await query
            .Skip((page.Page - 1) * page.PageSize).Take(page.PageSize)
            .Select(v => new ConfigVersionSummary(v.Id, v.EntityType, v.EntityId, v.VersionNumber, v.ChangeType, v.ChangeSummary, v.ChangedBy, v.ChangedAt))
            .ToListAsync(ct);

        return new PagedResult<ConfigVersionSummary> { Data = items, Page = page.Page, PageSize = page.PageSize, Total = total };
    }

    public async Task<ConfigVersionDetail> GetAsync(string tenantId, Guid versionId, CancellationToken ct)
    {
        var tenantGuid = await tenantSlugResolver.ResolveAsync(tenantId, ct);
        var v = await db.ConfigVersions.FirstOrDefaultAsync(x => x.TenantId == tenantGuid && x.Id == versionId, ct)
            ?? throw new NotFoundApiException($"Config version '{versionId}'");

        return new ConfigVersionDetail(v.Id, v.EntityType, v.EntityId, v.VersionNumber, v.ChangeType, v.ChangeSummary, v.ChangedBy, v.ChangedAt, v.Snapshot);
    }
}
