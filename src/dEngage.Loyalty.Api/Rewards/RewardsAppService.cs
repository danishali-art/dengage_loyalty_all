using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using dEngage.Loyalty.Api.Framework.Data;
using dEngage.Loyalty.Api.Framework.ErrorHandling;
using dEngage.Loyalty.Api.Framework.Pagination;
using dEngage.Loyalty.Api.Programs;
using dEngage.Loyalty.RuleEngine.Campaigns.Streak;
using dEngage.Loyalty.Schema;
using dEngage.Loyalty.Shared;
using Microsoft.EntityFrameworkCore;
using ProgramEntity = dEngage.Loyalty.Schema.Entities.Program;
using RewardEntity = dEngage.Loyalty.Schema.Entities.RewardDefinition;

namespace dEngage.Loyalty.Api.Rewards;

public interface IRewardsAppService
{
    Task<PagedResult<RewardResponse>> ListAsync(string tenantId, Guid programId, PageRequest page, CancellationToken ct);
    Task<RewardResponse> CreateAsync(string tenantId, Guid programId, CreateRewardRequest request, string createdBy, CancellationToken ct);
    Task<RewardResponse> UpdateAsync(string tenantId, Guid programId, Guid rewardId, UpdateRewardRequest request, CancellationToken ct);
    Task<RewardResponse> SetActiveAsync(string tenantId, Guid programId, Guid rewardId, bool isActive, CancellationToken ct);
    Task<RewardResponse> ApproveAsync(string tenantId, Guid programId, Guid rewardId, string approvedBy, CancellationToken ct);
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

