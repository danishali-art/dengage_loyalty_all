using FluentValidation;
using dEngage.Loyalty.Api.Framework.Json;
using dEngage.Loyalty.Api.Framework.Modules;
using dEngage.Loyalty.Api.Framework.Validation;
using HttpStatusCode = System.Net.HttpStatusCode;

namespace dEngage.Loyalty.Api.Customers;

// /api/v1/tenants/{tenantId}/customers[/{contactKey}[/ledger|/tier-history|/birthday]] —
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
            var limit = int.TryParse((string?)Request.Query["limit"], out var l) ? Math.Clamp(l, 1, 100) : 25;

            return JsonResponses.Ok(await appService.GetLedgerAsync(tenantId, contactKey, accountTypeId, cursor, limit, ct));
        });

        MapGet("/{contactKey}/tier-history", async (parameters, ct) =>
        {
            var tenantId = RequireTenantScope(RouteParam(parameters, "tenantId"));
            var contactKey = RouteParam(parameters, "contactKey");
            return JsonResponses.Ok(await appService.GetTierHistoryAsync(tenantId, contactKey, ct));
        });

        MapPost("/{contactKey}/birthday", async (parameters, ct) =>
        {
            var tenantId = RequireTenantScope(RouteParam(parameters, "tenantId"));
            var contactKey = RouteParam(parameters, "contactKey");
            var request = await Request.ReadValidatedJsonBodyAsync(birthdayValidator, ct);
            return JsonResponses.Ok(await appService.RegisterBirthdayAsync(tenantId, contactKey, request.MonthDay, ct), HttpStatusCode.Created);
        });
    }
}
