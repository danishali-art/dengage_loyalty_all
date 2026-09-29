using dEngage.Loyalty.Api.ConfigVersions;
using dEngage.Loyalty.Api.Framework.Auth;
using dEngage.Loyalty.Api.Framework.Data;
using dEngage.Loyalty.Api.Framework.ErrorHandling;
using dEngage.Loyalty.Api.Framework.Pagination;
using dEngage.Loyalty.Api.Programs;
using dEngage.Loyalty.Schema;
using dEngage.Loyalty.Shared;
using Microsoft.EntityFrameworkCore;
using ProgramEntity = dEngage.Loyalty.Schema.Entities.Program;
using TierEntity = dEngage.Loyalty.Schema.Entities.TierDefinition;

namespace dEngage.Loyalty.Api.Tiers;

public interface ITiersAppService
{
    Task<PagedResult<TierResponse>> ListAsync(string tenantId, Guid programId, PageRequest page, CancellationToken ct);
    Task<TierResponse> CreateAsync(string tenantId, Guid programId, CreateTierRequest request, AuthenticatedPrincipal? principal, CancellationToken ct);
    Task<TierResponse> UpdateAsync(string tenantId, Guid programId, Guid tierId, UpdateTierRequest request, AuthenticatedPrincipal? principal, CancellationToken ct);
    Task ReorderAsync(string tenantId, Guid programId, ReorderTiersRequest request, CancellationToken ct);
    Task DeleteAsync(string tenantId, Guid programId, Guid tierId, AuthenticatedPrincipal? principal, CancellationToken ct);
}