    public async Task<RewardResponse> CreateAsync(string tenantId, Guid programId, CreateRewardRequest request, string createdBy, CancellationToken ct)
    {
        var program = await RequireProgramAsync(tenantId, programId, ct);
        var tenantGuid = await tenantSlugResolver.ResolveAsync(tenantId, ct);

        RequirePrefixedName(request.Name, program.Slug);
        await RequireNameFreeAsync(tenantGuid, programId, request.Name, request.IsActive, exceptId: null, ct);
        var typeConfig = request.TypeConfig ?? default;
        await ValidateTypeConfigReferencesAsync(tenantGuid, programId, request.RewardType, typeConfig, ct);

        var entity = new RewardEntity
        {
            Id = Guid.NewGuid(),
            TenantId = tenantGuid,
            ProgramId = programId,
            Name = request.Name,
            DisplayName = request.DisplayName,
            Acquisition = request.Acquisition,
            RewardType = request.RewardType,
            PointsPrice = request.PointsPrice,
            PointsAccountTypeId = request.PointsAccountTypeId,
            TypeConfig = Serialize(request.TypeConfig),
            IsActive = request.IsActive,
            // A4: cashback pays real money — a different admin must approve it before it can be
            // bought or earned (CR-04 parity). Tier upgrades move no money.
            Status = request.RewardType == RewardType.Cashback ? RewardStatus.PendingApproval : RewardStatus.Active,
            CreatedBy = createdBy,
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
        RequireNotRetired(entity);

        if (request.Name is not null && request.Name != entity.Name)
        {
            var program = await RequireProgramAsync(tenantId, programId, ct);
            RequirePrefixedName(request.Name, program.Slug);
            await RequireNameFreeAsync(entity.TenantId, programId, request.Name, entity.IsActive, exceptId: entity.Id, ct);
            entity.Name = request.Name;
        }
        if (request.DisplayName is not null) entity.DisplayName = request.DisplayName;

        // A4: anything that changes what an approved cashback pays — the amount, the wallet it
        // pays into, or its points price — sends it back for approval (the CR-04 recheck on rule
        // retargeting, applied to rewards).
        var needsReapproval = false;
        if (request.PointsPrice is not null && request.PointsPrice != entity.PointsPrice)
        {
            entity.PointsPrice = request.PointsPrice;
            needsReapproval = true;
        }
        if (request.PointsAccountTypeId is not null) entity.PointsAccountTypeId = request.PointsAccountTypeId;

        if (request.TypeConfig is not null)
        {
            // RewardType is immutable after creation, so the shape it implies is known here —
            // re-validate against the existing entity's RewardType, same rules as on create.
            var errors = RewardTypeRegistry.ValidateTypeConfig(entity.RewardType, request.TypeConfig.Value);
            if (errors.Count > 0)
                throw new ValidationApiException(string.Join(" ", errors));
            await ValidateTypeConfigReferencesAsync(entity.TenantId, programId, entity.RewardType, request.TypeConfig.Value, ct);

            if (entity.RewardType == RewardType.Cashback)
            {
                var before = JsonDocument.Parse(entity.TypeConfig).RootElement;
                var after = request.TypeConfig.Value;

                // §3.3: once approved, the currency is locked — same rule as the CASH wallet's own
                // currency (1.3.CL item 4). The wallet may still change within that currency.
                if (entity.ApprovedBy is not null && ReadString(before, "currency") != ReadString(after, "currency"))
                    throw new ConflictApiException("currency_locked", "The currency of an approved cashback reward cannot be changed.");

                if (ReadString(before, "amount") != ReadString(after, "amount")
                    || ReadString(before, "cash_account_type_id") != ReadString(after, "cash_account_type_id"))
                    needsReapproval = true;
            }
            entity.TypeConfig = Serialize(request.TypeConfig);
        }

        if (needsReapproval && entity.RewardType == RewardType.Cashback)
            entity.Status = RewardStatus.PendingApproval;

        await programChanges.MarkChangedAsync(programId, ct);
        await repository.SaveChangesAsync(ct);
        return ToResponse(entity);
    }

    public async Task<RewardResponse> SetActiveAsync(string tenantId, Guid programId, Guid rewardId, bool isActive, CancellationToken ct)
    {
        var entity = await Find(tenantId, programId, rewardId, ct);

        if (isActive && !entity.IsActive)
        {
            RequireNotRetired(entity);
            await RequireNameFreeAsync(entity.TenantId, programId, entity.Name, isActive: true, exceptId: entity.Id, ct);
        }
        if (!isActive && entity.IsActive)
            await RequireNotUsedByStreakAsync(entity, ct);

        entity.IsActive = isActive;
        await programChanges.MarkChangedAsync(programId, ct);
        await repository.SaveChangesAsync(ct);
        return ToResponse(entity);
    }

    // A4 (CR-04 parity): only moves a cashback reward out of PendingApproval, and only for a
    // different admin than the one who created it — an identity comparison, the same as
    // RulesAppService.ApproveAsync, because RBAC has no separate approver role.
    public async Task<RewardResponse> ApproveAsync(string tenantId, Guid programId, Guid rewardId, string approvedBy, CancellationToken ct)
    {
        var entity = await Find(tenantId, programId, rewardId, ct);

        if (entity.Status != RewardStatus.PendingApproval)
            throw new ValidationApiException($"Reward '{rewardId}' is not pending approval.");
        if (entity.CreatedBy is not null && string.Equals(entity.CreatedBy, approvedBy, StringComparison.Ordinal))
            throw new ValidationApiException("A reward cannot be approved by the same admin who created it.");

        entity.Status = RewardStatus.Active;
        entity.ApprovedBy = approvedBy;

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
        await RequireNotUsedByStreakAsync(entity, ct);

        repository.Remove(entity);
        await programChanges.MarkChangedAsync(programId, ct);
        await repository.SaveChangesAsync(ct);
    }

    // A1: a retired reward (retired type or acquisition, or a combination the matrix forbids) is
    // kept readable for history but can't be edited or re-activated.
    private static void RequireNotRetired(RewardEntity entity)
    {
        if (!RewardType.IsCreatable(entity.RewardType)
            || !RewardAcquisition.IsCreatable(entity.Acquisition)
            || !RewardType.IsAllowedWith(entity.RewardType, entity.Acquisition))
            throw new ConflictApiException("reward_type_retired",
                $"Rewards of type '{entity.RewardType}' acquired by '{entity.Acquisition}' are retired — they can't be edited or re-activated.");
    }

    // A5: {program slug}_{suffix}. The slug has no '_' (ProgramsValidators), so a name can only
    // ever carry one program's prefix.
    private static void RequirePrefixedName(string name, string programSlug)
    {
        if (!Regex.IsMatch(name, $"^{Regex.Escape(programSlug)}_[a-z0-9_]+$"))
            throw new ValidationApiException(
                $"reward_name_prefix_required: the reward name must start with the program slug '{programSlug}_' followed by lowercase letters, digits or '_'.");
    }

    // Friendly 409s for what the unique indexes enforce anyway: names are unique within a program,
    // and an active name is unique across the tenant (reward.purchase looks rewards up by name).
    private async Task RequireNameFreeAsync(Guid tenantGuid, Guid programId, string name, bool isActive, Guid? exceptId, CancellationToken ct)
    {
        var taken = await db.RewardDefinitions.AnyAsync(r =>
            r.TenantId == tenantGuid && r.Name == name && r.Id != exceptId &&
            (r.ProgramId == programId || (isActive && r.IsActive)), ct);
        if (taken)
            throw new ConflictApiException("reward_name_taken", $"A reward named '{name}' already exists.");
    }

    // §3.3 / §3.6: the checks the registry can't make without the DB. Tenancy contract — the
    // referenced wallet and tier must belong to this tenant AND this program.
    private async Task ValidateTypeConfigReferencesAsync(Guid tenantGuid, Guid programId, string rewardType, JsonElement cfg, CancellationToken ct)
    {
        if (rewardType == RewardType.Cashback)
        {
            if (!Guid.TryParse(ReadString(cfg, "cash_account_type_id"), out var walletId)) return; // shape error already reported
            var wallet = await db.AccountTypes.AsNoTracking().FirstOrDefaultAsync(a =>
                a.TenantId == tenantGuid && a.ProgramId == programId && a.Id == walletId, ct);
            if (wallet is null || wallet.Type != nameof(AccountType.CASH))
                throw new ValidationApiException("type_config.cash_account_type_id must be a CASH account type of this program.");

            var walletConfig = JsonDocument.Parse(wallet.Config).RootElement;
            if (ReadString(walletConfig, "currency") != ReadString(cfg, "currency"))
                throw new ValidationApiException("type_config.currency must match the currency of the selected CASH wallet.");

            var decimals = walletConfig.TryGetProperty("decimals", out var d) && d.TryGetInt32(out var n) ? n : 2;
            if (decimal.TryParse(ReadString(cfg, "amount"), NumberStyles.Number, CultureInfo.InvariantCulture, out var amount)
                && decimal.Round(amount, decimals) != amount)
                throw new ValidationApiException($"type_config.amount can have at most {decimals} decimal places for this wallet.");
        }
        else if (rewardType == RewardType.TierUpgrade)
        {
            if (!Guid.TryParse(ReadString(cfg, "target_tier_id"), out var tierId)) return; // shape error already reported
            var tierExists = await db.TierDefinitions.AnyAsync(t =>
                t.TenantId == tenantGuid && t.ProgramId == programId && t.Id == tierId && t.Status != TierStatus.Deleted, ct);
            if (!tierExists)
                throw new ValidationApiException("type_config.target_tier_id must be a tier of this program.");

            // The upgrade is applied to the customer's tier-qualifying wallet — without one the
            // program has no tier to move.
            if (!await db.AccountTypes.AnyAsync(a => a.TenantId == tenantGuid && a.ProgramId == programId && a.IsTierQualifying, ct))
                throw new ConflictApiException("no_tier_qualifying_account",
                    "This program has no tier-qualifying account type, so a tier upgrade can't be applied.");
        }
    }

    // §3.7: a streak campaign that still references this reward would complete and grant nothing.
    private async Task RequireNotUsedByStreakAsync(RewardEntity reward, CancellationToken ct)
    {
        var campaigns = await db.StreakCampaigns.AsNoTracking()
            .Where(c => c.TenantId == reward.TenantId && c.ProgramId == reward.ProgramId && c.Status == RuleStatus.Active)
            .Select(c => new { c.Name, c.Config })
            .ToListAsync(ct);

        var user = campaigns.FirstOrDefault(c => StreakRewardId(c.Config) == reward.Id);
        if (user is not null)
            throw new ConflictApiException("reward_in_use_by_streak",
                $"Streak campaign '{user.Name}' pays out this reward — change or disable the campaign first.");
    }

    private static Guid? StreakRewardId(string config)
    {
        try { return StreakConfig.Parse(config).Reward?.RewardDefinitionId; }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException) { return null; }
    }

