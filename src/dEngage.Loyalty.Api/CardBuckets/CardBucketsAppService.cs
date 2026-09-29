using System.Text.Json;
using dEngage.Loyalty.Api.Framework.Data;
using dEngage.Loyalty.Api.Framework.ErrorHandling;
using dEngage.Loyalty.Api.Framework.Pagination;
using dEngage.Loyalty.Api.Programs;
using dEngage.Loyalty.Api.Rules;
using dEngage.Loyalty.RuleEngine;
using dEngage.Loyalty.RuleEngine.Cache;
using dEngage.Loyalty.RuleEngine.Models;
using dEngage.Loyalty.Schema;
using dEngage.Loyalty.Shared;
using Microsoft.EntityFrameworkCore;
using ProgramEntity = dEngage.Loyalty.Schema.Entities.Program;
using RuleEntity = dEngage.Loyalty.Schema.Entities.Rule;

namespace dEngage.Loyalty.Api.CardBuckets;

public interface ICardBucketsAppService
{
    Task<PagedResult<CardBucketResponse>> ListAsync(string tenantId, Guid programId, CardBucketListFilter filter, PageRequest page, CancellationToken ct);
    Task<CardBucketResponse> GetAsync(string tenantId, Guid programId, Guid bucketId, CancellationToken ct);
    Task<CardBucketResponse> CreateAsync(string tenantId, Guid programId, CreateCardBucketRequest request, CancellationToken ct);
    Task<CardBucketResponse> UpdateAsync(string tenantId, Guid programId, Guid bucketId, UpdateCardBucketRequest request, CancellationToken ct);
    Task<CardBucketResponse> SetStatusAsync(string tenantId, Guid programId, Guid bucketId, string status, CancellationToken ct);
    Task DeleteAsync(string tenantId, Guid programId, Guid bucketId, CancellationToken ct);
}

