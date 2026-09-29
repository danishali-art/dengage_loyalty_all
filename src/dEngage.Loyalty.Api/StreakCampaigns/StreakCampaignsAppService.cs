using System.Text.Json;
using dEngage.Loyalty.Api.Framework.Data;
using dEngage.Loyalty.Api.Framework.ErrorHandling;
using dEngage.Loyalty.Api.Framework.Pagination;
using dEngage.Loyalty.Api.Programs;
using dEngage.Loyalty.RuleEngine.Campaigns;
using dEngage.Loyalty.RuleEngine.Campaigns.Streak;
using dEngage.Loyalty.RuleEngine.Models;
using dEngage.Loyalty.Schema;
using dEngage.Loyalty.Shared;
using Microsoft.EntityFrameworkCore;
using CampaignEntity = dEngage.Loyalty.Schema.Entities.StreakCampaign;
using ProgramEntity = dEngage.Loyalty.Schema.Entities.Program;

namespace dEngage.Loyalty.Api.StreakCampaigns;

public interface IStreakCampaignsAppService
{
    Task<PagedResult<StreakCampaignResponse>> ListAsync(string tenantId, Guid programId, StreakCampaignListFilter filter, PageRequest page, CancellationToken ct);
    Task<StreakCampaignResponse> GetAsync(string tenantId, Guid programId, Guid campaignId, CancellationToken ct);
    Task<StreakCampaignResponse> CreateAsync(string tenantId, Guid programId, CreateStreakCampaignRequest request, CancellationToken ct);
    Task<StreakCampaignResponse> UpdateAsync(string tenantId, Guid programId, Guid campaignId, UpdateStreakCampaignRequest request, CancellationToken ct);
    Task<StreakCampaignResponse> SetStatusAsync(string tenantId, Guid programId, Guid campaignId, string status, CancellationToken ct);
    Task DeleteAsync(string tenantId, Guid programId, Guid campaignId, CancellationToken ct);
}

