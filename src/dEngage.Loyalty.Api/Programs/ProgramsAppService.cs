using System.Globalization;
using System.Text.Json;
using dEngage.Loyalty.Api.ConfigVersions;
using dEngage.Loyalty.Api.Framework.Auth;
using dEngage.Loyalty.Api.Framework.Data;
using dEngage.Loyalty.Api.Framework.ErrorHandling;
using dEngage.Loyalty.Api.Framework.Pagination;
using dEngage.Loyalty.Schema;
using dEngage.Loyalty.Shared;
using Microsoft.EntityFrameworkCore;
using ProgramEntity = dEngage.Loyalty.Schema.Entities.Program;

namespace dEngage.Loyalty.Api.Programs;

public interface IProgramsAppService
{
    Task<PagedResult<ProgramResponse>> ListAsync(string tenantId, PageRequest page, CancellationToken ct);
    Task<ProgramResponse> GetAsync(string tenantId, Guid programId, CancellationToken ct);
    Task<ProgramResponse> CreateAsync(string tenantId, CreateProgramRequest request, AuthenticatedPrincipal? principal, CancellationToken ct);
    Task<ProgramResponse> UpdateAsync(string tenantId, Guid programId, UpdateProgramRequest request, AuthenticatedPrincipal? principal, CancellationToken ct);
    Task<ProgramResponse> PublishAsync(string tenantId, Guid programId, AuthenticatedPrincipal? principal, CancellationToken ct);
    Task DeleteAsync(string tenantId, Guid programId, AuthenticatedPrincipal? principal, CancellationToken ct);
}

