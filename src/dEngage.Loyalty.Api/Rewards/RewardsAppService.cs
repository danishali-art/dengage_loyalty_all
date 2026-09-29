using System.Text.Json;
using dEngage.Loyalty.Api.Framework.Data;
using dEngage.Loyalty.Api.Framework.ErrorHandling;
using dEngage.Loyalty.Api.Framework.Pagination;
using dEngage.Loyalty.Api.Programs;
using dEngage.Loyalty.Schema;
using Microsoft.EntityFrameworkCore;
using ProgramEntity = dEngage.Loyalty.Schema.Entities.Program;
using RewardEntity = dEngage.Loyalty.Schema.Entities.RewardDefinition;

namespace dEngage.Loyalty.Api.Rewards;

public interface IRewardsAppService
{
    Task<PagedResult<RewardResponse>> ListAsync(string tenantId, Guid programId, PageRequest page, CancellationToken ct);
    Task<RewardResponse> CreateAsync(string tenantId, Guid programId, CreateRewardRequest request, CancellationToken ct);
    Task<RewardResponse> UpdateAsync(string tenantId, Guid programId, Guid rewardId, UpdateRewardRequest request, CancellationToken ct);
    Task<RewardResponse> SetActiveAsync(string tenantId, Guid programId, Guid rewardId, bool isActive, CancellationToken ct);
    Task DeleteAsync(string tenantId, Guid programId, Guid rewardId, CancellationToken ct);
}

public sealed class RewardsAppService(
    IProgramChangeTracker programChanges,
    IRepository<RewardEntity> repository,
    IRepository<ProgramEntity> programRepository,
    LoyaltyDbContext db,
    ITenantSlugResolver tenantSlugResolver) : IRewardsAppService
{
    public async Task<PagedResult<RewardResponse>> ListAsync(string tenantId, Guid programId, PageRequest page, CancellationToken ct)
    {
        await RequireProgramAsync(tenantId, programId, ct);

        var query = (await repository.Query(tenantId, ct)).Where(r => r.ProgramId == programId).OrderBy(r => r.Name);
        var total = await query.CountAsync(ct);
        var entities = await query.Skip((page.Page - 1) * page.PageSize).Take(page.PageSize).ToListAsync(ct);

        return new PagedResult<RewardResponse>
        {
            Data = entities.Select(ToResponse).ToList(),
            Page = page.Page,
            PageSize = page.PageSize,
            Total = total
        };
    }

    public async Task<RewardResponse> CreateAsync(string tenantId, Guid programId, CreateRewardRequest request, CancellationToken ct)
    {
        await RequireProgramAsync(tenantId, programId, ct);
        var tenantGuid = await tenantSlugResolver.ResolveAsync(tenantId, ct);

        var entity = new RewardEntity
        {
            Id = Guid.NewGuid(),
            TenantId = tenantGuid,
            ProgramId = programId,
            Name = request.Name,
            DisplayName = request.DisplayName,
            Acquisition = request.Acquisition,
            RewardType = request.RewardType,
            StampAccountTypeId = request.StampAccountTypeId,
            PointsPrice = request.PointsPrice,
            PointsAccountTypeId = request.PointsAccountTypeId,
            TypeConfig = Serialize(request.TypeConfig),
            IsActive = request.IsActive,
            CreatedAt = DateTime.UtcNow
        };

        await repository.AddAsync(entity, ct);
        await programChanges.MarkChangedAsync(programId, ct);
        await repository.SaveChangesAsync(ct);
        return ToResponse(entity);
    }

    public async Task<RewardResponse> UpdateAsync(string tenantId, Guid programId, Guid rewardId, UpdateRewardRequest request, CancellationToken ct)
    {
        var entity = await Find(tenantId, programId, rewardId, ct);

        if (request.Name is not null) entity.Name = request.Name;
        if (request.DisplayName is not null) entity.DisplayName = request.DisplayName;
        if (request.StampAccountTypeId is not null) entity.StampAccountTypeId = request.StampAccountTypeId;
        if (request.PointsPrice is not null) entity.PointsPrice = request.PointsPrice;
        if (request.PointsAccountTypeId is not null) entity.PointsAccountTypeId = request.PointsAccountTypeId;

        if (request.TypeConfig is not null)
        {
            // RewardType is immutable after creation, so the shape it implies is known here —
            // re-validate against the existing entity's RewardType, same rules as on create.
            var errors = RewardTypeRegistry.ValidateTypeConfig(entity.RewardType, request.TypeConfig.Value);
            if (errors.Count > 0)
                throw new ValidationApiException(string.Join(" ", errors));
            entity.TypeConfig = Serialize(request.TypeConfig);
        }

        await programChanges.MarkChangedAsync(programId, ct);

        await repository.SaveChangesAsync(ct);
        return ToResponse(entity);
    }

    public async Task<RewardResponse> SetActiveAsync(string tenantId, Guid programId, Guid rewardId, bool isActive, CancellationToken ct)
    {
        var entity = await Find(tenantId, programId, rewardId, ct);
        entity.IsActive = isActive;
        await programChanges.MarkChangedAsync(programId, ct);
        await repository.SaveChangesAsync(ct);
        return ToResponse(entity);
    }

    public async Task DeleteAsync(string tenantId, Guid programId, Guid rewardId, CancellationToken ct)
    {
        var entity = await Find(tenantId, programId, rewardId, ct);
        var tenantGuid = await tenantSlugResolver.ResolveAsync(tenantId, ct);

        if (await db.RewardLogs.AnyAsync(l => l.TenantId == tenantGuid && l.RewardDefinitionId == rewardId, ct))
            throw new ConflictApiException("reward_has_history", "This reward has purchase history — deactivate it instead of deleting.");

        repository.Remove(entity);
        await programChanges.MarkChangedAsync(programId, ct);
        await repository.SaveChangesAsync(ct);
    }

    private async Task<RewardEntity> Find(string tenantId, Guid programId, Guid rewardId, CancellationToken ct)
    {
        var entity = await repository.FindAsync(tenantId, rewardId, ct);
        if (entity is null || entity.ProgramId != programId)
            throw new NotFoundApiException($"Reward '{rewardId}'");
        return entity;
    }

    private async Task RequireProgramAsync(string tenantId, Guid programId, CancellationToken ct)
    {
        if (await programRepository.FindAsync(tenantId, programId, ct) is null)
            throw new NotFoundApiException($"Program '{programId}'");
    }

    private static RewardResponse ToResponse(RewardEntity r) => new(
        r.Id, r.Name, r.DisplayName, r.Acquisition, r.RewardType, r.StampAccountTypeId, r.PointsPrice,
        r.PointsAccountTypeId, JsonDocument.Parse(r.TypeConfig).RootElement, r.IsActive, r.CreatedAt);

    private static string Serialize(JsonElement? typeConfig) =>
        typeConfig is { } cfg ? cfg.GetRawText() : "{}";
}
