using System.Text.Json;
using dEngage.Loyalty.Api.Framework.Data;
using dEngage.Loyalty.Api.Framework.ErrorHandling;
using dEngage.Loyalty.Api.Framework.Pagination;
using dEngage.Loyalty.Api.Programs;
using dEngage.Loyalty.RuleEngine;
using dEngage.Loyalty.RuleEngine.Cache;
using dEngage.Loyalty.RuleEngine.Metadata;
using dEngage.Loyalty.RuleEngine.Models;
using dEngage.Loyalty.Schema;
using dEngage.Loyalty.Shared;
using dEngage.Loyalty.Shared.Events;
using Microsoft.EntityFrameworkCore;
using AccountTypeEntity = dEngage.Loyalty.Schema.Entities.AccountType;
using ProgramEntity = dEngage.Loyalty.Schema.Entities.Program;
using RuleEntity = dEngage.Loyalty.Schema.Entities.Rule;

namespace dEngage.Loyalty.Api.Rules;

public sealed record RuleListFilter(string? Event, string? Type, Guid? TargetAccountTypeId, string? Status, bool? Stackable);

public interface IRulesAppService
{
    Task<PagedResult<RuleResponse>> ListAsync(string tenantId, Guid programId, RuleListFilter filter, PageRequest page, CancellationToken ct);
    Task<RuleResponse> GetAsync(string tenantId, Guid programId, Guid ruleId, CancellationToken ct);
    Task<RuleResponse> CreateAsync(string tenantId, Guid programId, CreateRuleRequest request, string createdBy, CancellationToken ct);
    Task<RuleResponse> UpdateAsync(string tenantId, Guid programId, Guid ruleId, UpdateRuleRequest request, CancellationToken ct);
    Task<RuleResponse> SetStatusAsync(string tenantId, Guid programId, Guid ruleId, string status, CancellationToken ct);
    Task<RuleResponse> ApproveAsync(string tenantId, Guid programId, Guid ruleId, string approvedBy, CancellationToken ct);
    Task DeleteAsync(string tenantId, Guid programId, Guid ruleId, CancellationToken ct);
    RulesMetadataResponse GetMetadata();
}

