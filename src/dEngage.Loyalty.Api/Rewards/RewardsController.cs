using FluentValidation;
using dEngage.Loyalty.Api.Framework.Json;
using dEngage.Loyalty.Api.Framework.Controllers;
using dEngage.Loyalty.Api.Framework.Validation;
using HttpStatusCode = System.Net.HttpStatusCode;
using Microsoft.AspNetCore.Mvc;


namespace dEngage.Loyalty.Api.Rewards;

// /api/v1/tenants/{tenantId}/programs/{programId}/rewards
[Route("api/v1/tenants/{tenantId}/programs/{programId}/rewards")]
public sealed class RewardsController(
    IRewardsAppService appService,
    IValidator<CreateRewardRequest> createValidator,
    IValidator<UpdateRewardRequest> updateValidator) : LoyaltyControllerBase
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
        var request = await Request.ReadValidatedJsonBodyAsync(createValidator, ct);
        var createdBy = RequireAuthenticated().SubjectId;
        return JsonResponses.Ok(await appService.CreateAsync(tenantId, programId, request, createdBy, ct), HttpStatusCode.Created);
    }

    [HttpPatch("{rewardId}")]
    public async Task<IActionResult> Update(CancellationToken ct)
    {
        var tenantId = RequireTenantScope(RouteParam("tenantId"));
        var programId = RouteGuidParam("programId");
        var rewardId = RouteGuidParam("rewardId");
        var request = await Request.ReadValidatedJsonBodyAsync(updateValidator, ct);
        return JsonResponses.Ok(await appService.UpdateAsync(tenantId, programId, rewardId, request, ct));
    }

    [HttpPatch("{rewardId}/active")]
    public async Task<IActionResult> SetActive(CancellationToken ct)
    {
        var tenantId = RequireTenantScope(RouteParam("tenantId"));
        var programId = RouteGuidParam("programId");
        var rewardId = RouteGuidParam("rewardId");
        var body = await Request.ReadJsonBodyAsync<SetActiveRequest>(ct);
        return JsonResponses.Ok(await appService.SetActiveAsync(tenantId, programId, rewardId, body.IsActive, ct));
    }

    // CR 2026-09-30 (A4): moves a cashback reward out of PendingApproval — a different admin
    // than its creator (same shape as PATCH .../rules/{ruleId}/approve, CR-04).
    [HttpPatch("{rewardId}/approve")]
    public async Task<IActionResult> Approve(CancellationToken ct)
    {
        var tenantId = RequireTenantScope(RouteParam("tenantId"));
        var programId = RouteGuidParam("programId");
        var rewardId = RouteGuidParam("rewardId");
        var approvedBy = RequireAuthenticated().SubjectId;
        return JsonResponses.Ok(await appService.ApproveAsync(tenantId, programId, rewardId, approvedBy, ct));
    }

    [HttpDelete("{rewardId}")]
    public async Task<IActionResult> Delete(CancellationToken ct)
    {
        var tenantId = RequireTenantScope(RouteParam("tenantId"));
        var programId = RouteGuidParam("programId");
        var rewardId = RouteGuidParam("rewardId");
        await appService.DeleteAsync(tenantId, programId, rewardId, ct);
        return JsonResponses.NoContent();
    }

    private sealed record SetActiveRequest(bool IsActive);
}
