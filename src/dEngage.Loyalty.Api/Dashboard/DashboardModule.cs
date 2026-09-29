using dEngage.Loyalty.Api.Framework.Json;
using dEngage.Loyalty.Api.Framework.Modules;

namespace dEngage.Loyalty.Api.Dashboard;

// /api/v1/tenants/{tenantId}/dashboard — single-tenant scope (the currently-selected tenant,
// via the portal's existing tenant switcher); no cross-tenant platform-admin rollup.
public sealed class DashboardModule : TenantScopedModule
{
    public DashboardModule(IDashboardAppService appService)
        : base("/api/v1/tenants/{tenantId}/dashboard")
    {
        MapGet("/summary", async (parameters, ct) =>
        {
            var tenantId = RequireTenantScope(RouteParam(parameters, "tenantId"));
            var programId = Guid.TryParse((string?)Request.Query["programId"], out var pid) ? pid : (Guid?)null;
            var from = DateTime.TryParse((string?)Request.Query["fromDate"], out var f) ? f : (DateTime?)null;
            var to = DateTime.TryParse((string?)Request.Query["toDate"], out var t) ? t : (DateTime?)null;
            return JsonResponses.Ok(await appService.GetSummaryAsync(tenantId, programId, from, to, ct));
        });
    }
}
