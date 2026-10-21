using FluentValidation;
using dEngage.Loyalty.Api.Framework.Json;
using dEngage.Loyalty.Api.Framework.Controllers;
using dEngage.Loyalty.Api.Framework.Validation;
using HttpStatusCode = System.Net.HttpStatusCode;
using Microsoft.AspNetCore.Mvc;


namespace dEngage.Loyalty.Api.Rules;

// /api/v1/tenants/{tenantId}/programs/{programId}/rules
[Route("api/v1/tenants/{tenantId}/programs/{programId}/rules")]
public sealed class RulesController(
    IRulesAppService appService,
    IValidator<CreateRuleRequest> createValidator,
    IValidator<UpdateRuleRequest> updateValidator,
    IValidator<SetRuleStatusRequest> statusValidator) : LoyaltyControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var tenantId = RequireTenantScope(RouteParam("tenantId"));
        var programId = RouteGuidParam("programId");

        var filter = new RuleListFilter(
            (string?)Request.Query["event"],
            (string?)Request.Query["type"],
            Guid.TryParse((string?)Request.Query["targetAccountTypeId"], out var atId) ? atId : null,
            (string?)Request.Query["status"],
            bool.TryParse((string?)Request.Query["stackable"], out var stackable) ? stackable : null);

        return JsonResponses.Ok(await appService.ListAsync(tenantId, programId, filter, ReadPageRequest(), ct));
    }

    // CR-03. Routing prefers the literal "metadata" segment over "{ruleId}", so the two don't clash.
    [HttpGet("metadata")]
    public IActionResult GetMetadata()
    {
        RequireTenantScope(RouteParam("tenantId"));
        return JsonResponses.Ok(appService.GetMetadata());
    }

    [HttpGet("{ruleId}")]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        var tenantId = RequireTenantScope(RouteParam("tenantId"));
        var programId = RouteGuidParam("programId");
        var ruleId = RouteGuidParam("ruleId");
        return JsonResponses.Ok(await appService.GetAsync(tenantId, programId, ruleId, ct));
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

    [HttpPatch("{ruleId}")]
    public async Task<IActionResult> Update(CancellationToken ct)
    {
        var tenantId = RequireTenantScope(RouteParam("tenantId"));
        var programId = RouteGuidParam("programId");
        var ruleId = RouteGuidParam("ruleId");
        var request = await Request.ReadValidatedJsonBodyAsync(updateValidator, ct);
        return JsonResponses.Ok(await appService.UpdateAsync(tenantId, programId, ruleId, request, ct));
    }

    [HttpPatch("{ruleId}/status")]
    public async Task<IActionResult> SetStatus(CancellationToken ct)
    {
        var tenantId = RequireTenantScope(RouteParam("tenantId"));
        var programId = RouteGuidParam("programId");
        var ruleId = RouteGuidParam("ruleId");
        var request = await Request.ReadValidatedJsonBodyAsync(statusValidator, ct);
        return JsonResponses.Ok(await appService.SetStatusAsync(tenantId, programId, ruleId, request.Status, ct));
    }

    // CR-04: distinct from PATCH /{ruleId}/status — only moves a CASH rule out of
    // PendingApproval, and requires a different admin than the one who created it
    // (enforced in RulesAppService.ApproveAsync).
    [HttpPatch("{ruleId}/approve")]
    public async Task<IActionResult> Approve(CancellationToken ct)
    {
        var tenantId = RequireTenantScope(RouteParam("tenantId"));
        var programId = RouteGuidParam("programId");
        var ruleId = RouteGuidParam("ruleId");
        var approvedBy = RequireAuthenticated().SubjectId;
        return JsonResponses.Ok(await appService.ApproveAsync(tenantId, programId, ruleId, approvedBy, ct));
    }

    [HttpDelete("{ruleId}")]
    public async Task<IActionResult> Delete(CancellationToken ct)
    {
        var tenantId = RequireTenantScope(RouteParam("tenantId"));
        var programId = RouteGuidParam("programId");
        var ruleId = RouteGuidParam("ruleId");
        await appService.DeleteAsync(tenantId, programId, ruleId, ct);
        return JsonResponses.NoContent();
    }
}
