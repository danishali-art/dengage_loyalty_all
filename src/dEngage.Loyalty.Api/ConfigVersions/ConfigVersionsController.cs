using dEngage.Loyalty.Api.Framework.ErrorHandling;
using dEngage.Loyalty.Api.Framework.Json;
using dEngage.Loyalty.Api.Framework.Controllers;
using Microsoft.AspNetCore.Mvc;


namespace dEngage.Loyalty.Api.ConfigVersions;

// /api/v1/tenants/{tenantId}/config-versions — read-only history for versioned config
// aggregates (Program, TierDefinition, Rule, RewardDefinition, AccountType, CardBucket,
// StreakCampaign share this one table; see ConfigVersion's remarks). View-only for now —
// rollback/restore needs bespoke reconciliation per entity type and is a deliberate fast-follow.
[Route("api/v1/tenants/{tenantId}/config-versions")]
public sealed class ConfigVersionsController(
    IConfigVersionsAppService appService) : LoyaltyControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var tenantId = RequireTenantScope(RouteParam("tenantId"));
        var entityType = (string?)Request.Query["entityType"]
            ?? throw new ValidationApiException("entityType query parameter is required.");
        if (!Guid.TryParse((string?)Request.Query["entityId"], out var entityId))
            throw new ValidationApiException("entityId query parameter is required and must be a GUID.");

        return JsonResponses.Ok(await appService.ListAsync(tenantId, entityType, entityId, ReadPageRequest(), ct));
    }

    [HttpGet("{versionId}")]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        var tenantId = RequireTenantScope(RouteParam("tenantId"));
        return JsonResponses.Ok(await appService.GetAsync(tenantId, RouteGuidParam("versionId"), ct));
    }
}