// A Card Bucket IS a FixedBonusRule row (Trigger fixed to "card.transaction", Template =
// "card_bucket") — it carries no state of its own, unlike Streak Campaigns, so no new table.
// Multiple buckets are meant to combine on the same transaction (a premium-MCC bucket and a
// weekend bucket both paying out), so — unlike a plain Rule, which defaults non-stackable —
// buckets are always created stackable.
public sealed class CardBucketsAppService(
    IProgramChangeTracker programChanges,
    IRepository<RuleEntity> repository,
    IRepository<ProgramEntity> programRepository,
    IRuleCacheService ruleCacheService,
    IRuleVersioningService versioning,
    ITenantSlugResolver tenantSlugResolver) : ICardBucketsAppService
{
    private const string Template = "card_bucket";
    private const string Trigger = "card.transaction";
    private static readonly JsonSerializerOptions DslOptions = new();

    public async Task<PagedResult<CardBucketResponse>> ListAsync(string tenantId, Guid programId, CardBucketListFilter filter, PageRequest page, CancellationToken ct)
    {
        await RequireProgramAsync(tenantId, programId, ct);

        var query = (await repository.Query(tenantId, ct)).Where(r => r.ProgramId == programId && r.Template == Template);
        query = filter.Status is not null
            ? query.Where(r => r.Status == filter.Status)
            : query.Where(r => r.Status != RuleStatus.Deleted);

        query = query.OrderByDescending(r => r.Priority);
        var total = await query.CountAsync(ct);
        var entities = await query.Skip((page.Page - 1) * page.PageSize).Take(page.PageSize).ToListAsync(ct);

        return new PagedResult<CardBucketResponse>
        {
            Data = entities.Select(ToResponse).ToList(),
            Page = page.Page,
            PageSize = page.PageSize,
            Total = total
        };
    }

    public async Task<CardBucketResponse> GetAsync(string tenantId, Guid programId, Guid bucketId, CancellationToken ct) =>
        ToResponse(await Find(tenantId, programId, bucketId, ct));

    public async Task<CardBucketResponse> CreateAsync(string tenantId, Guid programId, CreateCardBucketRequest request, CancellationToken ct)
    {
        await RequireProgramAsync(tenantId, programId, ct);
        var conditions = BuildConditions(request);
        GroupedConditionDsl.Validate(conditions);
        var tenantGuid = await tenantSlugResolver.ResolveAsync(tenantId, ct);

        var entity = new RuleEntity
        {
            Id = Guid.NewGuid(),
            TenantId = tenantGuid,
            ProgramId = programId,
            Name = request.Name,
            Type = RuleTypes.FixedBonusRule,
            Trigger = Trigger,
            Template = Template,
            Conditions = conditions is null ? null : JsonSerializer.Serialize(conditions, DslOptions),
            Calculation = JsonSerializer.Serialize(new RuleCalculation { FixedValue = request.RewardAmount }, DslOptions),
            TargetAccountTypeId = request.TargetAccountTypeId,
            Limits = ToLimitsJson(request.PerCustomerPerDay, request.PerCustomerTotal),
            Priority = request.Priority,
            Stackable = true,
            ActiveFrom = request.ActiveFrom,
            ActiveTo = request.ActiveTo,
            Status = RuleStatus.Active,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        await repository.AddAsync(entity, ct);
        await programChanges.MarkChangedAsync(programId, ct);
        await repository.SaveChangesAsync(ct);
        await ruleCacheService.LoadFromDbAsync(tenantId, programId, ct);

        return ToResponse(entity);
    }

    public async Task<CardBucketResponse> UpdateAsync(string tenantId, Guid programId, Guid bucketId, UpdateCardBucketRequest request, CancellationToken ct)
    {
        var entity = await Find(tenantId, programId, bucketId, ct);
        var existing = CardBucketConditionMapper.FromConditions(FlatConditionsMigrator.ParseConditions(entity.Conditions));
        var existingLimits = entity.Limits is null ? null : JsonSerializer.Deserialize<RuleLimits>(entity.Limits, DslOptions);

        var conditions = CardBucketConditionMapper.ToConditions(
            request.MccCodes ?? existing.MccCodes,
            request.AmountMin ?? existing.AmountMin,
            request.AmountMax ?? existing.AmountMax,
            request.CountryMode ?? existing.CountryMode,
            request.Countries ?? existing.Countries,
            request.RequireCaptured ?? existing.RequireCaptured,
            request.HourFrom ?? existing.HourFrom,
            request.HourTo ?? existing.HourTo,
            request.DaysOfWeek ?? existing.DaysOfWeek,
            request.AdditionalConditions ?? existing.AdditionalConditions);
        GroupedConditionDsl.Validate(conditions);

        // CR-09 (A10 guarantee #7) — see RulesAppService.UpdateAsync remarks.
        await versioning.ArchiveAndBumpAsync(entity.TenantId, entity, ct);

        if (request.Name is not null) entity.Name = request.Name;
        if (request.TargetAccountTypeId is not null) entity.TargetAccountTypeId = request.TargetAccountTypeId.Value;
        if (request.RewardAmount is not null) entity.Calculation = JsonSerializer.Serialize(new RuleCalculation { FixedValue = request.RewardAmount.Value }, DslOptions);
        entity.Conditions = conditions is null ? null : JsonSerializer.Serialize(conditions, DslOptions);
        if (request.PerCustomerPerDay is not null || request.PerCustomerTotal is not null)
            entity.Limits = ToLimitsJson(
                request.PerCustomerPerDay ?? existingLimits?.PerCustomerPerDay,
                request.PerCustomerTotal ?? existingLimits?.PerCustomerTotal);
        if (request.Priority is not null) entity.Priority = request.Priority.Value;
        if (request.ActiveFrom is not null) entity.ActiveFrom = request.ActiveFrom;
        if (request.ActiveTo is not null) entity.ActiveTo = request.ActiveTo;
        entity.UpdatedAt = DateTime.UtcNow;

        await programChanges.MarkChangedAsync(programId, ct);

        await repository.SaveChangesAsync(ct);
        await ruleCacheService.LoadFromDbAsync(tenantId, programId, ct);

        return ToResponse(entity);
    }

    public async Task<CardBucketResponse> SetStatusAsync(string tenantId, Guid programId, Guid bucketId, string status, CancellationToken ct)
    {
        var entity = await Find(tenantId, programId, bucketId, ct);
        entity.Status = status;
        entity.UpdatedAt = DateTime.UtcNow;
        await programChanges.MarkChangedAsync(programId, ct);
        await repository.SaveChangesAsync(ct);
        await ruleCacheService.LoadFromDbAsync(tenantId, programId, ct);
        return ToResponse(entity);
    }

    // Same soft-delete-via-status pattern as RulesAppService.DeleteAsync (the rules_soft_delete_trg
    // trigger applies here too, since this is the same table).
    public async Task DeleteAsync(string tenantId, Guid programId, Guid bucketId, CancellationToken ct)
    {
        var entity = await Find(tenantId, programId, bucketId, ct);
        entity.Status = RuleStatus.Deleted;
        entity.UpdatedAt = DateTime.UtcNow;
        await programChanges.MarkChangedAsync(programId, ct);
        await repository.SaveChangesAsync(ct);
        await ruleCacheService.LoadFromDbAsync(tenantId, programId, ct);
    }

    private static ConditionTree? BuildConditions(CreateCardBucketRequest r) => CardBucketConditionMapper.ToConditions(
        r.MccCodes, r.AmountMin, r.AmountMax, r.CountryMode, r.Countries, r.RequireCaptured,
        r.HourFrom, r.HourTo, r.DaysOfWeek, r.AdditionalConditions);

    private static string? ToLimitsJson(decimal? perDay, decimal? total)
    {
        var limits = CardBucketConditionMapper.ToLimits(perDay, total);
        return limits is null ? null : JsonSerializer.Serialize(limits, DslOptions);
    }

    private async Task<RuleEntity> Find(string tenantId, Guid programId, Guid bucketId, CancellationToken ct)
    {
        var entity = await repository.FindAsync(tenantId, bucketId, ct);
        if (entity is null || entity.ProgramId != programId || entity.Template != Template)
            throw new NotFoundApiException($"Card bucket '{bucketId}'");
        return entity;
    }

    private async Task RequireProgramAsync(string tenantId, Guid programId, CancellationToken ct)
    {
        if (await programRepository.FindAsync(tenantId, programId, ct) is null)
            throw new NotFoundApiException($"Program '{programId}'");
    }

    private static CardBucketResponse ToResponse(RuleEntity r)
    {
        var conditions = FlatConditionsMigrator.ParseConditions(r.Conditions);
        var f = CardBucketConditionMapper.FromConditions(conditions);
        var calc = JsonSerializer.Deserialize<RuleCalculation>(r.Calculation, DslOptions);
        var limits = r.Limits is null ? null : JsonSerializer.Deserialize<RuleLimits>(r.Limits, DslOptions);

        return new CardBucketResponse(
            r.Id, r.Name, f.MccCodes, f.AmountMin, f.AmountMax, f.CountryMode, f.Countries, f.RequireCaptured,
            f.HourFrom, f.HourTo, f.DaysOfWeek, limits?.PerCustomerPerDay, limits?.PerCustomerTotal,
            // Card Buckets always create FixedBonusRule rows, which always have a target account
            // (only ReversalRule's is ever null — see Schema.Entities.Rule.TargetAccountTypeId).
            r.TargetAccountTypeId!.Value, calc?.FixedValue ?? 0, r.Priority, r.ActiveFrom, r.ActiveTo,
            f.AdditionalConditions, r.Status, r.CreatedAt, r.UpdatedAt);
    }
}
