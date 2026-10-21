using FluentValidation;
using dEngage.Loyalty.Api.Framework.Json;
using dEngage.Loyalty.Api.Framework.Controllers;
using dEngage.Loyalty.Api.Framework.Validation;
using HttpStatusCode = System.Net.HttpStatusCode;
using Microsoft.AspNetCore.Mvc;


namespace dEngage.Loyalty.Api.StreakCampaigns;

// /api/v1/tenants/{tenantId}/programs/{programId}/streak-campaigns
[Route("api/v1/tenants/{tenantId}/programs/{programId}/streak-campaigns")]
public sealed class StreakCampaignsController(
    IStreakCampaignsAppService appService,
    IValidator<CreateStreakCampaignRequest> createValidator,
    IValidator<UpdateStreakCampaignRequest> updateValidator,
    IValidator<SetStreakCampaignStatusRequest> statusValidator) : LoyaltyControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var tenantId = RequireTenantScope(RouteParam("tenantId"));
        var programId = RouteGuidParam("programId");

        var filter = new StreakCampaignListFilter(
            (string?)Request.Query["event"],
            (string?)Request.Query["status"]);

        return JsonResponses.Ok(await appService.ListAsync(tenantId, programId, filter, ReadPageRequest(), ct));
    }

    [HttpGet("{campaignId}")]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        var tenantId = RequireTenantScope(RouteParam("tenantId"));
        var programId = RouteGuidParam("programId");
        var campaignId = RouteGuidParam("campaignId");
        return JsonResponses.Ok(await appService.GetAsync(tenantId, programId, campaignId, ct));
    }

    [HttpPost]
    public async Task<IActionResult> Create(CancellationToken ct)
    {
        var tenantId = RequireTenantScope(RouteParam("tenantId"));
        var programId = RouteGuidParam("programId");
        var request = await Request.ReadValidatedJsonBodyAsync(createValidator, ct);
        return JsonResponses.Ok(await appService.CreateAsync(tenantId, programId, request, ct), HttpStatusCode.Created);
    }

    [HttpPatch("{campaignId}")]
    public async Task<IActionResult> Update(CancellationToken ct)
    {
        var tenantId = RequireTenantScope(RouteParam("tenantId"));
        var programId = RouteGuidParam("programId");
        var campaignId = RouteGuidParam("campaignId");
        var request = await Request.ReadValidatedJsonBodyAsync(updateValidator, ct);
        return JsonResponses.Ok(await appService.UpdateAsync(tenantId, programId, campaignId, request, ct));
    }

    [HttpPatch("{campaignId}/status")]
    public async Task<IActionResult> SetStatus(CancellationToken ct)
    {
        var tenantId = RequireTenantScope(RouteParam("tenantId"));
        var programId = RouteGuidParam("programId");
        var campaignId = RouteGuidParam("campaignId");
        var request = await Request.ReadValidatedJsonBodyAsync(statusValidator, ct);
        return JsonResponses.Ok(await appService.SetStatusAsync(tenantId, programId, campaignId, request.Status, ct));
    }

    [HttpDelete("{campaignId}")]
    public async Task<IActionResult> Delete(CancellationToken ct)
    {
        var tenantId = RequireTenantScope(RouteParam("tenantId"));
        var programId = RouteGuidParam("programId");
        var campaignId = RouteGuidParam("campaignId");
        await appService.DeleteAsync(tenantId, programId, campaignId, ct);
        return JsonResponses.NoContent();
    }
}
