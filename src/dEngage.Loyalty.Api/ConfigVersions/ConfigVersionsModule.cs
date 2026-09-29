using dEngage.Loyalty.Api.Framework.ErrorHandling;
using dEngage.Loyalty.Api.Framework.Json;
using dEngage.Loyalty.Api.Framework.Modules;

namespace dEngage.Loyalty.Api.ConfigVersions;

// /api/v1/tenants/{tenantId}/config-versions — read-only history for versioned config
// aggregates (Program, TierDefinition, Rule, RewardDefinition, AccountType, CardBucket,
// StreakCampaign share this one table; see ConfigVersion's remarks). View-only for now —
// rollback/restore needs bespoke reconciliation per entity type and is a deliberate fast-follow.
public sealed class ConfigVersionsModule : TenantScopedModule
{
    public ConfigVersionsModule(IConfigVersionsAppService appService)
        : base("/api/v1/tenants/{tenantId}/config-versions")
    {
        MapGet("", async (parameters, ct) =>
        {
            var tenantId = RequireTenantScope(RouteParam(parameters, "tenantId"));
            var entityType = (string?)Request.Query["entityType"]
                ?? throw new ValidationApiException("entityType query parameter is required.");
            if (!Guid.TryParse((string?)Request.Query["entityId"], out var entityId))
                throw new ValidationApiException("entityId query parameter is required and must be a GUID.");

            return JsonResponses.Ok(await appService.ListAsync(tenantId, entityType, entityId, ReadPageRequest(), ct));
        });

        MapGet("/{versionId}", async (parameters, ct) =>
        {
            var tenantId = RequireTenantScope(RouteParam(parameters, "tenantId"));
            return JsonResponses.Ok(await appService.GetAsync(tenantId, RouteGuidParam(parameters, "versionId"), ct));
        });
    }
}
