using System.Text.RegularExpressions;
using dEngage.Loyalty.Api.Framework.Auth;
using dEngage.Loyalty.Api.Framework.ErrorHandling;
using dEngage.Loyalty.Api.Framework.Pagination;
using dEngage.Loyalty.Schema;
using dEngage.Loyalty.Schema.Entities;
using dEngage.Loyalty.Shared;
using Microsoft.EntityFrameworkCore;

namespace dEngage.Loyalty.Api.Platform;

public interface IPlatformAppService
{
    Task<TenantResponse> CreateTenantAsync(CreateTenantRequest request, CancellationToken ct);
    Task<PagedResult<TenantResponse>> ListTenantsAsync(PageRequest page, string? search, CancellationToken ct);
    Task<TenantResponse> GetTenantAsync(string tenantId, CancellationToken ct);
    Task<TenantResponse> UpdateTenantAsync(string tenantId, UpdateTenantRequest request, CancellationToken ct);
    Task<CreateApiKeyResponse> CreateApiKeyAsync(string tenantId, CancellationToken ct);
    Task<PagedResult<ApiKeyResponse>> ListApiKeysAsync(string tenantId, PageRequest page, CancellationToken ct);
    Task RevokeApiKeyAsync(string tenantId, Guid keyId, CancellationToken ct);
    Task<AdminUserResponse> CreateAdminUserAsync(string? tenantId, CreateAdminUserRequest request, CancellationToken ct);
    Task<PagedResult<AdminUserResponse>> ListAdminUsersAsync(string tenantId, PageRequest page, CancellationToken ct);
}