public sealed class TiersAppService(
    IProgramChangeTracker programChanges,
    IRepository<TierEntity> repository,
    IRepository<ProgramEntity> programRepository,
    LoyaltyDbContext db,
    ITenantSlugResolver tenantSlugResolver,
    IConfigVersionService configVersions) : ITiersAppService
{
    public async Task<PagedResult<TierResponse>> ListAsync(string tenantId, Guid programId, PageRequest page, CancellationToken ct)
    {
        await RequireProgramAsync(tenantId, programId, ct);
        var tenantGuid = await tenantSlugResolver.ResolveAsync(tenantId, ct);

        var query = (await repository.Query(tenantId, ct))
            .Where(t => t.ProgramId == programId && t.Status != TierStatus.Deleted)
            .OrderBy(t => t.SortOrder);
        var total = await query.CountAsync(ct);
        var entities = await query.Skip((page.Page - 1) * page.PageSize).Take(page.PageSize).ToListAsync(ct);

        var assignedTierIds = (await db.CustomerAccounts
            .Where(a => a.TenantId == tenantGuid && a.TierId != null && entities.Select(e => e.Id).Contains(a.TierId!.Value))
            .Select(a => a.TierId!.Value)
            .Distinct()
            .ToListAsync(ct)).ToHashSet();

        return new PagedResult<TierResponse>
        {
            Data = entities.Select(t => ToResponse(t, assignedTierIds.Contains(t.Id))).ToList(),
            Page = page.Page,
            PageSize = page.PageSize,
            Total = total
        };
    }

    public async Task<TierResponse> CreateAsync(string tenantId, Guid programId, CreateTierRequest request, AuthenticatedPrincipal? principal, CancellationToken ct)
    {
        await RequireProgramAsync(tenantId, programId, ct);
        var tenantGuid = await tenantSlugResolver.ResolveAsync(tenantId, ct);

        var entity = new TierEntity
        {
            Id = Guid.NewGuid(),
            TenantId = tenantGuid,
            ProgramId = programId,
            Name = request.Name,
            DisplayName = request.DisplayName,
            MinPoints = request.MinPoints,
            QualifyingDays = request.QualifyingModel == "periodic" ? request.QualifyingPeriodDays : null,
            GraceDays = request.GraceDays,
            SortOrder = request.SortOrder,
            Status = TierStatus.Active,
            CreatedAt = DateTime.UtcNow
        };

        await repository.AddAsync(entity, ct);
        await configVersions.StageAsync(db, tenantGuid, "Tier", entity.Id, entity, ConfigChangeType.Created, principal, null, ct);
        await programChanges.MarkChangedAsync(programId, ct);
        await repository.SaveChangesAsync(ct);
        return ToResponse(entity, hasAssignedAccounts: false);
    }

    public async Task<TierResponse> UpdateAsync(string tenantId, Guid programId, Guid tierId, UpdateTierRequest request, AuthenticatedPrincipal? principal, CancellationToken ct)
    {
        var entity = await Find(tenantId, programId, tierId, ct);
        var tenantGuid = await tenantSlugResolver.ResolveAsync(tenantId, ct);
        var hasAssignedAccounts = await db.CustomerAccounts.AnyAsync(a => a.TenantId == tenantGuid && a.TierId == tierId, ct);

        // The qualifying model (lifetime vs periodic, derived from QualifyingDays being null)
        // drives every assigned customer's accumulated qualifying-balance math — switching it
        // once accounts are on this tier would invalidate that history, so it locks then.
        if (request.QualifyingModel is not null && hasAssignedAccounts)
        {
            var currentModel = entity.QualifyingDays is null ? "lifetime" : "periodic";
            if (request.QualifyingModel != currentModel)
                throw new ConflictApiException("tier_in_use",
                    "Qualifying model cannot be changed once customer accounts are assigned to this tier.");
        }

        if (request.Name is not null) entity.Name = request.Name;
        if (request.DisplayName is not null) entity.DisplayName = request.DisplayName;
        if (request.MinPoints is not null) entity.MinPoints = request.MinPoints.Value;
        if (request.GraceDays is not null) entity.GraceDays = request.GraceDays.Value;

        if (request.QualifyingModel is not null)
        {
            entity.QualifyingDays = request.QualifyingModel == "periodic"
                ? request.QualifyingPeriodDays ?? entity.QualifyingDays
                : null;
        }
        else if (request.QualifyingPeriodDays is not null && entity.QualifyingDays is not null)
        {
            entity.QualifyingDays = request.QualifyingPeriodDays;
        }

        await configVersions.StageAsync(db, entity.TenantId, "Tier", entity.Id, entity, ConfigChangeType.Updated, principal, null, ct);
        await programChanges.MarkChangedAsync(programId, ct);
        await repository.SaveChangesAsync(ct);
        return ToResponse(entity, hasAssignedAccounts);
    }

    // Two-phase: bump every tier past the max possible sort_order first, then assign final values —
    // otherwise an intermediate UPDATE could collide with the (program_id, sort_order) unique index
    // against a row not yet moved (plan §3/§7).
    public async Task ReorderAsync(string tenantId, Guid programId, ReorderTiersRequest request, CancellationToken ct)
    {
        await RequireProgramAsync(tenantId, programId, ct);

        var tiers = await (await repository.Query(tenantId, ct))
            .Where(t => t.ProgramId == programId && t.Status != TierStatus.Deleted)
            .ToListAsync(ct);
        var byId = tiers.ToDictionary(t => t.Id);

        if (request.TierIds.Count != tiers.Count || request.TierIds.Any(id => !byId.ContainsKey(id)))
            throw new ValidationApiException("tierIds must contain exactly the tiers currently in this program, once each.");

        await using var tx = await db.Database.BeginTransactionAsync(ct);

        for (var i = 0; i < request.TierIds.Count; i++)
            byId[request.TierIds[i]].SortOrder = 1000 + i;
        await programChanges.MarkChangedAsync(programId, ct);
        await db.SaveChangesAsync(ct);

        for (var i = 0; i < request.TierIds.Count; i++)
            byId[request.TierIds[i]].SortOrder = i;
        await programChanges.MarkChangedAsync(programId, ct);
        await db.SaveChangesAsync(ct);

        await tx.CommitAsync(ct);
    }

    public async Task DeleteAsync(string tenantId, Guid programId, Guid tierId, AuthenticatedPrincipal? principal, CancellationToken ct)
    {
        var entity = await Find(tenantId, programId, tierId, ct);
        var tenantGuid = await tenantSlugResolver.ResolveAsync(tenantId, ct);

        if (await db.CustomerAccounts.AnyAsync(a => a.TenantId == tenantGuid && a.TierId == tierId, ct))
            throw new ConflictApiException("tier_in_use", "This tier is referenced by existing customer accounts.");

        var program = await programRepository.FindAsync(tenantId, programId, ct);
        if (program?.Status == ProgramStatus.Active)
            throw new ConflictApiException("program_running", "Tiers cannot be deleted while the parent program is active.");

        entity.Status = TierStatus.Deleted;
        await configVersions.StageAsync(db, entity.TenantId, "Tier", entity.Id, entity, ConfigChangeType.Deleted, principal, null, ct);
        await programChanges.MarkChangedAsync(programId, ct);
        await repository.SaveChangesAsync(ct);
    }

    private async Task<TierEntity> Find(string tenantId, Guid programId, Guid tierId, CancellationToken ct)
    {
        var entity = await repository.FindAsync(tenantId, tierId, ct);
        if (entity is null || entity.ProgramId != programId)
            throw new NotFoundApiException($"Tier '{tierId}'");
        return entity;
    }

    private async Task RequireProgramAsync(string tenantId, Guid programId, CancellationToken ct)
    {
        if (await programRepository.FindAsync(tenantId, programId, ct) is null)
            throw new NotFoundApiException($"Program '{programId}'");
    }

    private static TierResponse ToResponse(TierEntity t, bool hasAssignedAccounts) => new(
        t.Id, t.Name, t.DisplayName, t.MinPoints,
        t.QualifyingDays is null ? "lifetime" : "periodic",
        t.QualifyingDays, t.GraceDays, t.SortOrder, t.CreatedAt, hasAssignedAccounts);
}
