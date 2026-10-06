using System.Linq.Expressions;
using System.Text.Json;
using dEngage.Loyalty.Api.Framework.ErrorHandling;
using dEngage.Loyalty.Api.CardBuckets;
using dEngage.Loyalty.Api.Framework.Pagination;
using dEngage.Loyalty.RuleEngine.Campaigns.Streak;
using dEngage.Loyalty.RuleEngine.Models;
using dEngage.Loyalty.RuleEngine.Processing;
using dEngage.Loyalty.Schema;
using dEngage.Loyalty.Schema.Entities;
using dEngage.Loyalty.Shared;
using Microsoft.EntityFrameworkCore;

namespace dEngage.Loyalty.Api.Customers;

public interface ICustomersAppService
{
    Task<PagedResult<CustomerSummaryResponse>> ListAsync(string tenantId, PageRequest page, string? search, CancellationToken ct);
    Task<CustomerProfileResponse> GetProfileAsync(string tenantId, string contactKey, CancellationToken ct);
    Task<CursorPage<LedgerEntryResponse>> GetLedgerAsync(string tenantId, string contactKey, LedgerFilter filter, string? cursor, int limit, CancellationToken ct);
    Task<IReadOnlyList<TierHistoryEntryResponse>> GetTierHistoryAsync(string tenantId, string contactKey, CancellationToken ct);

    // CR 2026-10-02 (Customer 360): the customer's events and the event drawer.
    Task<CursorPage<CustomerEventResponse>> GetEventsAsync(string tenantId, string contactKey, CustomerEventFilter filter, string? cursor, int limit, CancellationToken ct);
    Task<CustomerEventDetailResponse> GetEventAsync(string tenantId, string contactKey, string eventId, CancellationToken ct);

    // CR 2026-10-02 (Customer 360) P2: rules & caps, streaks, rewards.
    Task<CursorPage<CustomerRuleFireResponse>> GetRuleFiresAsync(string tenantId, string contactKey, RuleFireFilter filter, string? cursor, int limit, CancellationToken ct);
    Task<IReadOnlyList<RuleCapUsageResponse>> GetCapUsageAsync(string tenantId, string contactKey, CancellationToken ct);
    Task<IReadOnlyList<CustomerStreakResponse>> GetStreaksAsync(string tenantId, string contactKey, CancellationToken ct);
    Task<IReadOnlyList<CustomerRewardResponse>> GetRewardsAsync(string tenantId, string contactKey, CancellationToken ct);

    // CR 2026-10-02 (Customer 360) P3: card buckets, messages sent.
    Task<IReadOnlyList<CustomerCardBucketResponse>> GetCardBucketsAsync(string tenantId, string contactKey, CancellationToken ct);
    Task<CursorPage<SentMessageResponse>> GetMessagesAsync(string tenantId, string contactKey, MessageFilter filter, string? cursor, int limit, CancellationToken ct);
}