public sealed class ProgramsAppService(
    IRepository<ProgramEntity> repository,
    LoyaltyDbContext db,
    ITenantSlugResolver tenantSlugResolver,
    IConfigVersionService configVersions) : IProgramsAppService
{
    // 1.3.CL item 9: publish snapshots get their own ConfigVersion sequence (= the publish
    // number, 1, 2, 3…) alongside the per-edit "Program" rows, which §5 f keeps.
    public const string PublicationEntityType = "ProgramPublication";

    public async Task<PagedResult<ProgramResponse>> ListAsync(string tenantId, PageRequest page, CancellationToken ct)
    {
        // Order the entity, then project — ordering AFTER Select on a projection containing
        // correlated subqueries fails to translate (confirmed empirically).
        var ordered = (await repository.Query(tenantId, ct))
            .Where(p => p.Status != ProgramStatus.Deleted)
            .OrderBy(p => p.Name);
        var total = await ordered.CountAsync(ct);
        var items = await Project(ordered).Skip((page.Page - 1) * page.PageSize).Take(page.PageSize).ToListAsync(ct);

        return new PagedResult<ProgramResponse> { Data = items, Page = page.Page, PageSize = page.PageSize, Total = total };
    }

    public async Task<ProgramResponse> GetAsync(string tenantId, Guid programId, CancellationToken ct)
    {
        var query = await repository.Query(tenantId, ct);
        return await Project(query.Where(p => p.Id == programId && p.Status != ProgramStatus.Deleted)).FirstOrDefaultAsync(ct)
            ?? throw new NotFoundApiException($"Program '{programId}'");
    }

    public async Task<ProgramResponse> CreateAsync(string tenantId, CreateProgramRequest request, AuthenticatedPrincipal? principal, CancellationToken ct)
    {
        var tenantGuid = await tenantSlugResolver.ResolveAsync(tenantId, ct);

        // A5: an explicit slug must be free; a derived one is made free.
        string slug;
        if (request.Slug is not null)
        {
            if (await SlugTakenAsync(tenantGuid, request.Slug, exceptId: null, ct))
                throw new ConflictApiException("slug_taken", $"Another program already uses the slug '{request.Slug}'.");
            slug = request.Slug;
        }
        else
        {
            slug = await FreeSlugAsync(tenantGuid, DeriveSlug(request.Name), ct);
        }

        var entity = new ProgramEntity
        {
            Id = Guid.NewGuid(),
            TenantId = tenantGuid,
            Name = request.Name,
            Slug = slug,
            DefaultRounding = request.DefaultRounding ?? RoundingDirection.Down,
            Description = request.Description,
            // 1.3.CL item 8: always a draft, and inactive until it has been published.
            Status = ProgramStatus.Inactive,
            PublicationStatus = ProgramPublicationStatus.Draft,
            CreatedAt = DateTime.UtcNow
        };

        await repository.AddAsync(entity, ct);
        await configVersions.StageAsync(db, tenantGuid, "Program", entity.Id, entity, ConfigChangeType.Created, principal, null, ct);
        await repository.SaveChangesAsync(ct);

        return await GetAsync(tenantId, entity.Id, ct);
    }

    public async Task<ProgramResponse> UpdateAsync(string tenantId, Guid programId, UpdateProgramRequest request, AuthenticatedPrincipal? principal, CancellationToken ct)
    {
        var entity = await repository.FindAsync(tenantId, programId, ct)
            ?? throw new NotFoundApiException($"Program '{programId}'");

        // 1.3.CL items 7/8: the Active toggle only works once the program has been published —
        // the engine never runs a draft.
        if (request.Status == ProgramStatus.Active && entity.PublicationStatus == ProgramPublicationStatus.Draft)
            throw new ConflictApiException("program_not_published", "Publish the program before activating it.");

        // The qualifying-account lock moved to AccountTypesAppService with the field itself
        // (1.3.CL item 1) — the validator already rejects QualifyingAccountTypeId here.
        // A5: the slug prefixes reward names that integrations send, so it is fixed once published.
        if (request.Slug is not null && request.Slug != entity.Slug)
        {
            if (entity.PublicationStatus != ProgramPublicationStatus.Draft)
                throw new ConflictApiException("slug_locked", "The slug of a published program cannot be changed.");
            if (await SlugTakenAsync(entity.TenantId, request.Slug, exceptId: entity.Id, ct))
                throw new ConflictApiException("slug_taken", $"Another program already uses the slug '{request.Slug}'.");
            entity.Slug = request.Slug;
        }

        if (request.Name is not null) entity.Name = request.Name;
        if (request.Description is not null) entity.Description = request.Description;
        // CR 2026-10-06 Phase 4: configuration — rules inherit it — so it marks a publish pending.
        var roundingChanged = request.DefaultRounding is not null && request.DefaultRounding != entity.DefaultRounding;
        if (request.DefaultRounding is not null) entity.DefaultRounding = request.DefaultRounding;
        if (request.Status is not null) entity.Status = request.Status;

        // Toggling Active/Inactive is operational, not configuration (D8) — it neither needs a
        // publish nor marks one as pending. Name/description edits do.
        if ((request.Name is not null || request.Description is not null || roundingChanged)
            && entity.PublicationStatus == ProgramPublicationStatus.Published)
            entity.HasUnpublishedChanges = true;

        await configVersions.StageAsync(db, entity.TenantId, "Program", entity.Id, entity, ConfigChangeType.Updated, principal, null, ct);
        await repository.SaveChangesAsync(ct);
        return await GetAsync(tenantId, programId, ct);
    }

    // 1.3.CL items 8/9: Draft → Published, and every later Publish records one aggregate
    // ConfigVersion snapshot of the program and all its nested config (§3.10). Edits stay live
    // (D1 — no staging): Publish is a version stamp, not a deploy gate for already-published
    // programs.
    public async Task<ProgramResponse> PublishAsync(string tenantId, Guid programId, AuthenticatedPrincipal? principal, CancellationToken ct)
    {
        var entity = await repository.FindAsync(tenantId, programId, ct);
        if (entity is null || entity.Status == ProgramStatus.Deleted)
            throw new NotFoundApiException($"Program '{programId}'");

        if (entity.PublicationStatus == ProgramPublicationStatus.Published && !entity.HasUnpublishedChanges)
            throw new ConflictApiException("nothing_to_publish", "There are no changes since the last publish.");

        var snapshot = await BuildSnapshotAsync(entity, ct);

        // §5 c prerequisites: something to earn into, and tiers need a wallet to qualify on.
        if (snapshot.AccountTypes.Count == 0)
            throw new ConflictApiException("publish_prerequisites_not_met", "Add at least one account type before publishing.");
        if (snapshot.Tiers.Count > 0 && !snapshot.AccountTypes.Any(a => a.IsTierQualifying))
            throw new ConflictApiException("publish_prerequisites_not_met",
                "This program has tiers — mark one POINTS account type as the tier qualifying account before publishing.");

        var summary = string.Create(CultureInfo.InvariantCulture,
            $"{snapshot.AccountTypes.Count} account types, {snapshot.Tiers.Count} tiers, {snapshot.Rewards.Count} rewards, " +
            $"{snapshot.Rules.Count} rules, {snapshot.StreakCampaigns.Count} streak campaigns");
        var version = await configVersions.StageAsync(
            db, entity.TenantId, PublicationEntityType, entity.Id, snapshot, ConfigChangeType.Published, principal, summary, ct);

        entity.PublicationStatus = ProgramPublicationStatus.Published;
        entity.HasUnpublishedChanges = false;
        entity.PublishedVersion = version;
        entity.PublishedAt = DateTime.UtcNow;
        // Same identity the ConfigVersion row records (admin email / "api-key" / "system").
        entity.PublishedBy = db.ConfigVersions.Local
            .Single(v => v.EntityType == PublicationEntityType && v.EntityId == entity.Id && v.VersionNumber == version)
            .ChangedBy;

        await repository.SaveChangesAsync(ct);
        return await GetAsync(tenantId, programId, ct);
    }

    public async Task DeleteAsync(string tenantId, Guid programId, AuthenticatedPrincipal? principal, CancellationToken ct)
    {
        var entity = await repository.FindAsync(tenantId, programId, ct)
            ?? throw new NotFoundApiException($"Program '{programId}'");

        if (entity.Status == ProgramStatus.Active)
            throw new ConflictApiException("program_running", "Program must be inactive before it can be deleted.");

        entity.Status = ProgramStatus.Deleted;
        await configVersions.StageAsync(db, entity.TenantId, "Program", entity.Id, entity, ConfigChangeType.Deleted, principal, null, ct);
        await repository.SaveChangesAsync(ct);
    }

    // Projections only (no tracked entities, no navigation properties). Deleted children are
    // left out — they are not part of what is live.
    private async Task<ProgramPublicationSnapshot> BuildSnapshotAsync(ProgramEntity program, CancellationToken ct)
    {
        var tenantId = program.TenantId;
        var programId = program.Id;

        var accountTypes = (await db.AccountTypes.AsNoTracking()
                .Where(a => a.TenantId == tenantId && a.ProgramId == programId)
                .OrderBy(a => a.Name)
                .ToListAsync(ct))
            .Select(a => new PublishedAccountType(a.Id, a.Type, a.Name, Json(a.Config), a.IsTierQualifying))
            .ToList();

        var tiers = (await db.TierDefinitions.AsNoTracking()
                .Where(t => t.TenantId == tenantId && t.ProgramId == programId && t.Status != TierStatus.Deleted)
                .OrderBy(t => t.SortOrder)
                .ToListAsync(ct))
            .Select(t => new PublishedTier(t.Id, t.Name, t.DisplayName, Money(t.MinPoints), t.QualifyingDays, t.GraceDays, t.SortOrder))
            .ToList();

        var rewards = (await db.RewardDefinitions.AsNoTracking()
                .Where(r => r.TenantId == tenantId && r.ProgramId == programId)
                .OrderBy(r => r.Name)
                .ToListAsync(ct))
            .Select(r => new PublishedReward(r.Id, r.Name, r.DisplayName, r.Acquisition, r.RewardType,
                r.PointsPrice is { } price ? Money(price) : null, r.PointsAccountTypeId, Json(r.TypeConfig), r.IsActive, r.Status))
            .ToList();

        // Card buckets are rules (Template set) and come along here.
        var rules = (await db.Rules.AsNoTracking()
                .Where(r => r.TenantId == tenantId && r.ProgramId == programId && r.Status != RuleStatus.Deleted)
                .OrderByDescending(r => r.Priority).ThenBy(r => r.Name)
                .ToListAsync(ct))
            .Select(r => new PublishedRule(r.Id, r.CurrentVersion, r.Name, r.Type, r.Trigger, r.Status, r.Template,
                r.TargetAccountTypeId, r.Priority, r.Stackable,
                JsonOrNull(r.Conditions), Json(r.Calculation), JsonOrNull(r.Limits), JsonOrNull(r.Configuration),
                r.ActiveFrom, r.ActiveTo))
            .ToList();

        var streaks = (await db.StreakCampaigns.AsNoTracking()
                .Where(s => s.TenantId == tenantId && s.ProgramId == programId && s.Status != RuleStatus.Deleted)
                .OrderBy(s => s.Name)
                .ToListAsync(ct))
            .Select(s => new PublishedStreakCampaign(s.Id, s.Name, s.Trigger, s.Status, s.TargetAccountTypeId,
                JsonOrNull(s.Conditions), Json(s.Config), s.ActiveFrom, s.ActiveTo))
            .ToList();

        return new ProgramPublicationSnapshot(
            new PublishedProgram(program.Id, program.Name, program.Description, program.Status, program.Slug, program.DefaultRounding),
            accountTypes, tiers, rewards, rules, streaks);
    }

    private static string Money(decimal value) => value.ToString(CultureInfo.InvariantCulture);
    private static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement.Clone();
    private static JsonElement? JsonOrNull(string? json) => json is null ? null : Json(json);

    private IQueryable<ProgramResponse> Project(IQueryable<ProgramEntity> source) =>
        source.Select(p => new ProgramResponse(
            // 1.3.CL item 1: both fields are deprecated on the response for one release.
            // QualifyingAccountTypeId is derived from the account type flag that replaced it;
            // WarningDays is always null (it now lives per POINTS account type).
            p.Id, p.Name, p.Description, p.Status,
            db.AccountTypes.Where(a => a.ProgramId == p.Id && a.IsTierQualifying).Select(a => (Guid?)a.Id).FirstOrDefault(),
            (int?)null, p.CreatedAt,
            db.AccountTypes.Count(a => a.ProgramId == p.Id),
            db.Rules.Count(r => r.ProgramId == p.Id && r.Status != RuleStatus.Deleted),
            p.PublicationStatus, p.HasUnpublishedChanges, p.PublishedVersion, p.PublishedAt, p.PublishedBy, p.Slug, p.DefaultRounding));

    // A5 default for a create without a slug: same rule as the migration backfill (lowercase,
    // non [a-z0-9] runs -> '-', at most 32 chars so a "-N" suffix still fits, at least 2).
    private static string DeriveSlug(string name)
    {
        var s = System.Text.RegularExpressions.Regex.Replace(name.ToLowerInvariant(), "[^a-z0-9]+", "-").Trim('-');
        if (s.Length > 32) s = s[..32].Trim('-');
        return s.Length < 2 ? $"program-{s}".Trim('-') : s;
    }

    private async Task<string> FreeSlugAsync(Guid tenantGuid, string baseSlug, CancellationToken ct)
    {
        var candidate = baseSlug;
        for (var n = 2; await SlugTakenAsync(tenantGuid, candidate, exceptId: null, ct); n++)
            candidate = $"{baseSlug}-{n}";
        return candidate;
    }

    // Deleted programs keep their slug (the unique index covers every row), so they count too.
    private Task<bool> SlugTakenAsync(Guid tenantGuid, string slug, Guid? exceptId, CancellationToken ct) =>
        db.Programs.AnyAsync(p => p.TenantId == tenantGuid && p.Slug == slug && p.Id != exceptId, ct);
}
