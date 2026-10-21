using FluentValidation;
using dEngage.Loyalty.Api.Framework.Json;
using dEngage.Loyalty.Api.Framework.Controllers;
using dEngage.Loyalty.Api.Framework.Validation;
using HttpStatusCode = System.Net.HttpStatusCode;
using Microsoft.AspNetCore.Mvc;


namespace dEngage.Loyalty.Api.AccountTypes;

// /api/v1/tenants/{tenantId}/programs/{programId}/account-types
[Route("api/v1/tenants/{tenantId}/programs/{programId}/account-types")]
public sealed class AccountTypesController(
    IAccountTypesAppService appService,
    IValidator<CreateAccountTypeRequest> createValidator,
    IValidator<UpdateAccountTypeRequest> updateValidator) : LoyaltyControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var tenantId = RequireTenantScope(RouteParam("tenantId"));
        var programId = RouteGuidParam("programId");
        return JsonResponses.Ok(await appService.ListAsync(tenantId, programId, ReadPageRequest(), ct));
    }

    [HttpGet("{accountTypeId}")]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        var tenantId = RequireTenantScope(RouteParam("tenantId"));
        var programId = RouteGuidParam("programId");
        var accountTypeId = RouteGuidParam("accountTypeId");
        return JsonResponses.Ok(await appService.GetAsync(tenantId, programId, accountTypeId, ct));
    }

    [HttpPost]
    public async Task<IActionResult> Create(CancellationToken ct)
    {
        var tenantId = RequireTenantScope(RouteParam("tenantId"));
        var programId = RouteGuidParam("programId");
        var request = await Request.ReadValidatedJsonBodyAsync(createValidator, ct);
        return JsonResponses.Ok(await appService.CreateAsync(tenantId, programId, request, ct), HttpStatusCode.Created);
    }

    [HttpPatch("{accountTypeId}")]
    public async Task<IActionResult> Update(CancellationToken ct)
    {
        var tenantId = RequireTenantScope(RouteParam("tenantId"));
        var programId = RouteGuidParam("programId");
        var accountTypeId = RouteGuidParam("accountTypeId");
        var request = await Request.ReadValidatedJsonBodyAsync(updateValidator, ct);
        return JsonResponses.Ok(await appService.UpdateAsync(tenantId, programId, accountTypeId, request, ct));
    }
}
