using FluentValidation;
using dEngage.Loyalty.Api.Framework.Json;
using dEngage.Loyalty.Api.Framework.Controllers;
using dEngage.Loyalty.Api.Framework.Validation;
using HttpStatusCode = System.Net.HttpStatusCode;
using Microsoft.AspNetCore.Mvc;


namespace dEngage.Loyalty.Api.Tiers;

// /api/v1/tenants/{tenantId}/programs/{programId}/tiers
[Route("api/v1/tenants/{tenantId}/programs/{programId}/tiers")]
public sealed class TiersController(
    ITiersAppService appService,
    IValidator<CreateTierRequest> createValidator,
    IValidator<UpdateTierRequest> updateValidator,
    IValidator<ReorderTiersRequest> reorderValidator) : LoyaltyControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var tenantId = RequireTenantScope(RouteParam("tenantId"));
        var programId = RouteGuidParam("programId");
        return JsonResponses.Ok(await appService.ListAsync(tenantId, programId, ReadPageRequest(), ct));
    }

    [HttpPost]
    public async Task<IActionResult> Create(CancellationToken ct)
    {
        var tenantId = RequireTenantScope(RouteParam("tenantId"));
        var programId = RouteGuidParam("programId");
        var principal = RequireAuthenticated();
        var request = await Request.ReadValidatedJsonBodyAsync(createValidator, ct);
        return JsonResponses.Ok(await appService.CreateAsync(tenantId, programId, request, principal, ct), HttpStatusCode.Created);
    }

    [HttpPatch("{tierId}")]
    public async Task<IActionResult> Update(CancellationToken ct)
    {
        var tenantId = RequireTenantScope(RouteParam("tenantId"));
        var programId = RouteGuidParam("programId");
        var tierId = RouteGuidParam("tierId");
        var principal = RequireAuthenticated();
        var request = await Request.ReadValidatedJsonBodyAsync(updateValidator, ct);
        return JsonResponses.Ok(await appService.UpdateAsync(tenantId, programId, tierId, request, principal, ct));
    }

    [HttpPost("reorder")]
    public async Task<IActionResult> Reorder(CancellationToken ct)
    {
        var tenantId = RequireTenantScope(RouteParam("tenantId"));
        var programId = RouteGuidParam("programId");
        var request = await Request.ReadValidatedJsonBodyAsync(reorderValidator, ct);
        await appService.ReorderAsync(tenantId, programId, request, ct);
        return JsonResponses.NoContent();
    }

    [HttpDelete("{tierId}")]
    public async Task<IActionResult> Delete(CancellationToken ct)
    {
        var tenantId = RequireTenantScope(RouteParam("tenantId"));
        var programId = RouteGuidParam("programId");
        var tierId = RouteGuidParam("tierId");
        var principal = RequireAuthenticated();
        await appService.DeleteAsync(tenantId, programId, tierId, principal, ct);
        return JsonResponses.NoContent();
    }
}
