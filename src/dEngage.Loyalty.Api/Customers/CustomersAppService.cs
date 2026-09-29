using System.Text.Json;
using dEngage.Loyalty.Api.Framework.ErrorHandling;
using dEngage.Loyalty.Api.Framework.Pagination;
using dEngage.Loyalty.Schema;
using dEngage.Loyalty.Schema.Entities;
using Microsoft.EntityFrameworkCore;

namespace dEngage.Loyalty.Api.Customers;

public interface ICustomersAppService
{
    Task<PagedResult<CustomerSummaryResponse>> ListAsync(string tenantId, PageRequest page, string? search, CancellationToken ct);
    Task<CustomerProfileResponse> GetProfileAsync(string tenantId, string contactKey, CancellationToken ct);
    Task<CursorPage<LedgerEntryResponse>> GetLedgerAsync(string tenantId, string contactKey, Guid? accountTypeId, string? cursor, int limit, CancellationToken ct);
    Task<IReadOnlyList<TierHistoryEntryResponse>> GetTierHistoryAsync(string tenantId, string contactKey, CancellationToken ct);

    // CR-10 (A11): the one deliberate exception to this module's read-only scope — see
    // CustomerBirthday remarks.
    Task<BirthdayResponse> RegisterBirthdayAsync(string tenantId, string contactKey, string monthDay, CancellationToken ct);
}

// Read-only throughout except RegisterBirthdayAsync — never calls the mutating
// TierEvaluationService/LedgerService, only replicates their read-side "what tier is next"
// logic (plan §7, CustomersModule note).
public sealed class CustomersAppService(LoyaltyDbContext db, ITenantSlugResolver tenantSlugResolver) : ICustomersAppService
{
    // No standalone Customer entity — a "customer" only exists implicitly via the CustomerAccount
    // rows a contact key has accrued, so the list groups by contact_key rather than querying a
    // dedicated table. Tenant-wide, same scope as the single-customer lookup below (not
    // program-scoped — one contact key can hold accounts across several programs in a tenant).
    public async Task<PagedResult<CustomerSummaryResponse>> ListAsync(string tenantId, PageRequest page, string? search, CancellationToken ct)
    {
        var tenantGuid = await tenantSlugResolver.ResolveAsync(tenantId, ct);
        var query = db.CustomerAccounts.Where(a => a.TenantId == tenantGuid);
        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(a => EF.Functions.ILike(a.ContactKey, $"%{search}%"));

        // EF Core can't translate the DTO's positional record constructor directly inside a
        // GroupBy().Select() (confirmed empirically) — project to an anonymous type first, then
        // map to the response record after materializing.
        var grouped = query
            .GroupBy(a => a.ContactKey)
            .Select(g => new { ContactKey = g.Key, AccountCount = g.Count(), LastActivityAt = g.Max(a => a.UpdatedAt) })
            .OrderByDescending(c => c.LastActivityAt);

        var total = await grouped.CountAsync(ct);
        var items = await grouped.Skip((page.Page - 1) * page.PageSize).Take(page.PageSize).ToListAsync(ct);

        return new PagedResult<CustomerSummaryResponse>
        {
            Data = items.Select(c => new CustomerSummaryResponse(c.ContactKey, c.AccountCount, c.LastActivityAt)).ToList(),
            Page = page.Page,
            PageSize = page.PageSize,
            Total = total
        };
    }

    public async Task<CustomerProfileResponse> GetProfileAsync(string tenantId, string contactKey, CancellationToken ct)
    {
        var tenantGuid = await tenantSlugResolver.ResolveAsync(tenantId, ct);
        var accounts = await db.CustomerAccounts
            .Include(a => a.AccountType)
            .Include(a => a.Tier)
            .Where(a => a.TenantId == tenantGuid && a.ContactKey == contactKey)
            .ToListAsync(ct);

        if (accounts.Count == 0)
            throw new NotFoundApiException($"Customer '{contactKey}'");

        var balances = accounts.Select(a => new AccountBalanceResponse(
            a.AccountTypeId, a.AccountType.Name, a.AccountType.Type, a.Balance, ReadExpirationDays(a.AccountType.Config)
        )).ToList();

        var qualifying = accounts.FirstOrDefault(a => a.TierId is not null);
        TierProgressResponse? tierProgress = null;

        if (qualifying is not null && qualifying.Tier is not null)
        {
            var nextTier = await db.TierDefinitions
                .Where(t => t.TenantId == tenantGuid && t.ProgramId == qualifying.Tier.ProgramId
                    && t.SortOrder > qualifying.Tier.SortOrder)
                .OrderBy(t => t.SortOrder)
                .FirstOrDefaultAsync(ct);

            tierProgress = new TierProgressResponse(
                qualifying.Tier.Name, qualifying.Tier.DisplayName,
                nextTier?.Name, nextTier?.DisplayName, nextTier?.MinPoints,
                qualifying.TierQualifyingPts, qualifying.TierPeriodStart, qualifying.TierExpiresAt);
        }

        return new CustomerProfileResponse(contactKey, balances, tierProgress);
    }

