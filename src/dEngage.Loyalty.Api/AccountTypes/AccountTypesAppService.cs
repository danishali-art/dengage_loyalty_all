using System.Text.Json;
using dEngage.Loyalty.Api.Framework.Data;
using dEngage.Loyalty.Api.Framework.ErrorHandling;
using dEngage.Loyalty.Api.Framework.Pagination;
using dEngage.Loyalty.Api.Programs;
using dEngage.Loyalty.RuleEngine.Cache;
using dEngage.Loyalty.Schema;
using dEngage.Loyalty.Shared;
using Microsoft.EntityFrameworkCore;
using ProgramEntity = dEngage.Loyalty.Schema.Entities.Program;
using AccountTypeEntity = dEngage.Loyalty.Schema.Entities.AccountType;

namespace dEngage.Loyalty.Api.AccountTypes;

public interface IAccountTypesAppService
{
    Task<PagedResult<AccountTypeResponse>> ListAsync(string tenantId, Guid programId, PageRequest page, CancellationToken ct);
    Task<AccountTypeResponse> GetAsync(string tenantId, Guid programId, Guid accountTypeId, CancellationToken ct);
    Task<AccountTypeResponse> CreateAsync(string tenantId, Guid programId, CreateAccountTypeRequest request, CancellationToken ct);
    Task<AccountTypeResponse> UpdateAsync(string tenantId, Guid programId, Guid accountTypeId, UpdateAccountTypeRequest request, CancellationToken ct);
}