// tenantId parameters/DTOs throughout are the external slug (tenants.slug) — unchanged contract.
// tenants.id (Guid) is the internal surrogate key every other table's tenant_id FK now targets;
// ITenantSlugResolver translates slug -> Guid wherever this service needs to query/write by it.
public sealed class PlatformAppService(
    LoyaltyDbContext db,
    IApiKeyGenerator apiKeyGenerator,
    IPasswordService passwordService,
    ITenantSlugResolver tenantSlugResolver)
    : IPlatformAppService
{
    private static readonly Regex TenantIdPattern = new("^[a-z0-9_]{1,50}$", RegexOptions.Compiled);

    // Same three partitions as scripts/provision_tenant.sql — this replaces that manual step for
    // tenants created through the API (the .sql file remains the documented recovery/manual path).
    // Partitions are keyed by the slug, not the new Guid id — see Tenant.cs's remarks: Postgres
    // can't retype a partition key in place and can't FK into a partitioned table anyway, so
    // ledger_entries/event_inbox/event_log deliberately stay on the slug.
    public async Task<TenantResponse> CreateTenantAsync(CreateTenantRequest request, CancellationToken ct)
    {
        if (!TenantIdPattern.IsMatch(request.Id))
            throw new ValidationApiException("Tenant id must be 1-50 lowercase letters, digits, or underscores.");

        if (await db.Tenants.AnyAsync(t => t.Slug == request.Id, ct))
            throw new ConflictApiException("tenant_already_exists", $"Tenant '{request.Id}' already exists.");

        var tenant = new Tenant { Id = Guid.NewGuid(), Slug = request.Id, Name = request.Name, Status = TenantStatus.Active, CreatedAt = DateTime.UtcNow };

        await using var tx = await db.Database.BeginTransactionAsync(ct);

        db.Tenants.Add(tenant);
        await db.SaveChangesAsync(ct);

        foreach (var table in new[] { "ledger_entries", "event_inbox", "event_log" })
        {
            // DDL cannot take bind parameters at all in Postgres — not just for identifiers, but
            // for the partition bound literal too (confirmed empirically: Npgsql sends a genuine
            // $1 parameter here and Postgres rejects it with "there is no parameter $1", since
            // CREATE TABLE ... FOR VALUES IN (...) is parsed outside the extended query protocol's
            // parameter support). The only available defense is the strict TenantIdPattern check
            // above, applied before this string is built.
#pragma warning disable EF1002
            await db.Database.ExecuteSqlRawAsync(
                $"CREATE TABLE IF NOT EXISTS {table}_{tenant.Slug} PARTITION OF {table} FOR VALUES IN ('{tenant.Slug}')", ct);
#pragma warning restore EF1002
        }

        await tx.CommitAsync(ct);
        tenantSlugResolver.Seed(tenant.Slug, tenant.Id);
        return ToResponse(tenant);
    }

    public async Task<PagedResult<TenantResponse>> ListTenantsAsync(PageRequest page, string? search, CancellationToken ct)
    {
        var query = db.Tenants.AsQueryable();
        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(t => EF.Functions.ILike(t.Name, $"%{search}%") || EF.Functions.ILike(t.Slug, $"%{search}%"));

        query = query.OrderBy(t => t.Slug);
        var total = await query.CountAsync(ct);
        var items = await query.Skip((page.Page - 1) * page.PageSize).Take(page.PageSize).ToListAsync(ct);

        return new PagedResult<TenantResponse>
        {
            Data = items.Select(ToResponse).ToList(),
            Page = page.Page,
            PageSize = page.PageSize,
            Total = total
        };
    }

    public async Task<TenantResponse> GetTenantAsync(string tenantId, CancellationToken ct)
    {
        var tenant = await db.Tenants.FirstOrDefaultAsync(t => t.Slug == tenantId, ct)
            ?? throw new NotFoundApiException($"Tenant '{tenantId}'");
        return ToResponse(tenant);
    }

    public async Task<TenantResponse> UpdateTenantAsync(string tenantId, UpdateTenantRequest request, CancellationToken ct)
    {
        var tenant = await db.Tenants.FirstOrDefaultAsync(t => t.Slug == tenantId, ct)
            ?? throw new NotFoundApiException($"Tenant '{tenantId}'");

        if (request.Name is not null) tenant.Name = request.Name;
        if (request.Status is not null) tenant.Status = request.Status;

        await db.SaveChangesAsync(ct);
        return ToResponse(tenant);
    }

    public async Task<CreateApiKeyResponse> CreateApiKeyAsync(string tenantId, CancellationToken ct)
    {
        var tenant = await db.Tenants.FirstOrDefaultAsync(t => t.Slug == tenantId, ct)
            ?? throw new NotFoundApiException($"Tenant '{tenantId}'");

        var generated = apiKeyGenerator.Generate(tenantId);
        var entity = new TenantApiKey
        {
            Id = Guid.NewGuid(),
            TenantId = tenant.Id,
            KeyPrefix = generated.Prefix,
            HashedKey = generated.HashedKey,
            CreatedAt = DateTime.UtcNow
        };

        db.TenantApiKeys.Add(entity);
        await db.SaveChangesAsync(ct);

        return new CreateApiKeyResponse(entity.Id, entity.KeyPrefix, generated.RawKey, entity.CreatedAt);
    }

    public async Task<PagedResult<ApiKeyResponse>> ListApiKeysAsync(string tenantId, PageRequest page, CancellationToken ct)
    {
        var tenantGuid = await tenantSlugResolver.ResolveAsync(tenantId, ct);
        var query = db.TenantApiKeys.Where(k => k.TenantId == tenantGuid).OrderByDescending(k => k.CreatedAt);
        var total = await query.CountAsync(ct);
        var items = await query.Skip((page.Page - 1) * page.PageSize).Take(page.PageSize)
            .Select(k => new ApiKeyResponse(k.Id, k.KeyPrefix, k.CreatedAt, k.LastUsedAt, k.RevokedAt))
            .ToListAsync(ct);

        return new PagedResult<ApiKeyResponse> { Data = items, Page = page.Page, PageSize = page.PageSize, Total = total };
    }

    public async Task RevokeApiKeyAsync(string tenantId, Guid keyId, CancellationToken ct)
    {
        var tenantGuid = await tenantSlugResolver.ResolveAsync(tenantId, ct);
        var key = await db.TenantApiKeys.FirstOrDefaultAsync(k => k.TenantId == tenantGuid && k.Id == keyId, ct)
            ?? throw new NotFoundApiException($"API key '{keyId}'");

        key.RevokedAt ??= DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    public async Task<AdminUserResponse> CreateAdminUserAsync(string? tenantId, CreateAdminUserRequest request, CancellationToken ct)
    {
        if (request.Role == "tenant_admin" && tenantId is null)
            throw new ValidationApiException("tenant_admin users must be created under a tenant.");
        if (request.Role == "platform_admin" && tenantId is not null)
            throw new ValidationApiException("platform_admin users are not scoped to a tenant.");

        if (await db.AdminUsers.AnyAsync(u => u.Email == request.Email, ct))
            throw new ConflictApiException("email_already_registered", $"'{request.Email}' is already registered.");

        var tenantGuid = tenantId is null ? (Guid?)null : await tenantSlugResolver.ResolveAsync(tenantId, ct);

        var user = new AdminUser
        {
            Id = Guid.NewGuid(),
            Email = request.Email,
            PasswordHash = passwordService.Hash(request.Password),
            Role = request.Role,
            TenantId = tenantGuid,
            Status = EntityStatus.Active,
            CreatedAt = DateTime.UtcNow
        };

        db.AdminUsers.Add(user);
        await db.SaveChangesAsync(ct);

        return new AdminUserResponse(user.Id, user.Email, user.Role, tenantId, user.Status, user.CreatedAt);
    }

    public async Task<PagedResult<AdminUserResponse>> ListAdminUsersAsync(string tenantId, PageRequest page, CancellationToken ct)
    {
        var tenantGuid = await tenantSlugResolver.ResolveAsync(tenantId, ct);
        var query = db.AdminUsers.Where(u => u.TenantId == tenantGuid).OrderByDescending(u => u.CreatedAt);
        var total = await query.CountAsync(ct);
        var items = await query.Skip((page.Page - 1) * page.PageSize).Take(page.PageSize)
            .Select(u => new AdminUserResponse(u.Id, u.Email, u.Role, tenantId, u.Status, u.CreatedAt))
            .ToListAsync(ct);

        return new PagedResult<AdminUserResponse> { Data = items, Page = page.Page, PageSize = page.PageSize, Total = total };
    }

    private static TenantResponse ToResponse(Tenant t) => new(t.Slug, t.Name, t.Status, t.CreatedAt);
}