public sealed class StreakCampaignsAppService(
    IProgramChangeTracker programChanges,
    IRepository<CampaignEntity> repository,
    IRepository<ProgramEntity> programRepository,
    ICampaignConfigCacheService campaignConfigCacheService,
    ITenantSlugResolver tenantSlugResolver) : IStreakCampaignsAppService
{
    private static readonly JsonSerializerOptions DslOptions = new(); // no camelCase — respects each model's own [JsonPropertyName]

    public async Task<PagedResult<StreakCampaignResponse>> ListAsync(string tenantId, Guid programId, StreakCampaignListFilter filter, PageRequest page, CancellationToken ct)
    {
        await RequireProgramAsync(tenantId, programId, ct);

        var query = (await repository.Query(tenantId, ct)).Where(c => c.ProgramId == programId);
        if (filter.Event is not null) query = query.Where(c => c.Trigger == filter.Event);
        // A deleted campaign should behave as gone unless the caller explicitly asks to see
        // deleted ones — same reasoning as RulesAppService.ListAsync.
        query = filter.Status is not null
            ? query.Where(c => c.Status == filter.Status)
            : query.Where(c => c.Status != RuleStatus.Deleted);

        query = query.OrderByDescending(c => c.CreatedAt);
        var total = await query.CountAsync(ct);
        var entities = await query.Skip((page.Page - 1) * page.PageSize).Take(page.PageSize).ToListAsync(ct);

        return new PagedResult<StreakCampaignResponse>
        {
            Data = entities.Select(ToResponse).ToList(),
            Page = page.Page,
            PageSize = page.PageSize,
            Total = total
        };
    }

    public async Task<StreakCampaignResponse> GetAsync(string tenantId, Guid programId, Guid campaignId, CancellationToken ct) =>
        ToResponse(await Find(tenantId, programId, campaignId, ct));

    public async Task<StreakCampaignResponse> CreateAsync(string tenantId, Guid programId, CreateStreakCampaignRequest request, CancellationToken ct)
    {
        await RequireProgramAsync(tenantId, programId, ct);
        ValidateDsl(request.Conditions, request.Config);
        var tenantGuid = await tenantSlugResolver.ResolveAsync(tenantId, ct);

        var entity = new CampaignEntity
        {
            Id = Guid.NewGuid(),
            TenantId = tenantGuid,
            ProgramId = programId,
            Name = request.Name,
            Trigger = request.Trigger,
            TargetAccountTypeId = request.TargetAccountTypeId,
            Conditions = request.Conditions is null ? null : JsonSerializer.Serialize(request.Conditions, DslOptions),
            Config = JsonSerializer.Serialize(request.Config, DslOptions),
            ActiveFrom = request.ActiveFrom,
            ActiveTo = request.ActiveTo,
            Status = RuleStatus.Active,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        await repository.AddAsync(entity, ct);
        await programChanges.MarkChangedAsync(programId, ct);
        await repository.SaveChangesAsync(ct);
        await campaignConfigCacheService.LoadFromDbAsync(tenantId, programId, ct);

        return ToResponse(entity);
    }

    public async Task<StreakCampaignResponse> UpdateAsync(string tenantId, Guid programId, Guid campaignId, UpdateStreakCampaignRequest request, CancellationToken ct)
    {
        var entity = await Find(tenantId, programId, campaignId, ct);

        var conditions = request.Conditions ?? (entity.Conditions is null ? null : JsonSerializer.Deserialize<List<ConditionClause>>(entity.Conditions));
        var config = request.Config ?? StreakConfig.Parse(entity.Config);
        ValidateDsl(conditions, config);

        if (request.Name is not null) entity.Name = request.Name;
        if (request.Trigger is not null) entity.Trigger = request.Trigger;
        if (request.TargetAccountTypeId is not null) entity.TargetAccountTypeId = request.TargetAccountTypeId.Value;
        if (request.Conditions is not null) entity.Conditions = JsonSerializer.Serialize(request.Conditions, DslOptions);
        if (request.Config is not null) entity.Config = JsonSerializer.Serialize(request.Config, DslOptions);
        if (request.ActiveFrom is not null) entity.ActiveFrom = request.ActiveFrom;
        if (request.ActiveTo is not null) entity.ActiveTo = request.ActiveTo;
        entity.UpdatedAt = DateTime.UtcNow;

        await programChanges.MarkChangedAsync(programId, ct);

        await repository.SaveChangesAsync(ct);
        await campaignConfigCacheService.LoadFromDbAsync(tenantId, programId, ct);

        return ToResponse(entity);
    }

    public async Task<StreakCampaignResponse> SetStatusAsync(string tenantId, Guid programId, Guid campaignId, string status, CancellationToken ct)
    {
        var entity = await Find(tenantId, programId, campaignId, ct);
        entity.Status = status;
        entity.UpdatedAt = DateTime.UtcNow;
        await programChanges.MarkChangedAsync(programId, ct);
        await repository.SaveChangesAsync(ct);
        await campaignConfigCacheService.LoadFromDbAsync(tenantId, programId, ct);
        return ToResponse(entity);
    }

    // Soft-delete, same audit-preserving convention as RulesAppService.DeleteAsync — this table
    // has no soft-delete DB trigger of its own, so a plain status flip is enough here (no trigger
    // to work around).
    public async Task DeleteAsync(string tenantId, Guid programId, Guid campaignId, CancellationToken ct)
    {
        var entity = await Find(tenantId, programId, campaignId, ct);
        entity.Status = RuleStatus.Deleted;
        entity.UpdatedAt = DateTime.UtcNow;
        await programChanges.MarkChangedAsync(programId, ct);
        await repository.SaveChangesAsync(ct);
        await campaignConfigCacheService.LoadFromDbAsync(tenantId, programId, ct);
    }

    private static void ValidateDsl(List<ConditionClause>? conditions, StreakConfig config)
    {
        ConditionDsl.Validate(conditions);
        config.Validate();
    }

    private async Task<CampaignEntity> Find(string tenantId, Guid programId, Guid campaignId, CancellationToken ct)
    {
        var entity = await repository.FindAsync(tenantId, campaignId, ct);
        if (entity is null || entity.ProgramId != programId)
            throw new NotFoundApiException($"Streak campaign '{campaignId}'");
        return entity;
    }

    private async Task RequireProgramAsync(string tenantId, Guid programId, CancellationToken ct)
    {
        if (await programRepository.FindAsync(tenantId, programId, ct) is null)
            throw new NotFoundApiException($"Program '{programId}'");
    }

    private static StreakCampaignResponse ToResponse(CampaignEntity c) => new(
        c.Id, c.Name, c.Trigger, c.TargetAccountTypeId,
        c.Conditions is null ? null : JsonSerializer.Deserialize<List<ConditionClause>>(c.Conditions, DslOptions),
        StreakConfig.Parse(c.Config),
        c.ActiveFrom, c.ActiveTo, c.Status, c.CreatedAt, c.UpdatedAt);
}