public sealed class AccountTypesAppService(
    IProgramChangeTracker programChanges,
    IRepository<AccountTypeEntity> repository,
    IRepository<ProgramEntity> programRepository,
    AccountTypeConfigValidatorSelector configValidator,
    ITenantSlugResolver tenantSlugResolver,
    IRuleCacheService ruleCacheService) : IAccountTypesAppService
{
    public async Task<PagedResult<AccountTypeResponse>> ListAsync(string tenantId, Guid programId, PageRequest page, CancellationToken ct)
    {
        await RequireProgramAsync(tenantId, programId, ct);

        var query = (await repository.Query(tenantId, ct)).Where(a => a.ProgramId == programId).OrderBy(a => a.Name);
        var total = await query.CountAsync(ct);

        // Materialize first, then project — ToResponse parses Config (JsonDocument.Parse), which
        // EF Core cannot translate to SQL inside an IQueryable .Select().
        var entities = await query.Skip((page.Page - 1) * page.PageSize).Take(page.PageSize).ToListAsync(ct);

        return new PagedResult<AccountTypeResponse>
        {
            Data = entities.Select(ToResponse).ToList(),
            Page = page.Page,
            PageSize = page.PageSize,
            Total = total
        };
    }

    public async Task<AccountTypeResponse> GetAsync(string tenantId, Guid programId, Guid accountTypeId, CancellationToken ct)
    {
        var entity = await Find(tenantId, programId, accountTypeId, ct);
        return ToResponse(entity);
    }

    public async Task<AccountTypeResponse> CreateAsync(string tenantId, Guid programId, CreateAccountTypeRequest request, CancellationToken ct)
    {
        var program = await RequireProgramAsync(tenantId, programId, ct);

        var configJson = request.Config.GetRawText();
        configValidator.Validate(request.Type, configJson);
        var tenantGuid = await tenantSlugResolver.ResolveAsync(tenantId, ct);

        var isTierQualifying = request.IsTierQualifying ?? false;
        if (isTierQualifying)
            await EnsureCanBecomeTierQualifyingAsync(tenantId, program, request.Type, null, ct);

        var entity = new AccountTypeEntity
        {
            Id = Guid.NewGuid(),
            TenantId = tenantGuid,
            ProgramId = programId,
            Type = request.Type,
            Name = request.Name,
            Config = configJson,
            IsTierQualifying = isTierQualifying,
            CreatedAt = DateTime.UtcNow
        };

        await repository.AddAsync(entity, ct);
        await programChanges.MarkChangedAsync(programId, ct);
        await repository.SaveChangesAsync(ct);
        return ToResponse(entity);
    }

    public async Task<AccountTypeResponse> UpdateAsync(string tenantId, Guid programId, Guid accountTypeId, UpdateAccountTypeRequest request, CancellationToken ct)
    {
        var entity = await Find(tenantId, programId, accountTypeId, ct);
        var program = await RequireProgramAsync(tenantId, programId, ct);

        if (request.Name is not null) entity.Name = request.Name;

        var decimalsChanged = false;
        if (request.Config is not null)
        {
            var configJson = request.Config.Value.GetRawText();
            configValidator.Validate(entity.Type, configJson);

            // 1.3.CL item 4 (§5 e): switching a CASH wallet's currency would reinterpret every
            // balance already held in it — locked after creation, like Type.
            if (entity.Type == "CASH" && ReadString(entity.Config, "currency") != ReadString(configJson, "currency"))
                throw new ConflictApiException("currency_locked", "The currency of a CASH account type cannot be changed after creation.");

            decimalsChanged = ReadDecimals(entity.Config) != ReadDecimals(configJson);
            entity.Config = configJson;
        }

        if (request.IsTierQualifying is { } isTierQualifying && isTierQualifying != entity.IsTierQualifying)
        {
            if (isTierQualifying)
                await EnsureCanBecomeTierQualifyingAsync(tenantId, program, entity.Type, entity.Id, ct);
            else
                EnsureTierQualifyingUnlocked(program);
            entity.IsTierQualifying = isTierQualifying;
        }

        await programChanges.MarkChangedAsync(programId, ct);
        await repository.SaveChangesAsync(ct);

        // Spend rules cache their target wallet's decimals (CachedRule.TargetDecimals) — refresh
        // so a precision change takes effect without waiting for the next RuleSync reload.
        if (decimalsChanged)
            await ruleCacheService.LoadFromDbAsync(tenantId, programId, ct);

        return ToResponse(entity);
    }

    // 1.3.CL item 1: moved here from ProgramsAppService.UpdateAsync. The qualifying wallet feeds
    // tier-qualifying balance calculations for every customer already in the program — changing
    // it while the program is live would silently corrupt that history, so it locks while active.
    private async Task EnsureCanBecomeTierQualifyingAsync(
        string tenantId, ProgramEntity program, string type, Guid? selfId, CancellationToken ct)
    {
        if (type != "POINTS")
            throw new ValidationApiException("Only a POINTS account type can be the tier qualifying account.");

        EnsureTierQualifyingUnlocked(program);

        var alreadyQualifying = await (await repository.Query(tenantId, ct))
            .AnyAsync(a => a.ProgramId == program.Id && a.IsTierQualifying && a.Id != selfId, ct);
        if (alreadyQualifying)
            throw new ConflictApiException("tier_qualifying_exists",
                "This program already has a tier qualifying account type — unset it there first.");
    }

    private static void EnsureTierQualifyingUnlocked(ProgramEntity program)
    {
        if (program.Status == ProgramStatus.Active)
            throw new ConflictApiException("program_running",
                "The tier qualifying account type cannot be changed while the program is active.");
    }

    private async Task<AccountTypeEntity> Find(string tenantId, Guid programId, Guid accountTypeId, CancellationToken ct)
    {
        var entity = await repository.FindAsync(tenantId, accountTypeId, ct);
        if (entity is null || entity.ProgramId != programId)
            throw new NotFoundApiException($"Account type '{accountTypeId}'");
        return entity;
    }

    private async Task<ProgramEntity> RequireProgramAsync(string tenantId, Guid programId, CancellationToken ct) =>
        await programRepository.FindAsync(tenantId, programId, ct)
            ?? throw new NotFoundApiException($"Program '{programId}'");

    private static string? ReadString(string configJson, string key)
    {
        using var doc = JsonDocument.Parse(configJson);
        return doc.RootElement.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
    }

    private static decimal? ReadDecimals(string configJson)
    {
        using var doc = JsonDocument.Parse(configJson);
        return doc.RootElement.TryGetProperty("decimals", out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDecimal() : null;
    }

    private static AccountTypeResponse ToResponse(AccountTypeEntity a) =>
        new(a.Id, a.Type, a.Name, JsonDocument.Parse(a.Config).RootElement.Clone(), a.CreatedAt, a.IsTierQualifying);
}