    public async Task<CursorPage<LedgerEntryResponse>> GetLedgerAsync(
        string tenantId, string contactKey, Guid? accountTypeId, string? cursor, int limit, CancellationToken ct)
    {
        var query = db.LedgerEntries.Where(l => l.TenantId == tenantId && l.ContactKey == contactKey);
        if (accountTypeId is not null)
            query = query.Where(l => l.CustomerAccount.AccountTypeId == accountTypeId);

        if (LedgerCursor.TryParse(cursor, out var createdAt, out var id))
        {
            query = query.Where(l => l.CreatedAt < createdAt || (l.CreatedAt == createdAt && l.Id.CompareTo(id) < 0));
        }

        var entities = await query
            .OrderByDescending(l => l.CreatedAt).ThenByDescending(l => l.Id)
            .Take(limit + 1)
            .Select(l => new { l.Id, l.Reason, l.Delta, l.CustomerAccount.AccountTypeId, l.Metadata, l.CreatedAt })
            .ToListAsync(ct);

        var hasMore = entities.Count > limit;
        var page = entities.Take(limit).ToList();
        var nextCursor = hasMore ? LedgerCursor.Format(page[^1].CreatedAt, page[^1].Id) : null;

        return new CursorPage<LedgerEntryResponse>
        {
            Data = page.Select(l => new LedgerEntryResponse(l.Id, l.Reason, l.Delta, l.AccountTypeId, l.Metadata, l.CreatedAt)).ToList(),
            NextCursor = nextCursor
        };
    }

    public async Task<BirthdayResponse> RegisterBirthdayAsync(string tenantId, string contactKey, string monthDay, CancellationToken ct)
    {
        var tenantGuid = await tenantSlugResolver.ResolveAsync(tenantId, ct);
        var existing = await db.CustomerBirthdays
            .FirstOrDefaultAsync(x => x.TenantId == tenantGuid && x.ContactKey == contactKey, ct);

        if (existing is null)
        {
            db.CustomerBirthdays.Add(new CustomerBirthday
            {
                Id = Guid.NewGuid(),
                TenantId = tenantGuid,
                ContactKey = contactKey,
                MonthDay = monthDay,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
        }
        else
        {
            existing.MonthDay = monthDay;
            existing.UpdatedAt = DateTime.UtcNow;
        }

        await db.SaveChangesAsync(ct);
        return new BirthdayResponse(contactKey, monthDay);
    }

    public async Task<IReadOnlyList<TierHistoryEntryResponse>> GetTierHistoryAsync(string tenantId, string contactKey, CancellationToken ct)
    {
        var tenantGuid = await tenantSlugResolver.ResolveAsync(tenantId, ct);
        return await db.TierUpgradeLogs
            .Include(t => t.FromTier)
            .Include(t => t.ToTier)
            .Where(t => t.TenantId == tenantGuid && t.ContactKey == contactKey)
            .OrderByDescending(t => t.CreatedAt)
            .Select(t => new TierHistoryEntryResponse(t.Id, t.FromTier != null ? t.FromTier.Name : null, t.ToTier.Name, t.QualifyingPts, t.CreatedAt))
            .ToListAsync(ct);
    }

    // v1 shows the configured expiration_days rather than a precise per-account FIFO figure —
    // that calculation only exists today as a batch cross-customer job (PointsExpiringDetectorJob)
    // with no reusable single-account query (plan §7, flagged as a phase-2 refactor).
    private static int? ReadExpirationDays(string configJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(configJson);
            return doc.RootElement.TryGetProperty("expiration_days", out var v) && v.ValueKind == JsonValueKind.Number
                ? v.GetInt32() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

internal static class LedgerCursor
{
    public static string Format(DateTime createdAt, Guid id) => $"{createdAt.Ticks}_{id}";

    public static bool TryParse(string? cursor, out DateTime createdAt, out Guid id)
    {
        createdAt = default;
        id = default;
        if (string.IsNullOrEmpty(cursor)) return false;

        var parts = cursor.Split('_', 2);
        if (parts.Length != 2 || !long.TryParse(parts[0], out var ticks) || !Guid.TryParse(parts[1], out id))
            return false;

        createdAt = new DateTime(ticks, DateTimeKind.Utc);
        return true;
    }
}