    private async Task<RewardEntity> Find(string tenantId, Guid programId, Guid rewardId, CancellationToken ct)
    {
        var entity = await repository.FindAsync(tenantId, rewardId, ct);
        if (entity is null || entity.ProgramId != programId)
            throw new NotFoundApiException($"Reward '{rewardId}'");
        return entity;
    }

    private async Task<ProgramEntity> RequireProgramAsync(string tenantId, Guid programId, CancellationToken ct) =>
        await programRepository.FindAsync(tenantId, programId, ct)
        ?? throw new NotFoundApiException($"Program '{programId}'");

    private static string? ReadString(JsonElement cfg, string field) =>
        cfg.ValueKind == JsonValueKind.Object && cfg.TryGetProperty(field, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString()
            : null;

    private static RewardResponse ToResponse(RewardEntity r) => new(
        r.Id, r.Name, r.DisplayName, r.Acquisition, r.RewardType, r.PointsPrice,
        r.PointsAccountTypeId, JsonDocument.Parse(r.TypeConfig).RootElement, r.IsActive, r.CreatedAt,
        r.Status, r.CreatedBy, r.ApprovedBy);

    private static string Serialize(JsonElement? typeConfig) =>
        typeConfig is { } cfg ? cfg.GetRawText() : "{}";
}
