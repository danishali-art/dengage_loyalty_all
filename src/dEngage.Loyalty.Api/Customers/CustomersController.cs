using System.Globalization;
using dEngage.Loyalty.Api.Framework.ErrorHandling;
using dEngage.Loyalty.Api.Framework.Json;
using dEngage.Loyalty.Api.Framework.Controllers;
using Microsoft.AspNetCore.Mvc;


namespace dEngage.Loyalty.Api.Customers;

// /api/v1/tenants/{tenantId}/customers[/{contactKey}[/ledger|/tier-history|/events[/{eventId}]|/rule-fires|/cap-usage|/streaks|/rewards|/card-buckets|/messages]] —
// read-only. (POST .../birthday, CR-10's one write, was removed by CR 2026-10-05 addendum A.)
[Route("api/v1/tenants/{tenantId}/customers")]
public sealed class CustomersController(
    ICustomersAppService appService) : LoyaltyControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var tenantId = RequireTenantScope(RouteParam("tenantId"));
        var search = (string?)Request.Query["search"];
        return JsonResponses.Ok(await appService.ListAsync(tenantId, ReadPageRequest(), search, ct));
    }

    [HttpGet("{contactKey}")]
    public async Task<IActionResult> GetProfile(CancellationToken ct)
    {
        var tenantId = RequireTenantScope(RouteParam("tenantId"));
        var contactKey = RouteParam("contactKey");
        return JsonResponses.Ok(await appService.GetProfileAsync(tenantId, contactKey, ct));
    }

    [HttpGet("{contactKey}/ledger")]
    public async Task<IActionResult> GetLedger(CancellationToken ct)
    {
        var tenantId = RequireTenantScope(RouteParam("tenantId"));
        var contactKey = RouteParam("contactKey");
        var accountTypeId = Guid.TryParse((string?)Request.Query["accountTypeId"], out var atId) ? atId : (Guid?)null;
        var cursor = (string?)Request.Query["cursor"];
        var limit = ReadLimit();

        // CR 2026-10-02 (Customer 360): the filters below are new and reject malformed values
        // (400); accountTypeId keeps its original lenient parsing.
        var filter = new LedgerFilter(
            accountTypeId,
            QueryGuid("programId"),
            QueryString("reasonGroup"),
            QueryUtc("from"),
            QueryUtc("to"),
            QueryString("eventId"),
            QueryGuid("ruleId"));

        return JsonResponses.Ok(await appService.GetLedgerAsync(tenantId, contactKey, filter, cursor, limit, ct));
    }

    [HttpGet("{contactKey}/tier-history")]
    public async Task<IActionResult> GetTierHistory(CancellationToken ct)
    {
        var tenantId = RequireTenantScope(RouteParam("tenantId"));
        var contactKey = RouteParam("contactKey");
        return JsonResponses.Ok(await appService.GetTierHistoryAsync(tenantId, contactKey, ct));
    }

    [HttpGet("{contactKey}/events")]
    public async Task<IActionResult> GetEvents(CancellationToken ct)
    {
        var tenantId = RequireTenantScope(RouteParam("tenantId"));
        var contactKey = RouteParam("contactKey");
        var filter = new CustomerEventFilter(
            QueryString("eventType"),
            QueryString("status"),
            QueryUtc("from"),
            QueryUtc("to"));

        return JsonResponses.Ok(await appService.GetEventsAsync(
            tenantId, contactKey, filter, (string?)Request.Query["cursor"], ReadLimit(), ct));
    }

    [HttpGet("{contactKey}/events/{eventId}")]
    public async Task<IActionResult> GetEvent(CancellationToken ct)
    {
        var tenantId = RequireTenantScope(RouteParam("tenantId"));
        var contactKey = RouteParam("contactKey");
        var eventId = RouteParam("eventId");
        return JsonResponses.Ok(await appService.GetEventAsync(tenantId, contactKey, eventId, ct));
    }

    // CR 2026-10-02 (Customer 360) P2.
    [HttpGet("{contactKey}/rule-fires")]
    public async Task<IActionResult> GetRuleFires(CancellationToken ct)
    {
        var tenantId = RequireTenantScope(RouteParam("tenantId"));
        var contactKey = RouteParam("contactKey");
        var filter = new RuleFireFilter(QueryGuid("ruleId"), QueryUtc("from"), QueryUtc("to"));
        return JsonResponses.Ok(await appService.GetRuleFiresAsync(
            tenantId, contactKey, filter, (string?)Request.Query["cursor"], ReadLimit(), ct));
    }

    [HttpGet("{contactKey}/cap-usage")]
    public async Task<IActionResult> GetCapUsage(CancellationToken ct)
    {
        var tenantId = RequireTenantScope(RouteParam("tenantId"));
        var contactKey = RouteParam("contactKey");
        return JsonResponses.Ok(await appService.GetCapUsageAsync(tenantId, contactKey, ct));
    }

    [HttpGet("{contactKey}/streaks")]
    public async Task<IActionResult> GetStreaks(CancellationToken ct)
    {
        var tenantId = RequireTenantScope(RouteParam("tenantId"));
        var contactKey = RouteParam("contactKey");
        return JsonResponses.Ok(await appService.GetStreaksAsync(tenantId, contactKey, ct));
    }

    [HttpGet("{contactKey}/rewards")]
    public async Task<IActionResult> GetRewards(CancellationToken ct)
    {
        var tenantId = RequireTenantScope(RouteParam("tenantId"));
        var contactKey = RouteParam("contactKey");
        return JsonResponses.Ok(await appService.GetRewardsAsync(tenantId, contactKey, ct));
    }

    // CR 2026-10-02 (Customer 360) P3.
    [HttpGet("{contactKey}/card-buckets")]
    public async Task<IActionResult> GetCardBuckets(CancellationToken ct)
    {
        var tenantId = RequireTenantScope(RouteParam("tenantId"));
        var contactKey = RouteParam("contactKey");
        return JsonResponses.Ok(await appService.GetCardBucketsAsync(tenantId, contactKey, ct));
    }

    [HttpGet("{contactKey}/messages")]
    public async Task<IActionResult> GetMessages(CancellationToken ct)
    {
        var tenantId = RequireTenantScope(RouteParam("tenantId"));
        var contactKey = RouteParam("contactKey");
        var filter = new MessageFilter(QueryString("eventType"), QueryString("status"), QueryUtc("from"), QueryUtc("to"));
        return JsonResponses.Ok(await appService.GetMessagesAsync(
            tenantId, contactKey, filter, (string?)Request.Query["cursor"], ReadLimit(), ct));
    }

    private int ReadLimit() =>
        int.TryParse((string?)Request.Query["limit"], out var l) ? Math.Clamp(l, 1, 100) : 25;

    private string? QueryString(string name)
    {
        var raw = (string?)Request.Query[name];
        return string.IsNullOrWhiteSpace(raw) ? null : raw.Trim();
    }

    private Guid? QueryGuid(string name)
    {
        var raw = QueryString(name);
        if (raw is null) return null;
        return Guid.TryParse(raw, out var id) ? id : throw new ValidationApiException($"'{name}' must be a GUID.");
    }

    // ISO-8601. A value without an offset is taken as UTC (guardrails: UTC everywhere) — the
    // portal converts the admin's local input before sending.
    private DateTime? QueryUtc(string name)
    {
        var raw = QueryString(name);
        if (raw is null) return null;
        return DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var at)
            ? at.UtcDateTime
            : throw new ValidationApiException($"'{name}' must be an ISO-8601 date-time.");
    }
}
