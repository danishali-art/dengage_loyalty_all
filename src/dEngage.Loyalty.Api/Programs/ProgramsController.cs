using FluentValidation;
using dEngage.Loyalty.Api.Framework.Json;
using dEngage.Loyalty.Api.Framework.Controllers;
using dEngage.Loyalty.Api.Framework.Validation;
using HttpStatusCode = System.Net.HttpStatusCode;
using Microsoft.AspNetCore.Mvc;


namespace dEngage.Loyalty.Api.Programs;

// /api/v1/tenants/{tenantId}/programs
[Route("api/v1/tenants/{tenantId}/programs")]
public sealed class ProgramsController(
    IProgramsAppService appService,
    IValidator<CreateProgramRequest> createValidator,
    IValidator<UpdateProgramRequest> updateValidator) : LoyaltyControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var tenantId = RequireTenantScope(RouteParam("tenantId"));
        return JsonResponses.Ok(await appService.ListAsync(tenantId, ReadPageRequest(), ct));
    }

    [HttpGet("{programId}")]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        var tenantId = RequireTenantScope(RouteParam("tenantId"));
        return JsonResponses.Ok(await appService.GetAsync(tenantId, RouteGuidParam("programId"), ct));
    }

    [HttpPost]
    public async Task<IActionResult> Create(CancellationToken ct)
    {
        var tenantId = RequireTenantScope(RouteParam("tenantId"));
        var principal = RequireAuthenticated();
        var request = await Request.ReadValidatedJsonBodyAsync(createValidator, ct);
        return JsonResponses.Ok(await appService.CreateAsync(tenantId, request, principal, ct), HttpStatusCode.Created);
    }

    [HttpPatch("{programId}")]
    public async Task<IActionResult> Update(CancellationToken ct)
    {
        var tenantId = RequireTenantScope(RouteParam("tenantId"));
        var principal = RequireAuthenticated();
        var request = await Request.ReadValidatedJsonBodyAsync(updateValidator, ct);
        return JsonResponses.Ok(await appService.UpdateAsync(tenantId, RouteGuidParam("programId"), request, principal, ct));
    }

    // 1.3.CL items 8/9: Draft → Published, writing one ProgramPublication ConfigVersion.
    [HttpPost("{programId}/publish")]
    public async Task<IActionResult> Publish(CancellationToken ct)
    {
        var tenantId = RequireTenantScope(RouteParam("tenantId"));
        var principal = RequireAuthenticated();
        return JsonResponses.Ok(await appService.PublishAsync(tenantId, RouteGuidParam("programId"), principal, ct));
    }

    [HttpDelete("{programId}")]
    public async Task<IActionResult> Delete(CancellationToken ct)
    {
        var tenantId = RequireTenantScope(RouteParam("tenantId"));
        var principal = RequireAuthenticated();
        await appService.DeleteAsync(tenantId, RouteGuidParam("programId"), principal, ct);
        return JsonResponses.NoContent();
    }
}