public sealed class RulesAppService(
    IProgramChangeTracker programChanges,
    IRepository<RuleEntity> repository,
    IRepository<ProgramEntity> programRepository,
    IRepository<AccountTypeEntity> accountTypeRepository,
    IRuleCacheService ruleCacheService,
    IRuleVersioningService versioning,
    ITenantSlugResolver tenantSlugResolver) : IRulesAppService
{
    private static readonly JsonSerializerOptions DslOptions = new(); // no camelCase — respects each model's own [JsonPropertyName]

    public async Task<PagedResult<RuleResponse>> ListAsync(string tenantId, Guid programId, RuleListFilter filter, PageRequest page, CancellationToken ct)
    {
        await RequireProgramAsync(tenantId, programId, ct);

        var query = (await repository.Query(tenantId, ct)).Where(r => r.ProgramId == programId);
        if (filter.Event is not null) query = query.Where(r => r.Trigger == filter.Event);
        if (filter.Type is not null) query = query.Where(r => r.Type == filter.Type);
        if (filter.TargetAccountTypeId is not null) query = query.Where(r => r.TargetAccountTypeId == filter.TargetAccountTypeId);
        // A deleted rule should behave as gone unless the caller explicitly asks to see deleted
        // ones — otherwise DELETE would soft-delete but the rule keeps showing up in the list.
        query = filter.Status is not null
            ? query.Where(r => r.Status == filter.Status)
            : query.Where(r => r.Status != RuleStatus.Deleted);
        if (filter.Stackable is not null) query = query.Where(r => r.Stackable == filter.Stackable);

        query = query.OrderByDescending(r => r.Priority);
        var total = await query.CountAsync(ct);
        var entities = await query.Skip((page.Page - 1) * page.PageSize).Take(page.PageSize).ToListAsync(ct);

        return new PagedResult<RuleResponse>
        {
            Data = entities.Select(ToResponse).ToList(),
            Page = page.Page,
            PageSize = page.PageSize,
            Total = total
        };
    }

    public async Task<RuleResponse> GetAsync(string tenantId, Guid programId, Guid ruleId, CancellationToken ct) =>
        ToResponse(await Find(tenantId, programId, ruleId, ct));

    public async Task<RuleResponse> CreateAsync(string tenantId, Guid programId, CreateRuleRequest request, string createdBy, CancellationToken ct)
    {
        await RequireProgramAsync(tenantId, programId, ct);
        ValidateDsl(request.Conditions);
        var tenantGuid = await tenantSlugResolver.ResolveAsync(tenantId, ct);

        string? targetAccountKind = null;
        if (request.TargetAccountTypeId is { } targetId)
        {
            var targetAccount = await (await accountTypeRepository.Query(tenantId, ct))
                .FirstOrDefaultAsync(a => a.Id == targetId, ct)
                ?? throw new NotFoundApiException($"Account type '{targetId}'");
            targetAccountKind = targetAccount.Type;

            // CR-03: target account kind must be one this rule type is allowed to post to
            // (A3's "valid targets" column) — same single source of truth as the trigger/type
            // compatibility check in RulesValidators.
            if (!dEngage.Loyalty.RuleEngine.Metadata.RuleTypeCatalog.IsValidTarget(request.Type, targetAccountKind))
                throw new ValidationApiException(
                    $"Account type '{targetAccountKind}' is not a valid target for '{request.Type}'.");
        }

        // CR 2026-10-05: a redeem rule's Redeem into wallet must be a CASH wallet of this program.
        if (request.Calculation?.CashAccountTypeId is { } cashId)
            await RequireCashWalletAsync(tenantId, programId, cashId, ct);

        // CR-04: a CASH target requires a distinct admin's approval before the rule can match —
        // RuleCacheService only loads Active rows, so PendingApproval is inert until approved.
        // CR 2026-10-05 (D2): so does a redeem rule that pays cash, though its target is POINTS.
        var isCash = targetAccountKind == "CASH" || BurnRuleCalculationRules.RequiresCashApproval(request.Type, request.Calculation);

        var entity = new RuleEntity
        {
            Id = Guid.NewGuid(),
            TenantId = tenantGuid,
            ProgramId = programId,
            Name = request.Name,
            Type = request.Type,
            Trigger = request.Trigger,
            Conditions = request.Conditions is null ? null : JsonSerializer.Serialize(request.Conditions, DslOptions),
            Calculation = JsonSerializer.Serialize(request.Calculation ?? new RuleCalculation(), DslOptions),
            TargetAccountTypeId = request.TargetAccountTypeId,
            Limits = request.Limits is null ? null : JsonSerializer.Serialize(request.Limits, DslOptions),
            Configuration = request.Configuration is null ? null : JsonSerializer.Serialize(request.Configuration, DslOptions),
            Priority = request.Priority,
            Stackable = request.Stackable,
            // 1.3.CL item 5: named groups and multipliers are retired — exclusive rules compete
            // per target wallet (WinnerSelector's fallback) and every rule stacks additively.
            ExclusivityGroup = null,
            StackMode = RuleStackMode.Additive,
            ActiveFrom = request.ActiveFrom,
            ActiveTo = request.ActiveTo,
            Status = isCash ? RuleStatus.PendingApproval : RuleStatus.Active,
            CreatedBy = createdBy,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        await repository.AddAsync(entity, ct);
        await programChanges.MarkChangedAsync(programId, ct);
        await repository.SaveChangesAsync(ct);
        await ruleCacheService.LoadFromDbAsync(tenantId, programId, ct);

        return ToResponse(entity);
    }

    // CR-04: only a different admin than the creator may approve a CASH-targeted rule
    // (identity comparison, not a distinct role — see plan's Scope decision #4: current RBAC
    // has no per-feature permission levels to gate this on instead).
    public async Task<RuleResponse> ApproveAsync(string tenantId, Guid programId, Guid ruleId, string approvedBy, CancellationToken ct)
    {
        var entity = await Find(tenantId, programId, ruleId, ct);
        await RequireNotRetiredAsync(tenantId, entity, ct);

        if (entity.Status != RuleStatus.PendingApproval)
            throw new ValidationApiException($"Rule '{ruleId}' is not pending approval.");
        if (entity.CreatedBy is not null && string.Equals(entity.CreatedBy, approvedBy, StringComparison.Ordinal))
            throw new ValidationApiException("A rule cannot be approved by the same admin who created it.");

        entity.Status = RuleStatus.Active;
        entity.ApprovedBy = approvedBy;
        entity.UpdatedAt = DateTime.UtcNow;

        await programChanges.MarkChangedAsync(programId, ct);

        await repository.SaveChangesAsync(ct);
        await ruleCacheService.LoadFromDbAsync(tenantId, programId, ct);

        return ToResponse(entity);
    }

    public async Task<RuleResponse> UpdateAsync(string tenantId, Guid programId, Guid ruleId, UpdateRuleRequest request, CancellationToken ct)
    {
        var entity = await Find(tenantId, programId, ruleId, ct);
        await RequireNotRetiredAsync(tenantId, entity, ct);

        var conditions = request.Conditions ?? FlatConditionsMigrator.ParseConditions(entity.Conditions);
        ValidateDsl(conditions);

        // CR 2026-10-05: the update validator can't see the rule's type, so the redeem / transfer
        // calculation is checked here, against the same list as on create.
        var oldCalculation = JsonSerializer.Deserialize<RuleCalculation>(entity.Calculation, DslOptions) ?? new RuleCalculation();
        var newCalculation = request.Calculation ?? oldCalculation;
        if (request.Calculation is not null)
        {
            var errors = BurnRuleCalculationRules.Errors(entity.Type, request.Calculation);
            if (errors.Count > 0)
                throw new ValidationApiException(string.Join(" ", errors));
            if (request.Calculation.CashAccountTypeId is { } cashId)
                await RequireCashWalletAsync(tenantId, programId, cashId, ct);
        }

        // CR-09 (A10 guarantee #7): archive the PRE-edit state as the version it's currently at,
        // then bump — must happen before any field below is mutated.
        await versioning.ArchiveAndBumpAsync(entity.TenantId, entity, ct);

        if (request.Name is not null) entity.Name = request.Name;
        if (request.Trigger is not null) entity.Trigger = request.Trigger;
        if (request.TargetAccountTypeId is not null && request.TargetAccountTypeId != entity.TargetAccountTypeId)
        {
            var newTargetAccount = await (await accountTypeRepository.Query(tenantId, ct))
                .FirstOrDefaultAsync(a => a.Id == request.TargetAccountTypeId.Value, ct)
                ?? throw new NotFoundApiException($"Account type '{request.TargetAccountTypeId}'");

            // CR-03: same target-kind compatibility check as CreateAsync.
            if (!dEngage.Loyalty.RuleEngine.Metadata.RuleTypeCatalog.IsValidTarget(entity.Type, newTargetAccount.Type))
                throw new ValidationApiException(
                    $"Account type '{newTargetAccount.Type}' is not a valid target for '{entity.Type}'.");

            entity.TargetAccountTypeId = request.TargetAccountTypeId.Value;

            // CR-04: recheck the CASH approval gate — retargeting an Active rule onto a CASH
            // account must not skip approval, and retargeting a PendingApproval rule away from
            // CASH must not leave it stuck waiting on an approval it no longer needs.
            var isCash = newTargetAccount.Type == "CASH"
                         || BurnRuleCalculationRules.RequiresCashApproval(entity.Type, newCalculation);
            if (isCash && entity.Status == RuleStatus.Active)
            {
                entity.Status = RuleStatus.PendingApproval;
                entity.ApprovedBy = null;
            }
            else if (!isCash && entity.Status == RuleStatus.PendingApproval)
            {
                entity.Status = RuleStatus.Active;
            }
        }
        if (request.Calculation is not null) entity.Calculation = JsonSerializer.Serialize(request.Calculation, DslOptions);

        // CR 2026-10-05 (D2): changing what a redeem rule pays — cash per point or the cash
        // wallet — needs a second admin again, whatever the rule's status (a disabled rule is
        // re-checked when it is switched back on, see SetStatusAsync).
        if (BurnRuleCalculationRules.RequiresCashApproval(entity.Type, newCalculation)
            && (oldCalculation.Factor != newCalculation.Factor || oldCalculation.CashAccountTypeId != newCalculation.CashAccountTypeId))
        {
            entity.ApprovedBy = null;
            if (entity.Status == RuleStatus.Active) entity.Status = RuleStatus.PendingApproval;
        }
        if (request.Conditions is not null) entity.Conditions = JsonSerializer.Serialize(request.Conditions, DslOptions);
        if (request.Limits is not null) entity.Limits = JsonSerializer.Serialize(request.Limits, DslOptions);
        if (request.Configuration is not null) entity.Configuration = JsonSerializer.Serialize(request.Configuration, DslOptions);
        if (request.Priority is not null) entity.Priority = request.Priority.Value;
        if (request.Stackable is not null) entity.Stackable = request.Stackable.Value;
        // 1.3.CL item 5: the validator rejects any group / non-Additive mode; normalise so an
        // edit also clears whatever a pre-retirement row still held.
        entity.ExclusivityGroup = null;
        entity.StackMode = RuleStackMode.Additive;
        if (request.ActiveFrom is not null) entity.ActiveFrom = request.ActiveFrom;
        if (request.ActiveTo is not null) entity.ActiveTo = request.ActiveTo;
        entity.UpdatedAt = DateTime.UtcNow;

        await programChanges.MarkChangedAsync(programId, ct);

        await repository.SaveChangesAsync(ct);
        await ruleCacheService.LoadFromDbAsync(tenantId, programId, ct);

        return ToResponse(entity);
    }

    public async Task<RuleResponse> SetStatusAsync(string tenantId, Guid programId, Guid ruleId, string status, CancellationToken ct)
    {
        var entity = await Find(tenantId, programId, ruleId, ct);

        // CR-04: a PendingApproval CASH rule must go through ApproveAsync's distinct-admin
        // check — otherwise the creator could self-activate it through this generic endpoint
        // and the approval gate would be meaningless.
        if (entity.Status == RuleStatus.PendingApproval)
            throw new ValidationApiException($"Rule '{ruleId}' is pending approval — use POST .../approve, not status.");

        // Disabling or deleting a retired rule stays allowed; only switching it back on is refused.
        if (status == RuleStatus.Active)
        {
            await RequireNotRetiredAsync(tenantId, entity, ct);

            // CR 2026-10-05 (R-O11): redeem / transfer rules disabled at deploy were saved before
            // their fields existed — they must be completed (an edit) before they can run.
            var calculation = JsonSerializer.Deserialize<RuleCalculation>(entity.Calculation, DslOptions);
            var errors = BurnRuleCalculationRules.Errors(entity.Type, calculation);
            if (errors.Count > 0)
                throw new ValidationApiException("Complete the rule before switching it on: " + string.Join(" ", errors));

            // D2: a redeem rule that pays cash and was never approved (or was edited since) goes
            // to a second admin instead of straight to Active.
            if (BurnRuleCalculationRules.RequiresCashApproval(entity.Type, calculation) && entity.ApprovedBy is null)
                status = RuleStatus.PendingApproval;
        }

        entity.Status = status;
        entity.UpdatedAt = DateTime.UtcNow;
        await programChanges.MarkChangedAsync(programId, ct);
        await repository.SaveChangesAsync(ct);
        await ruleCacheService.LoadFromDbAsync(tenantId, programId, ct);
        return ToResponse(entity);
    }

    // NOT a plain EF Remove: the rules_soft_delete_trg DB trigger is a BEFORE DELETE trigger
    // that RETURN NULLs to cancel the physical delete (confirmed empirically — see
    // 20260702024651_RulesSoftDeleteTrigger.cs). Npgsql then correctly reports 0 rows affected
    // for that DELETE command, but EF's change tracker expects 1 (it tracked the entity as
    // removed), so SaveChangesAsync throws DbUpdateConcurrencyException. The trigger exists as a
    // defense against a raw/manual DELETE bypassing the app entirely — from here, just perform
    // the same soft-delete directly via an UPDATE, exactly like SetStatusAsync does.
    public async Task DeleteAsync(string tenantId, Guid programId, Guid ruleId, CancellationToken ct)
    {
        var entity = await Find(tenantId, programId, ruleId, ct);
        entity.Status = RuleStatus.Deleted;
        entity.UpdatedAt = DateTime.UtcNow;
        await programChanges.MarkChangedAsync(programId, ct);
        await repository.SaveChangesAsync(ct);
        await ruleCacheService.LoadFromDbAsync(tenantId, programId, ct);
    }

    // CR-03: served from the same RuleTypeCatalog/EventTypes catalogs the validators and engine
    // read — no separate metadata store to drift out of sync.
    public RulesMetadataResponse GetMetadata()
    {
        var events = EventTypes.Catalog.Values.Select(e => new EventMetadataResponse(
            e.EventType,
            e.Category.ToString(),
            e.Source.ToString(),
            e.Cardinality.ToString(),
            e.Period?.ToString(),
            e.Fields.Select(f => new EventFieldMetadata(f.Path, f.Kind.ToString())).ToList(),
            RuleTypeCatalog.CompatibleRuleTypes(e))).ToList();

        var ruleTypes = RuleTypeCatalog.Catalog.Values.Select(m => new RuleTypeMetadataResponse(
            m.RuleType,
            m.Category.ToString(),
            m.RequiredKinds.Select(k => k.ToString()).ToList(),
            m.ValidTargetAccountKinds,
            m.Note)).ToList();

        return new RulesMetadataResponse(events, ruleTypes);
    }

    private static void ValidateDsl(ConditionTree? conditions) => GroupedConditionDsl.Validate(conditions);

    // CR 2026-10-05 (D1, D15, addendum A-D4): StampRule/ExpiryRule, the points.expired and
    // birthdaybonus triggers and STAMP targets are retired. Those rules were disabled by migration and stay readable as history,
    // but can't be edited, approved or re-activated — same shape as RewardsAppService's
    // reward_type_retired.
    private async Task RequireNotRetiredAsync(string tenantId, RuleEntity entity, CancellationToken ct)
    {
        var retired = RuleTypes.IsRetired(entity.Type)
            || EventTypes.IsRetiredTrigger(entity.Trigger)
            || (entity.TargetAccountTypeId is { } targetId
                && await (await accountTypeRepository.Query(tenantId, ct))
                    .AnyAsync(a => a.Id == targetId && a.Type == nameof(AccountType.STAMP), ct));
        if (retired)
            throw new ConflictApiException("rule_type_retired",
                $"Rule '{entity.Id}' uses a retired rule type, trigger or STAMP wallet — it is kept for history and can't be edited or re-activated.");
    }

    private async Task<RuleEntity> Find(string tenantId, Guid programId, Guid ruleId, CancellationToken ct)
    {
        var entity = await repository.FindAsync(tenantId, ruleId, ct);
        if (entity is null || entity.ProgramId != programId)
            throw new NotFoundApiException($"Rule '{ruleId}'");
        return entity;
    }

    private async Task RequireCashWalletAsync(string tenantId, Guid programId, Guid accountTypeId, CancellationToken ct)
    {
        var isCashWallet = await (await accountTypeRepository.Query(tenantId, ct))
            .AnyAsync(a => a.Id == accountTypeId && a.ProgramId == programId && a.Type == "CASH", ct);
        if (!isCashWallet)
            throw new ValidationApiException(
                $"Calculation.cashAccountTypeId '{accountTypeId}' must be a CASH account type of this program.");
    }

    private async Task RequireProgramAsync(string tenantId, Guid programId, CancellationToken ct)
    {
        if (await programRepository.FindAsync(tenantId, programId, ct) is null)
            throw new NotFoundApiException($"Program '{programId}'");
    }

    private static RuleResponse ToResponse(RuleEntity r) => new(
        r.Id, r.Name, r.Type, r.Trigger, r.TargetAccountTypeId,
        JsonSerializer.Deserialize<RuleCalculation>(r.Calculation, DslOptions),
        FlatConditionsMigrator.ParseConditions(r.Conditions),
        r.Limits is null ? null : JsonSerializer.Deserialize<RuleLimits>(r.Limits, DslOptions),
        r.Priority, r.Stackable, r.ExclusivityGroup, r.StackMode,
        r.Configuration is null ? null : JsonSerializer.Deserialize<RuleSettings>(r.Configuration, DslOptions),
        r.CurrentVersion, r.ActiveFrom, r.ActiveTo,
        r.Status, r.CreatedBy, r.ApprovedBy, r.CreatedAt, r.UpdatedAt);
}