// Read-only throughout (RegisterBirthdayAsync was removed by CR 2026-10-05 addendum A) — never calls the mutating
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
            .Include(a => a.AccountType).ThenInclude(t => t.Program)
            .Include(a => a.Tier)
            .Where(a => a.TenantId == tenantGuid && a.ContactKey == contactKey)
            .ToListAsync(ct);

        if (accounts.Count == 0)
            throw new NotFoundApiException($"Customer '{contactKey}'");

        var balances = accounts.Select(a => new AccountBalanceResponse(
            a.AccountTypeId, a.AccountType.Name, a.AccountType.Type, a.Balance, ReadExpirationDays(a.AccountType.Config),
            a.AccountType.ProgramId, a.AccountType.Program.Name
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

        var summary = await LoadSummaryAsync(tenantId, tenantGuid, contactKey, accounts, ct);
        var programs = await LoadProgramOverviewsAsync(tenantId, tenantGuid, contactKey, accounts, ct);
        return new CustomerProfileResponse(contactKey, balances, tierProgress, summary, programs);
    }

    // CR 2026-10-02 P2: the customer header.
    private async Task<CustomerSummaryInfoResponse> LoadSummaryAsync(
        string tenantSlug, Guid tenantGuid, string contactKey, List<CustomerAccount> accounts, CancellationToken ct)
    {
        var firstSeen = await db.LedgerEntries
            .Where(l => l.TenantId == tenantSlug && l.ContactKey == contactKey)
            .OrderBy(l => l.CreatedAt)
            .Select(l => (DateTime?)l.CreatedAt)
            .FirstOrDefaultAsync(ct);

        var since = DateTime.UtcNow.AddDays(-7);
        var failed = await db.EventInbox.CountAsync(e =>
            e.TenantId == tenantSlug && e.ContactKey == contactKey &&
            e.Status == InboxStatus.Failed && e.ReceivedAt >= since, ct);

        // In progress: a streak the customer is tracking that has at least one period met.
        var activeStreaks = await db.StreakProgresses.CountAsync(s =>
            s.TenantId == tenantGuid && s.ContactKey == contactKey &&
            s.Status == StreakProgressStatus.Active && s.StreakCount > 0, ct);

        return new CustomerSummaryInfoResponse(firstSeen, accounts.Max(a => a.UpdatedAt), failed, activeStreaks);
    }

    // CR 2026-10-02 P2: one card per program the customer has a wallet in.
    private async Task<IReadOnlyList<ProgramOverviewResponse>> LoadProgramOverviewsAsync(
        string tenantSlug, Guid tenantGuid, string contactKey, List<CustomerAccount> accounts, CancellationToken ct)
    {
        var accountIds = accounts.Select(a => a.Id).ToList();
        var programIds = accounts.Select(a => a.AccountType.ProgramId).Distinct().ToList();

        var held = await db.HeldPostings
            .Where(h => h.TenantId == tenantGuid && h.ContactKey == contactKey && h.PostedAt == null)
            .Select(h => new { h.CustomerAccountId, h.Delta })
            .ToListAsync(ct);
        var pending = held.GroupBy(h => h.CustomerAccountId).ToDictionary(g => g.Key, g => g.Sum(h => h.Delta));

        var expiryEntries = await db.LedgerEntries
            .Where(l => l.TenantId == tenantSlug && accountIds.Contains(l.CustomerAccountId)
                && CustomerViewRules.ExpiryReasons.Contains(l.Reason))
            .Select(l => new { l.CustomerAccountId, l.Id, l.Reason, l.Delta, l.CreatedAt })
            .ToListAsync(ct);
        var entriesByAccount = expiryEntries.ToLookup(e => e.CustomerAccountId,
            e => new CustomerViewRules.ExpiryEntry(e.Id, e.Reason, e.Delta, e.CreatedAt));

        var tiers = await db.TierDefinitions.AsNoTracking()
            .Where(t => t.TenantId == tenantGuid && programIds.Contains(t.ProgramId))
            .ToListAsync(ct);

        var progress = await db.StreakProgresses.AsNoTracking()
            .Where(s => s.TenantId == tenantGuid && s.ContactKey == contactKey)
            .ToListAsync(ct);
        var campaignIds = progress.Select(s => s.CampaignId).ToList();
        var campaigns = await db.StreakCampaigns.AsNoTracking()
            .Where(c => c.TenantId == tenantGuid && campaignIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, ct);

        var now = DateTime.UtcNow;
        return programIds.Select(programId =>
        {
            var programAccounts = accounts.Where(a => a.AccountType.ProgramId == programId).ToList();
            var wallets = programAccounts.Select(a =>
            {
                var config = ReadWalletConfig(a.AccountType.Config);
                var expiring = a.AccountType.Type == "POINTS" && config.ExpirationDays is { } exp && config.WarningDays is { } warn
                    ? CustomerViewRules.ExpiringSoon(a.Balance, exp, warn, entriesByAccount[a.Id], now)
                    : null;
                return new WalletOverviewResponse(a.AccountTypeId, a.AccountType.Name, a.AccountType.Type, a.Balance,
                    config.Currency, pending.GetValueOrDefault(a.Id), expiring?.Amount, expiring?.ExpiresOn);
            }).ToList();

            var streaks = progress
                .Where(s => campaigns.TryGetValue(s.CampaignId, out var c) && c.ProgramId == programId)
                .Select(s =>
                {
                    var c = campaigns[s.CampaignId];
                    return new StreakSummaryResponse(c.Id, c.Name, s.StreakCount, ReadStreakConfig(c.Config)?.TargetPeriods ?? 0,
                        s.Completions, s.Status);
                }).ToList();

            return new ProgramOverviewResponse(programId, programAccounts[0].AccountType.Program.Name, wallets,
                TierStatus(programAccounts, tiers.Where(t => t.ProgramId == programId).ToList()), streaks);
        }).ToList();
    }

    // The program's tier-qualifying wallet carries the tier; absent when the program has no tiers.
    private static ProgramTierStatusResponse? TierStatus(List<CustomerAccount> programAccounts, List<TierDefinition> tiers)
    {
        if (tiers.Count == 0)
            return null;

        var account = programAccounts.FirstOrDefault(a => a.AccountType.IsTierQualifying)
            ?? programAccounts.FirstOrDefault(a => a.TierId is not null);
        var current = account?.TierId is { } tierId ? tiers.FirstOrDefault(t => t.Id == tierId) : null;
        var next = tiers
            .Where(t => current is null || t.SortOrder > current.SortOrder)
            .OrderBy(t => t.SortOrder)
            .FirstOrDefault();

        return new ProgramTierStatusResponse(
            current?.Name, current?.DisplayName, next?.Name, next?.DisplayName, next?.MinPoints,
            account?.TierQualifyingPts ?? 0, account?.TierPeriodStart, account?.TierExpiresAt, account?.TierLockedUntil);
    }

    private static (int? ExpirationDays, int? WarningDays, string? Currency) ReadWalletConfig(string configJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(configJson);
            var root = doc.RootElement;
            int? Int(string name) => root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var i) ? i : null;
            var currency = root.TryGetProperty("currency", out var c) && c.ValueKind == JsonValueKind.String ? c.GetString() : null;
            return (Int("expiration_days"), Int("warning_days"), currency);
        }
        catch (JsonException)
        {
            return (null, null, null);
        }
    }

    // Deserialize without StreakConfig.Validate(): a stored config that no longer validates still
    // has to be displayable.
    private static StreakConfig? ReadStreakConfig(string configJson)
    {
        try
        {
            return JsonSerializer.Deserialize<StreakConfig>(configJson);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public async Task<CursorPage<LedgerEntryResponse>> GetLedgerAsync(
        string tenantId, string contactKey, LedgerFilter filter, string? cursor, int limit, CancellationToken ct)
    {
        EnsureRange(filter.From, filter.To);
        string[]? reasons = null;
        if (filter.ReasonGroup is not null && !LedgerReasonGroups.Groups.TryGetValue(filter.ReasonGroup, out reasons))
            throw new ValidationApiException(
                $"'reasonGroup' must be one of: {string.Join(", ", LedgerReasonGroups.Groups.Keys)}.");

        var tenantGuid = await tenantSlugResolver.ResolveAsync(tenantId, ct);

        // Ledger rows carry the tenant slug (the partition key), not the Guid.
        var query = db.LedgerEntries.Where(l => l.TenantId == tenantId && l.ContactKey == contactKey);
        if (filter.AccountTypeId is { } accountTypeId)
            query = query.Where(l => l.CustomerAccount.AccountTypeId == accountTypeId);
        if (filter.ProgramId is { } programId)
            query = query.Where(l => l.CustomerAccount.AccountType.ProgramId == programId);
        if (reasons is not null)
            query = query.Where(l => reasons.Contains(l.Reason));
        if (filter.From is { } from)
            query = query.Where(l => l.CreatedAt >= from);
        if (filter.To is { } to)
            query = query.Where(l => l.CreatedAt <= to);
        if (!string.IsNullOrEmpty(filter.EventId))
            query = query.Where(l => l.SourceEventId == filter.EventId);
        if (filter.RuleId is { } ruleId)
            query = query.Where(l => l.RuleId == ruleId);

        // Counted with the filters and before the cursor, so the total is the same on every page.
        var total = await query.CountAsync(ct);

        if (LedgerCursor.TryParse(cursor, out var createdAt, out var id))
        {
            query = query.Where(l => l.CreatedAt < createdAt || (l.CreatedAt == createdAt && l.Id.CompareTo(id) < 0));
        }

        var entities = await query
            .OrderByDescending(l => l.CreatedAt).ThenByDescending(l => l.Id)
            .Take(limit + 1)
            .Select(PostingRowProjection)
            .ToListAsync(ct);

        var hasMore = entities.Count > limit;
        var page = entities.Take(limit).ToList();
        var nextCursor = hasMore ? LedgerCursor.Format(page[^1].CreatedAt, page[^1].Id) : null;
        var sources = await LoadPostingSourcesAsync(tenantGuid, tenantId, page, ct);

        return new CursorPage<LedgerEntryResponse>
        {
            Data = page.Select(l =>
            {
                var s = sources.For(l);
                return new LedgerEntryResponse(l.Id, l.Reason, l.Delta, l.AccountTypeId, l.Metadata, l.CreatedAt,
                    l.SourceEventId, sources.EventTypeOf(l.SourceEventId),
                    s.RuleId, s.RuleName, s.RuleVersion, s.CampaignId, s.CampaignName,
                    l.AccountTypeName, l.AccountTypeType, l.ProgramId, l.ProgramName);
            }).ToList(),
            NextCursor = nextCursor,
            Total = total
        };
    }

    // CR 2026-10-02 (Customer 360, D6): only events received after the CR carry contact_key
    // (no backfill), so older events are reached from Activity rows through the drawer instead.
    public async Task<CursorPage<CustomerEventResponse>> GetEventsAsync(
        string tenantId, string contactKey, CustomerEventFilter filter, string? cursor, int limit, CancellationToken ct)
    {
        EnsureRange(filter.From, filter.To);
        if (filter.Status is not null && !InboxStatuses.Contains(filter.Status))
            throw new ValidationApiException($"'status' must be one of: {string.Join(", ", InboxStatuses)}.");

        // event_inbox carries the tenant slug (the partition key), like the ledger.
        var query = db.EventInbox.Where(e => e.TenantId == tenantId && e.ContactKey == contactKey);
        if (!string.IsNullOrEmpty(filter.EventType))
            query = query.Where(e => e.EventType == filter.EventType);
        if (filter.Status is not null)
            query = query.Where(e => e.Status == filter.Status);
        if (filter.From is { } from)
            query = query.Where(e => e.ReceivedAt >= from);
        if (filter.To is { } to)
            query = query.Where(e => e.ReceivedAt <= to);

        var total = await query.CountAsync(ct);

        if (EventCursor.TryParse(cursor, out var receivedAt, out var afterEventId))
        {
            query = query.Where(e => e.ReceivedAt < receivedAt
                || (e.ReceivedAt == receivedAt && string.Compare(e.EventId, afterEventId) < 0));
        }

        var rows = await query
            .OrderByDescending(e => e.ReceivedAt).ThenByDescending(e => e.EventId)
            .Take(limit + 1)
            .Select(e => new { e.EventId, e.EventType, e.Payload, e.ReceivedAt, e.ProcessedAt, e.Status, e.Error })
            .ToListAsync(ct);

        var hasMore = rows.Count > limit;
        var page = rows.Take(limit).ToList();
        var nextCursor = hasMore ? EventCursor.Format(page[^1].ReceivedAt, page[^1].EventId) : null;

        var eventIds = page.Select(e => e.EventId).ToList();
        var postings = await db.LedgerEntries
            .Where(l => l.TenantId == tenantId && l.ContactKey == contactKey && eventIds.Contains(l.SourceEventId))
            .Select(l => new
            {
                l.SourceEventId,
                l.CustomerAccount.AccountTypeId,
                AccountTypeName = l.CustomerAccount.AccountType.Name,
                AccountTypeType = l.CustomerAccount.AccountType.Type,
                l.Delta
            })
            .ToListAsync(ct);

        // Summed in memory: SQLite (the API test host) can't aggregate decimals.
        var outcomes = postings
            .GroupBy(p => p.SourceEventId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<EventWalletOutcomeResponse>)g
                .GroupBy(p => new { p.AccountTypeId, p.AccountTypeName, p.AccountTypeType })
                .Select(w => new EventWalletOutcomeResponse(
                    w.Key.AccountTypeId, w.Key.AccountTypeName, w.Key.AccountTypeType, w.Count(), w.Sum(p => p.Delta)))
                .ToList());

        return new CursorPage<CustomerEventResponse>
        {
            Data = page.Select(e => new CustomerEventResponse(
                e.EventId, e.EventType, ReadEnvelope(e.Payload).OccurredAt, e.ReceivedAt, e.ProcessedAt, e.Status, e.Error,
                outcomes.TryGetValue(e.EventId, out var o) ? o : [])).ToList(),
            NextCursor = nextCursor,
            Total = total
        };
    }

    public async Task<CustomerEventDetailResponse> GetEventAsync(
        string tenantId, string contactKey, string eventId, CancellationToken ct)
    {
        var tenantGuid = await tenantSlugResolver.ResolveAsync(tenantId, ct);
        var inbox = await db.EventInbox.AsNoTracking()
            .FirstOrDefaultAsync(e => e.TenantId == tenantId && e.EventId == eventId, ct);

        // Another customer's postings are shown only under a real inbound event — that is a
        // transfer's counterparty. Without one the id is a scheduled job's, so the customer's own only.
        var postingQuery = db.LedgerEntries.Where(l => l.TenantId == tenantId && l.SourceEventId == eventId);
        if (inbox is null)
            postingQuery = postingQuery.Where(l => l.ContactKey == contactKey);
        var postings = await postingQuery
            .OrderBy(l => l.CreatedAt).ThenBy(l => l.Id)
            .Select(PostingRowProjection)
            .ToListAsync(ct);

        // The event must belong to this customer: recorded for them on the inbox (events received
        // after CR 2026-10-02), or posted to them. Anything else is "not found", not "forbidden",
        // so an event id from another customer reveals nothing.
        if (inbox?.ContactKey != contactKey && postings.All(p => p.ContactKey != contactKey))
            throw new NotFoundApiException($"Event '{eventId}' for customer '{contactKey}'");

        var sources = await LoadPostingSourcesAsync(tenantGuid, tenantId, postings, ct);

        var held = await db.HeldPostings.AsNoTracking()
            .Where(h => h.TenantId == tenantGuid && h.ContactKey == contactKey && h.SourceEventId == eventId)
            .OrderBy(h => h.CreatedAt)
            .ToListAsync(ct);
        var heldAccountIds = held.Select(h => h.CustomerAccountId).Distinct().ToList();
        var heldAccounts = await db.CustomerAccounts
            .Where(a => a.TenantId == tenantGuid && heldAccountIds.Contains(a.Id))
            .Select(a => new { a.Id, a.AccountTypeId, a.AccountType.Name })
            .ToDictionaryAsync(a => a.Id, ct);

        var fires = await db.RuleFireAudits.AsNoTracking()
            .Where(a => a.TenantId == tenantGuid && a.ContactKey == contactKey && a.SourceEventId == eventId)
            .OrderBy(a => a.CreatedAt)
            .ToListAsync(ct);

        var applied = await db.StreakAppliedEvents.AsNoTracking()
            .Where(s => s.TenantId == tenantGuid && s.EventId == eventId)
            .OrderBy(s => s.AppliedAt)
            .ToListAsync(ct);
        var completions = await db.StreakLogs.AsNoTracking()
            .Where(s => s.TenantId == tenantGuid && s.ContactKey == contactKey && s.SourceEventId == eventId)
            .OrderBy(s => s.CreatedAt)
            .ToListAsync(ct);

        var ruleIds = held.Select(h => h.RuleId).Concat(fires.Select(f => f.RuleId)).Distinct().ToList();
        var ruleNames = await db.Rules
            .Where(r => r.TenantId == tenantGuid && ruleIds.Contains(r.Id))
            .ToDictionaryAsync(r => r.Id, r => r.Name, ct);
        var campaignIds = applied.Select(a => a.CampaignId).Concat(completions.Select(c => c.CampaignId)).Distinct().ToList();
        var campaignNames = await db.StreakCampaigns
            .Where(c => c.TenantId == tenantGuid && campaignIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, c => c.Name, ct);

        var rewards = await db.RewardLogs.AsNoTracking()
            .Where(r => r.TenantId == tenantGuid && r.ContactKey == contactKey && r.SourceEventId == eventId)
            .OrderBy(r => r.CreatedAt)
            .ToListAsync(ct);

        // A points-driven tier change is logged under the event id; a reward-driven one under the
        // streak grant's key (StreakCampaignModule's grantKey + RewardFulfilmentService's ":tier_upgrade").
        var streakTierKeys = completions
            .Select(c => CustomerViewRules.TierUpgradeKey(CustomerViewRules.StreakGrant(c.CampaignId, contactKey, c.CompletionNo)))
            .ToList();
        var tierKeys = streakTierKeys.Append(eventId).ToList();
        var tierChanges = await db.TierUpgradeLogs
            .Where(t => t.TenantId == tenantGuid && t.ContactKey == contactKey && tierKeys.Contains(t.SourceEventId))
            .OrderBy(t => t.CreatedAt)
            .Select(t => new TierChangeResponse(
                t.Id, t.FromTier != null ? t.FromTier.Name : null, t.ToTier.Name, t.QualifyingPts, t.CreatedAt))
            .ToListAsync(ct);

        var messages = await LoadMessagesAsync(tenantGuid, contactKey, eventId, inbox, postings, streakTierKeys, ct);

        InboundEventResponse? inbound = null;
        if (inbox is not null)
        {
            var (occurredAt, data) = ReadEnvelope(inbox.Payload);
            inbound = new InboundEventResponse(inbox.EventId, inbox.EventType, occurredAt, inbox.ReceivedAt,
                inbox.ProcessedAt, inbox.Status, inbox.Error,
                data is { } d ? CustomerPayloadMasker.Mask(d) : null);
        }

        return new CustomerEventDetailResponse(
            eventId,
            inbound,
            postings.Select(p =>
            {
                var s = sources.For(p);
                return new EventPostingResponse(p.Id, p.ContactKey, p.Reason, p.Delta, p.Metadata, p.CreatedAt,
                    s.RuleId, s.RuleName, s.RuleVersion, s.CampaignId, s.CampaignName,
                    p.AccountTypeId, p.AccountTypeName, p.AccountTypeType, p.ProgramId, p.ProgramName);
            }).ToList(),
            held.Select(h =>
            {
                var account = heldAccounts.GetValueOrDefault(h.CustomerAccountId);
                return new HeldPostingResponse(h.Id, h.RuleId, ruleNames.GetValueOrDefault(h.RuleId),
                    account?.AccountTypeId ?? Guid.Empty, account?.Name ?? "", h.Reason, h.Delta, h.HoldUntil, h.PostedAt);
            }).ToList(),
            fires.Select(f => new RuleFireResponse(f.Id, f.RuleId, ruleNames.GetValueOrDefault(f.RuleId), f.RuleVersion,
                f.ResultingDelta, f.LedgerEntryId, f.ConditionsSnapshot, f.CalculationSnapshot, f.ResolutionSnapshot,
                f.CreatedAt)).ToList(),
            applied.Select(a => new StreakAppliedResponse(a.CampaignId, campaignNames.GetValueOrDefault(a.CampaignId),
                a.AppliedAt)).ToList(),
            completions.Select(c => new StreakCompletionResponse(c.CampaignId, campaignNames.GetValueOrDefault(c.CampaignId),
                c.CompletionNo, c.CompletedPeriod, c.Periods, c.RewardKind, c.RewardRef, c.CreatedAt)).ToList(),
            rewards.Select(r => new RewardLogResponse(r.Id, r.RewardName, r.RewardDefinitionId, r.Status,
                r.CompletionCount, r.CreatedAt, r.DeliveredAt)).ToList(),
            tierChanges,
            messages);
    }

    public async Task<IReadOnlyList<TierHistoryEntryResponse>> GetTierHistoryAsync(string tenantId, string contactKey, CancellationToken ct)
    {
        var tenantGuid = await tenantSlugResolver.ResolveAsync(tenantId, ct);
        var rows = await db.TierUpgradeLogs
            .Where(t => t.TenantId == tenantGuid && t.ContactKey == contactKey)
            .OrderByDescending(t => t.CreatedAt)
            .Select(t => new
            {
                t.Id, FromTierName = t.FromTier != null ? t.FromTier.Name : null, ToTierName = t.ToTier.Name,
                t.QualifyingPts, t.CreatedAt, t.ToTier.ProgramId, t.SourceEventId
            })
            .ToListAsync(ct);

        var programIds = rows.Select(r => r.ProgramId).Distinct().ToList();
        var programNames = await db.Programs
            .Where(p => p.TenantId == tenantGuid && programIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, p => p.Name, ct);

        // CR 2026-10-02 P2: program and cause are additive.
        return rows.Select(r => new TierHistoryEntryResponse(r.Id, r.FromTierName, r.ToTierName, r.QualifyingPts, r.CreatedAt,
            r.ProgramId, programNames.GetValueOrDefault(r.ProgramId), CustomerViewRules.TierChangeCause(r.SourceEventId),
            r.SourceEventId)).ToList();
    }

    // ── CR 2026-10-02 (Customer 360) P2: rules & caps, streaks, rewards ──

    public async Task<CursorPage<CustomerRuleFireResponse>> GetRuleFiresAsync(
        string tenantId, string contactKey, RuleFireFilter filter, string? cursor, int limit, CancellationToken ct)
    {
        EnsureRange(filter.From, filter.To);
        var tenantGuid = await tenantSlugResolver.ResolveAsync(tenantId, ct);

        var query = db.RuleFireAudits.AsNoTracking().Where(a => a.TenantId == tenantGuid && a.ContactKey == contactKey);
        if (filter.RuleId is { } ruleId)
            query = query.Where(a => a.RuleId == ruleId);
        if (filter.From is { } from)
            query = query.Where(a => a.CreatedAt >= from);
        if (filter.To is { } to)
            query = query.Where(a => a.CreatedAt <= to);
        var total = await query.CountAsync(ct);
        if (LedgerCursor.TryParse(cursor, out var createdAt, out var id))
            query = query.Where(a => a.CreatedAt < createdAt || (a.CreatedAt == createdAt && a.Id.CompareTo(id) < 0));

        var rows = await query
            .OrderByDescending(a => a.CreatedAt).ThenByDescending(a => a.Id)
            .Take(limit + 1)
            .ToListAsync(ct);
        var hasMore = rows.Count > limit;
        var page = rows.Take(limit).ToList();

        var ruleIds = page.Select(a => a.RuleId).Distinct().ToList();
        var ruleNames = await db.Rules
            .Where(r => r.TenantId == tenantGuid && ruleIds.Contains(r.Id))
            .ToDictionaryAsync(r => r.Id, r => r.Name, ct);
        var eventIds = page.Select(a => a.SourceEventId).Distinct().ToList();
        var eventTypes = await db.EventInbox
            .Where(e => e.TenantId == tenantId && eventIds.Contains(e.EventId))
            .ToDictionaryAsync(e => e.EventId, e => e.EventType, ct);

        return new CursorPage<CustomerRuleFireResponse>
        {
            Data = page.Select(a => new CustomerRuleFireResponse(a.Id, a.RuleId, ruleNames.GetValueOrDefault(a.RuleId),
                a.RuleVersion, a.SourceEventId, eventTypes.GetValueOrDefault(a.SourceEventId), a.ResultingDelta,
                a.LedgerEntryId, a.ConditionsSnapshot, a.CalculationSnapshot, a.ResolutionSnapshot, a.CreatedAt)).ToList(),
            NextCursor = hasMore ? LedgerCursor.Format(page[^1].CreatedAt, page[^1].Id) : null,
            Total = total
        };
    }

    // D7: every non-deleted rule with a per-customer cap in the customer's programs, with this
    // customer's usage from the ledger. Card-bucket rules are included and flagged (P3 tab).
    public async Task<IReadOnlyList<RuleCapUsageResponse>> GetCapUsageAsync(string tenantId, string contactKey, CancellationToken ct)
    {
        var tenantGuid = await tenantSlugResolver.ResolveAsync(tenantId, ct);
        var programIds = await db.CustomerAccounts
            .Where(a => a.TenantId == tenantGuid && a.ContactKey == contactKey)
            .Select(a => a.AccountType.ProgramId)
            .Distinct()
            .ToListAsync(ct);

        var rules = (await db.Rules.AsNoTracking()
                .Where(r => r.TenantId == tenantGuid && programIds.Contains(r.ProgramId)
                    && r.Status != RuleStatus.Deleted && r.Limits != null)
                .Select(r => new { r.Id, r.Name, r.ProgramId, ProgramName = r.Program.Name, r.Status, r.Template, r.Limits })
                .ToListAsync(ct))
            .Select(r => (Rule: r, Limits: ReadLimits(r.Limits!)))
            .Where(x => x.Limits is { } l && (l.PerCustomerTotal.HasValue || l.PerCustomerPerDay.HasValue || l.PerCustomerPerPeriod.HasValue))
            .OrderBy(x => x.Rule.ProgramName).ThenBy(x => x.Rule.Name)
            .ToList();
        if (rules.Count == 0)
            return [];

        var ruleIds = rules.Select(x => (Guid?)x.Rule.Id).ToList();
        var postings = await db.LedgerEntries
            .Where(l => l.TenantId == tenantId && l.ContactKey == contactKey && ruleIds.Contains(l.RuleId)
                && CustomerViewRules.CapReasons.Contains(l.Reason))
            .Select(l => new { l.RuleId, l.Delta, l.CreatedAt })
            .ToListAsync(ct);
        var byRule = postings.ToLookup(p => p.RuleId!.Value);

        var now = DateTime.UtcNow;
        return rules.Select(x =>
        {
            var limits = x.Limits!;
            var used = byRule[x.Rule.Id].ToList();
            DateTime? periodStart = limits.PerCustomerPerPeriod.HasValue
                ? PeriodWindow.Start(limits.Period, limits.ResetWindow, now)
                : null;
            return new RuleCapUsageResponse(
                x.Rule.Id, x.Rule.Name, x.Rule.ProgramId, x.Rule.ProgramName, x.Rule.Status,
                x.Rule.Template == CardBucketsAppService.Template,
                limits.PerCustomerTotal, used.Sum(p => p.Delta),
                limits.PerCustomerPerDay, used.Where(p => p.CreatedAt >= now.Date).Sum(p => p.Delta),
                limits.PerCustomerPerPeriod, limits.PerCustomerPerPeriod.HasValue ? limits.Period ?? "Day" : null,
                limits.PerCustomerPerPeriod.HasValue ? limits.ResetWindow ?? "Calendar" : null,
                periodStart,
                periodStart is { } start ? used.Where(p => p.CreatedAt >= start).Sum(p => p.Delta) : null);
        }).ToList();
    }

    public async Task<IReadOnlyList<CustomerStreakResponse>> GetStreaksAsync(string tenantId, string contactKey, CancellationToken ct)
    {
        var tenantGuid = await tenantSlugResolver.ResolveAsync(tenantId, ct);
        var progress = await db.StreakProgresses.AsNoTracking()
            .Where(s => s.TenantId == tenantGuid && s.ContactKey == contactKey)
            .ToListAsync(ct);
        var campaignIds = progress.Select(s => s.CampaignId).ToList();

        var campaigns = await db.StreakCampaigns.AsNoTracking()
            .Where(c => c.TenantId == tenantGuid && campaignIds.Contains(c.Id))
            .Select(c => new { c.Id, c.Name, c.ProgramId, ProgramName = c.Program.Name, c.Status, c.Config })
            .ToDictionaryAsync(c => c.Id, ct);
        var periods = await db.StreakPeriodStates.AsNoTracking()
            .Where(s => s.TenantId == tenantGuid && s.ContactKey == contactKey && campaignIds.Contains(s.CampaignId))
            .ToListAsync(ct);
        var latestPeriod = periods.GroupBy(s => s.CampaignId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(s => s.PeriodStart).First());
        var logs = (await db.StreakLogs.AsNoTracking()
                .Where(s => s.TenantId == tenantGuid && s.ContactKey == contactKey && campaignIds.Contains(s.CampaignId))
                .ToListAsync(ct))
            .ToLookup(s => s.CampaignId);

        return progress
            .Where(s => campaigns.ContainsKey(s.CampaignId))
            .Select(s =>
            {
                var c = campaigns[s.CampaignId];
                var config = ReadStreakConfig(c.Config);
                var current = latestPeriod.GetValueOrDefault(s.CampaignId);
                return new CustomerStreakResponse(
                    c.Id, c.Name, c.ProgramId, c.ProgramName, c.Status,
                    config?.Period ?? "", config?.TargetPeriods ?? 0, config?.Aggregate?.Metric ?? "",
                    config?.Aggregate?.Threshold ?? 0, config?.Reward?.Kind ?? "",
                    s.StreakCount, s.LastMetPeriod, s.Completions, s.Status,
                    current?.PeriodStart, current?.AggSum, current?.AggCount, current?.Met,
                    logs[s.CampaignId].OrderByDescending(l => l.CompletionNo)
                        .Select(l => new StreakCompletionResponse(l.CampaignId, c.Name, l.CompletionNo, l.CompletedPeriod,
                            l.Periods, l.RewardKind, l.RewardRef, l.CreatedAt))
                        .ToList());
            })
            .OrderBy(r => r.ProgramName).ThenBy(r => r.CampaignName)
            .ToList();
    }

    // Rewards bought with points and stamp-card rewards (reward_log), plus reward definitions a
    // streak granted (streak_log — a streak grant writes no reward_log row). The payout is found by
    // the key RewardFulfilmentService posted it under (CustomerViewRules.CashbackKey/TierUpgradeKey).
    public async Task<IReadOnlyList<CustomerRewardResponse>> GetRewardsAsync(string tenantId, string contactKey, CancellationToken ct)
    {
        var tenantGuid = await tenantSlugResolver.ResolveAsync(tenantId, ct);

        var logs = await db.RewardLogs.AsNoTracking()
            .Where(r => r.TenantId == tenantGuid && r.ContactKey == contactKey)
            .ToListAsync(ct);
        var grants = await db.StreakLogs.AsNoTracking()
            .Where(s => s.TenantId == tenantGuid && s.ContactKey == contactKey && s.RewardKind == StreakRewardKind.RewardDefinition)
            .ToListAsync(ct);

        var campaignIds = grants.Select(g => g.CampaignId).Distinct().ToList();
        var campaignRewardIds = (await db.StreakCampaigns.AsNoTracking()
                .Where(c => c.TenantId == tenantGuid && campaignIds.Contains(c.Id))
                .Select(c => new { c.Id, c.Config })
                .ToListAsync(ct))
            .ToDictionary(c => c.Id, c => ReadStreakConfig(c.Config)?.Reward?.RewardDefinitionId);

        var definitionIds = logs.Where(l => l.RewardDefinitionId is not null).Select(l => l.RewardDefinitionId!.Value)
            .Concat(campaignRewardIds.Values.Where(v => v is not null).Select(v => v!.Value))
            .Distinct().ToList();
        var definitions = await db.RewardDefinitions.AsNoTracking()
            .Where(d => d.TenantId == tenantGuid && definitionIds.Contains(d.Id))
            .ToDictionaryAsync(d => d.Id, ct);

        // Grant per reward row: the purchase event id, or the streak completion key.
        var rows = logs.Select(l => (Log: (RewardLog?)l, Grant: (StreakLog?)null, Key: l.SourceEventId))
            .Concat(grants.Select(g => (Log: (RewardLog?)null, Grant: (StreakLog?)g,
                Key: CustomerViewRules.StreakGrant(g.CampaignId, contactKey, g.CompletionNo))))
            .ToList();

        var cashbackKeys = rows.Select(r => CustomerViewRules.CashbackKey(r.Key)).ToList();
        var costEntryIds = logs.Select(l => l.LedgerResetEntryId).ToList();
        var ledgerRows = await db.LedgerEntries
            .Where(l => l.TenantId == tenantId && l.ContactKey == contactKey
                && (cashbackKeys.Contains(l.IdempotencyKey) || costEntryIds.Contains(l.Id)))
            .Select(l => new { l.Id, l.IdempotencyKey, l.Delta, WalletName = l.CustomerAccount.AccountType.Name })
            .ToListAsync(ct);
        var cashback = ledgerRows.Where(l => cashbackKeys.Contains(l.IdempotencyKey)).ToDictionary(l => l.IdempotencyKey);
        var costs = ledgerRows.ToDictionary(l => l.Id);

        var tierKeys = rows.Select(r => CustomerViewRules.TierUpgradeKey(r.Key)).ToList();
        var tierUpgrades = await db.TierUpgradeLogs
            .Where(t => t.TenantId == tenantGuid && t.ContactKey == contactKey && tierKeys.Contains(t.SourceEventId))
            .Select(t => new { t.SourceEventId, ToTierName = t.ToTier.Name })
            .ToDictionaryAsync(t => t.SourceEventId, t => t.ToTierName, ct);

        return rows.Select(r =>
        {
            var definitionId = r.Log?.RewardDefinitionId ?? (r.Grant is { } g ? campaignRewardIds.GetValueOrDefault(g.CampaignId) : null);
            var definition = definitionId is { } did ? definitions.GetValueOrDefault(did) : null;
            var cost = r.Log is { } log ? costs.GetValueOrDefault(log.LedgerResetEntryId) : null;
            var cash = cashback.GetValueOrDefault(CustomerViewRules.CashbackKey(r.Key));
            var tier = tierUpgrades.GetValueOrDefault(CustomerViewRules.TierUpgradeKey(r.Key));
            var source = r.Grant is not null ? RewardAcquisition.StreakCompletion
                : definition?.Acquisition ?? RewardAcquisition.StampCompletion;

            return new CustomerRewardResponse(
                source, r.Log?.RewardName ?? definition?.Name ?? "", definitionId, definition?.RewardType,
                r.Log?.SourceEventId ?? r.Grant!.SourceEventId,
                cost is null ? null : Math.Abs(cost.Delta), cost?.WalletName,
                cash is not null ? "cash_credited" : tier is not null ? "tier_upgraded" : "none",
                cash?.Delta, cash?.WalletName, tier,
                r.Log?.Status, r.Log?.CreatedAt ?? r.Grant!.CreatedAt);
        })
            .OrderByDescending(r => r.CreatedAt)
            .ToList();
    }

    // ── CR 2026-10-02 (Customer 360) P3 ──

    public async Task<IReadOnlyList<CustomerCardBucketResponse>> GetCardBucketsAsync(string tenantId, string contactKey, CancellationToken ct)
    {
        var tenantGuid = await tenantSlugResolver.ResolveAsync(tenantId, ct);
        var programIds = await db.CustomerAccounts
            .Where(a => a.TenantId == tenantGuid && a.ContactKey == contactKey)
            .Select(a => a.AccountType.ProgramId)
            .Distinct()
            .ToListAsync(ct);

        var buckets = await db.Rules.AsNoTracking()
            .Where(r => r.TenantId == tenantGuid && programIds.Contains(r.ProgramId)
                && r.Template == CardBucketsAppService.Template && r.Status != RuleStatus.Deleted)
            .Select(r => new { r.Id, r.Name, r.ProgramId, ProgramName = r.Program.Name, r.Status, r.Calculation, r.Limits })
            .ToListAsync(ct);
        if (buckets.Count == 0)
            return [];

        var ruleIds = buckets.Select(b => (Guid?)b.Id).ToList();
        var postings = (await db.LedgerEntries
                .Where(l => l.TenantId == tenantId && l.ContactKey == contactKey && ruleIds.Contains(l.RuleId)
                    && CustomerViewRules.CapReasons.Contains(l.Reason))
                .Select(l => new { l.RuleId, l.Reason, l.Delta, l.CreatedAt })
                .ToListAsync(ct))
            .ToLookup(p => p.RuleId!.Value);

        var today = DateTime.UtcNow.Date;
        return buckets
            .Select(b =>
            {
                var limits = b.Limits is null ? null : ReadLimits(b.Limits);
                var used = postings[b.Id].ToList();
                var earns = used.Where(p => p.Reason != LedgerReason.Refund).ToList();
                return new CustomerCardBucketResponse(
                    b.Id, b.Name, b.ProgramId, b.ProgramName, b.Status, ReadFixedAmount(b.Calculation),
                    limits?.PerCustomerPerDay, used.Where(p => p.CreatedAt >= today).Sum(p => p.Delta),
                    limits?.PerCustomerTotal, used.Sum(p => p.Delta),
                    earns.Count, earns.Count == 0 ? null : earns.Max(p => p.CreatedAt));
            })
            .OrderBy(b => b.ProgramName).ThenBy(b => b.Name)
            .ToList();
    }

    // D5: no payload. Published messages are purged after 30 days (OutboxPublisherWorker), so
    // older published messages are no longer listed.
    public async Task<CursorPage<SentMessageResponse>> GetMessagesAsync(
        string tenantId, string contactKey, MessageFilter filter, string? cursor, int limit, CancellationToken ct)
    {
        EnsureRange(filter.From, filter.To);
        if (filter.Status is not null && !OutboxStatuses.Contains(filter.Status))
            throw new ValidationApiException($"'status' must be one of: {string.Join(", ", OutboxStatuses)}.");
        var tenantGuid = await tenantSlugResolver.ResolveAsync(tenantId, ct);

        var query = db.OutboxEvents.AsNoTracking().Where(o => o.TenantId == tenantGuid && o.ContactKey == contactKey);
        if (!string.IsNullOrEmpty(filter.EventType))
            query = query.Where(o => o.EventType == filter.EventType);
        if (filter.Status is not null)
            query = query.Where(o => o.Status == filter.Status);
        if (filter.From is { } from)
            query = query.Where(o => o.CreatedAt >= from);
        if (filter.To is { } to)
            query = query.Where(o => o.CreatedAt <= to);
        var total = await query.CountAsync(ct);
        if (MessageCursor.TryParse(cursor, out var createdAt, out var afterId))
            query = query.Where(o => o.CreatedAt < createdAt || (o.CreatedAt == createdAt && o.Id < afterId));

        var rows = await query
            .OrderByDescending(o => o.CreatedAt).ThenByDescending(o => o.Id)
            .Take(limit + 1)
            .Select(o => new { o.Id, o.EventId, o.EventType, o.Status, o.Attempts, o.DedupKey, o.CreatedAt, o.PublishedAt, o.Payload })
            .ToListAsync(ct);
        var hasMore = rows.Count > limit;
        var page = rows.Take(limit).ToList();

        return new CursorPage<SentMessageResponse>
        {
            Data = page.Select(o => new SentMessageResponse(o.EventId, o.EventType, o.Status, o.Attempts, o.DedupKey,
                o.CreatedAt, o.PublishedAt, ReadReason(o.Payload))).ToList(),
            NextCursor = hasMore ? MessageCursor.Format(page[^1].CreatedAt, page[^1].Id) : null,
            Total = total
        };
    }

    private static readonly string[] OutboxStatuses = [OutboxStatus.Pending, OutboxStatus.Published, OutboxStatus.Failed];

    // A card bucket is a FixedBonusRule: its reward is calculation.amount.
    private static decimal? ReadFixedAmount(string calculationJson)
    {
        try
        {
            return JsonSerializer.Deserialize<RuleCalculation>(calculationJson)?.FixedValue;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    // Same options RulesAppService reads limits with: each model's own [JsonPropertyName].
    private static RuleLimits? ReadLimits(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<RuleLimits>(json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    // Messages are enqueued while the event is processed, so only the customer's outbox rows
    // around that time are read, then matched: data.source_event_id for every outbound type,
    // and the dedup key for tier.changed, which carries no source_event_id.
    private async Task<IReadOnlyList<SentMessageResponse>> LoadMessagesAsync(
        Guid tenantGuid, string contactKey, string eventId, EventInbox? inbox, IReadOnlyList<PostingRow> postings,
        IReadOnlyList<string> streakTierKeys, CancellationToken ct)
    {
        var anchors = postings.Select(p => p.CreatedAt).ToList();
        if (inbox is not null)
        {
            anchors.Add(inbox.ReceivedAt);
            if (inbox.ProcessedAt is { } processedAt) anchors.Add(processedAt);
        }
        if (anchors.Count == 0)
            return [];

        var windowStart = anchors.Min() - MessageWindowBefore;
        var windowEnd = anchors.Max() + MessageWindowAfter;
        var candidates = await db.OutboxEvents.AsNoTracking()
            .Where(o => o.TenantId == tenantGuid && o.ContactKey == contactKey
                && o.CreatedAt >= windowStart && o.CreatedAt <= windowEnd)
            .OrderBy(o => o.Id)
            .ToListAsync(ct);

        var pointsTierPrefix = $"tier_changed:{eventId}:";
        var streakTierDedupKeys = streakTierKeys.Select(k => $"tier_changed:{k}").ToHashSet();

        return candidates
            .Where(o => ReadSourceEventId(o.Payload) == eventId
                || (o.DedupKey is { } key && (key.StartsWith(pointsTierPrefix, StringComparison.Ordinal)
                    || streakTierDedupKeys.Contains(key))))
            .Select(o => new SentMessageResponse(o.EventId, o.EventType, o.Status, o.Attempts, o.DedupKey,
                o.CreatedAt, o.PublishedAt, ReadReason(o.Payload)))
            .ToList();
    }

    private static readonly TimeSpan MessageWindowBefore = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan MessageWindowAfter = TimeSpan.FromHours(1);

    private static readonly string[] InboxStatuses = [InboxStatus.Pending, InboxStatus.Processed, InboxStatus.Failed];

    private static void EnsureRange(DateTime? from, DateTime? to)
    {
        if (from is not null && to is not null && from > to)
            throw new ValidationApiException("'from' must not be after 'to'.");
    }

    private sealed record PostingRow(
        Guid Id, string ContactKey, string Reason, decimal Delta, string? Metadata, DateTime CreatedAt,
        string SourceEventId, Guid? RuleId,
        Guid AccountTypeId, string AccountTypeName, string AccountTypeType, Guid ProgramId, string ProgramName);

    private static readonly Expression<Func<LedgerEntry, PostingRow>> PostingRowProjection = l => new PostingRow(
        l.Id, l.ContactKey, l.Reason, l.Delta, l.Metadata, l.CreatedAt, l.SourceEventId, l.RuleId,
        l.CustomerAccount.AccountTypeId, l.CustomerAccount.AccountType.Name, l.CustomerAccount.AccountType.Type,
        l.CustomerAccount.AccountType.ProgramId, l.CustomerAccount.AccountType.Program.Name);

    private sealed record PostingSource(
        Guid? RuleId, string? RuleName, int? RuleVersion, Guid? CampaignId, string? CampaignName);

    private sealed class PostingSources(
        IReadOnlyDictionary<string, string> eventTypes,
        IReadOnlyDictionary<Guid, int> ruleVersions,
        IReadOnlyDictionary<Guid, string> ruleNames,
        IReadOnlyDictionary<Guid, string> campaignNames)
    {
        public string? EventTypeOf(string sourceEventId) => eventTypes.GetValueOrDefault(sourceEventId);

        // A fixed-bonus streak posting stores the campaign id in rule_id, so an id that isn't a
        // rule but is a campaign is reported as the campaign.
        public PostingSource For(PostingRow row)
        {
            if (row.RuleId is not { } ruleId)
                return new PostingSource(null, null, null, null, null);
            if (!ruleNames.ContainsKey(ruleId) && campaignNames.TryGetValue(ruleId, out var campaignName))
                return new PostingSource(null, null, null, ruleId, campaignName);
            return new PostingSource(ruleId, ruleNames.GetValueOrDefault(ruleId),
                ruleVersions.TryGetValue(row.Id, out var v) ? v : null, null, null);
        }
    }

    // Where each posting came from: the inbound event's type, and the rule (with the version
    // recorded on its rule_fire_audit row) or the streak campaign.
    private async Task<PostingSources> LoadPostingSourcesAsync(
        Guid tenantGuid, string tenantSlug, IReadOnlyList<PostingRow> rows, CancellationToken ct)
    {
        var entryIds = rows.Select(r => r.Id).ToList();
        var eventIds = rows.Select(r => r.SourceEventId).Distinct().ToList();
        var ruleIds = rows.Where(r => r.RuleId is not null).Select(r => r.RuleId!.Value).Distinct().ToList();

        var eventTypes = await db.EventInbox
            .Where(e => e.TenantId == tenantSlug && eventIds.Contains(e.EventId))
            .ToDictionaryAsync(e => e.EventId, e => e.EventType, ct);

        var versions = await db.RuleFireAudits
            .Where(a => a.TenantId == tenantGuid && a.LedgerEntryId != null && entryIds.Contains(a.LedgerEntryId.Value))
            .Select(a => new { EntryId = a.LedgerEntryId!.Value, a.RuleVersion })
            .ToListAsync(ct);

        var ruleNames = await db.Rules
            .Where(r => r.TenantId == tenantGuid && ruleIds.Contains(r.Id))
            .ToDictionaryAsync(r => r.Id, r => r.Name, ct);

        var campaignNames = await db.StreakCampaigns
            .Where(c => c.TenantId == tenantGuid && ruleIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, c => c.Name, ct);

        return new PostingSources(
            eventTypes,
            versions.GroupBy(v => v.EntryId).ToDictionary(g => g.Key, g => g.First().RuleVersion),
            ruleNames,
            campaignNames);
    }

    // The inbox stores the whole envelope as serialized by the Consumer (PascalCase members,
    // "Data" holding the event's own snake_case fields); outbox payloads use camelCase members.
    // Read both case-insensitively.
    private static (DateTime? OccurredAt, JsonElement? Data) ReadEnvelope(string payload)
    {
        try
        {
            using var doc = JsonDocument.Parse(payload);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                return (null, null);

            DateTime? occurredAt = null;
            JsonElement? data = null;
            foreach (var property in doc.RootElement.EnumerateObject())
            {
                if (property.Name.Equals("OccurredAt", StringComparison.OrdinalIgnoreCase)
                    && property.Value.ValueKind == JsonValueKind.String
                    && property.Value.TryGetDateTime(out var at) && at != default)
                    occurredAt = at.Kind == DateTimeKind.Unspecified
                        ? DateTime.SpecifyKind(at, DateTimeKind.Utc)
                        : at.ToUniversalTime();
                else if (property.Name.Equals("Data", StringComparison.OrdinalIgnoreCase))
                    data = property.Value.Clone();
            }
            return (occurredAt, data);
        }
        catch (JsonException)
        {
            return (null, null);
        }
    }

    private static string? ReadSourceEventId(string payload) =>
        ReadEnvelope(payload).Data is { ValueKind: JsonValueKind.Object } data
        && data.TryGetProperty("source_event_id", out var id) && id.ValueKind == JsonValueKind.String
            ? id.GetString()
            : null;

    // Addendum C (D5 amended): only data.reason leaves the payload — the outcome code of a
    // *_failed message (redeem_failed, transfer_failed, purchase_failed, cash.*_failed).
    private static string? ReadReason(string payload) =>
        ReadEnvelope(payload).Data is { ValueKind: JsonValueKind.Object } data
        && data.TryGetProperty("reason", out var reason) && reason.ValueKind == JsonValueKind.String
            ? reason.GetString()
            : null;

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

// CR 2026-10-02: same shape as LedgerCursor, for event_inbox's string event ids. Split on the
// first '_' only, so an event id containing '_' round-trips.
internal static class EventCursor
{
    public static string Format(DateTime receivedAt, string eventId) => $"{receivedAt.Ticks}_{eventId}";

    public static bool TryParse(string? cursor, out DateTime receivedAt, out string eventId)
    {
        receivedAt = default;
        eventId = "";
        if (string.IsNullOrEmpty(cursor)) return false;

        var parts = cursor.Split('_', 2);
        if (parts.Length != 2 || !long.TryParse(parts[0], out var ticks) || parts[1].Length == 0)
            return false;

        receivedAt = new DateTime(ticks, DateTimeKind.Utc);
        eventId = parts[1];
        return true;
    }
}

// CR 2026-10-02 P3: outbox rows page on (created_at, id) — id is the bigint identity.
internal static class MessageCursor
{
    public static string Format(DateTime createdAt, long id) => $"{createdAt.Ticks}_{id}";

    public static bool TryParse(string? cursor, out DateTime createdAt, out long id)
    {
        createdAt = default;
        id = default;
        if (string.IsNullOrEmpty(cursor)) return false;

        var parts = cursor.Split('_', 2);
        if (parts.Length != 2 || !long.TryParse(parts[0], out var ticks) || !long.TryParse(parts[1], out id))
            return false;

        createdAt = new DateTime(ticks, DateTimeKind.Utc);
        return true;
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
