using System.Globalization;
using FluentValidation;
using dEngage.Loyalty.Api.Framework.ErrorHandling;
using dEngage.Loyalty.Api.Framework.Json;
using dEngage.Loyalty.Api.Framework.Modules;
using dEngage.Loyalty.Api.Framework.Validation;
using HttpStatusCode = System.Net.HttpStatusCode;

namespace dEngage.Loyalty.Api.Customers;

// /api/v1/tenants/{tenantId}/customers[/{contactKey}[/ledger|/tier-history|/events[/{eventId}]|/rule-fires|/cap-usage|/streaks|/rewards|/card-buckets|/messages|/birthday]] —
// read-only except POST .../birthday (CR-10 A11's one deliberate exception).
public sealed class CustomersModule : TenantScopedModule
{
    public CustomersModule(ICustomersAppService appService, IValidator<RegisterBirthdayRequest> birthdayValidator)
        : base("/api/v1/tenants/{tenantId}/customers")
    {
        MapGet("", async (parameters, ct) =>
        {
            var tenantId = RequireTenantScope(RouteParam(parameters, "tenantId"));
            var search = (string?)Request.Query["search"];
            return JsonResponses.Ok(await appService.ListAsync(tenantId, ReadPageRequest(), search, ct));
        });

        MapGet("/{contactKey}", async (parameters, ct) =>
        {
            var tenantId = RequireTenantScope(RouteParam(parameters, "tenantId"));
            var contactKey = RouteParam(parameters, "contactKey");
            return JsonResponses.Ok(await appService.GetProfileAsync(tenantId, contactKey, ct));
        });

        MapGet("/{contactKey}/ledger", async (parameters, ct) =>
        {
            var tenantId = RequireTenantScope(RouteParam(parameters, "tenantId"));
            var contactKey = RouteParam(parameters, "contactKey");
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
        });

        MapGet("/{contactKey}/tier-history", async (parameters, ct) =>
        {
            var tenantId = RequireTenantScope(RouteParam(parameters, "tenantId"));
            var contactKey = RouteParam(parameters, "contactKey");
            return JsonResponses.Ok(await appService.GetTierHistoryAsync(tenantId, contactKey, ct));
        });

        MapGet("/{contactKey}/events", async (parameters, ct) =>
        {
            var tenantId = RequireTenantScope(RouteParam(parameters, "tenantId"));
            var contactKey = RouteParam(parameters, "contactKey");
            var filter = new CustomerEventFilter(
                QueryString("eventType"),
                QueryString("status"),
                QueryUtc("from"),
                QueryUtc("to"));

            return JsonResponses.Ok(await appService.GetEventsAsync(
                tenantId, contactKey, filter, (string?)Request.Query["cursor"], ReadLimit(), ct));
        });

        MapGet("/{contactKey}/events/{eventId}", async (parameters, ct) =>
        {
            var tenantId = RequireTenantScope(RouteParam(parameters, "tenantId"));
            var contactKey = RouteParam(parameters, "contactKey");
            var eventId = RouteParam(parameters, "eventId");
            return JsonResponses.Ok(await appService.GetEventAsync(tenantId, contactKey, eventId, ct));
        });

        // CR 2026-10-02 (Customer 360) P2.
        MapGet("/{contactKey}/rule-fires", async (parameters, ct) =>
        {
            var tenantId = RequireTenantScope(RouteParam(parameters, "tenantId"));
            var contactKey = RouteParam(parameters, "contactKey");
            var filter = new RuleFireFilter(QueryGuid("ruleId"), QueryUtc("from"), QueryUtc("to"));
            return JsonResponses.Ok(await appService.GetRuleFiresAsync(
                tenantId, contactKey, filter, (string?)Request.Query["cursor"], ReadLimit(), ct));
        });

        MapGet("/{contactKey}/cap-usage", async (parameters, ct) =>
        {
            var tenantId = RequireTenantScope(RouteParam(parameters, "tenantId"));
            var contactKey = RouteParam(parameters, "contactKey");
            return JsonResponses.Ok(await appService.GetCapUsageAsync(tenantId, contactKey, ct));
        });

        MapGet("/{contactKey}/streaks", async (parameters, ct) =>
        {
            var tenantId = RequireTenantScope(RouteParam(parameters, "tenantId"));
            var contactKey = RouteParam(parameters, "contactKey");
            return JsonResponses.Ok(await appService.GetStreaksAsync(tenantId, contactKey, ct));
        });

        MapGet("/{contactKey}/rewards", async (parameters, ct) =>
        {
            var tenantId = RequireTenantScope(RouteParam(parameters, "tenantId"));
            var contactKey = RouteParam(parameters, "contactKey");
            return JsonResponses.Ok(await appService.GetRewardsAsync(tenantId, contactKey, ct));
        });

        // CR 2026-10-02 (Customer 360) P3.
        MapGet("/{contactKey}/card-buckets", async (parameters, ct) =>
        {
            var tenantId = RequireTenantScope(RouteParam(parameters, "tenantId"));
            var contactKey = RouteParam(parameters, "contactKey");
            return JsonResponses.Ok(await appService.GetCardBucketsAsync(tenantId, contactKey, ct));
        });

        MapGet("/{contactKey}/messages", async (parameters, ct) =>
        {
            var tenantId = RequireTenantScope(RouteParam(parameters, "tenantId"));
            var contactKey = RouteParam(parameters, "contactKey");
            var filter = new MessageFilter(QueryString("eventType"), QueryString("status"), QueryUtc("from"), QueryUtc("to"));
            return JsonResponses.Ok(await appService.GetMessagesAsync(
                tenantId, contactKey, filter, (string?)Request.Query["cursor"], ReadLimit(), ct));
        });

        MapPost("/{contactKey}/birthday", async (parameters, ct) =>
        {
            var tenantId = RequireTenantScope(RouteParam(parameters, "tenantId"));
            var contactKey = RouteParam(parameters, "contactKey");
            var request = await Request.ReadValidatedJsonBodyAsync(birthdayValidator, ct);
            return JsonResponses.Ok(await appService.RegisterBirthdayAsync(tenantId, contactKey, request.MonthDay, ct), HttpStatusCode.Created);
        });
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
