using FluentValidation;
using dEngage.Loyalty.Api.Framework.Json;
using dEngage.Loyalty.Api.Framework.Controllers;
using dEngage.Loyalty.Api.Framework.Validation;
using HttpStatusCode = System.Net.HttpStatusCode;
using Microsoft.AspNetCore.Mvc;


namespace dEngage.Loyalty.Api.CardBuckets;

// /api/v1/tenants/{tenantId}/programs/{programId}/card-buckets
[Route("api/v1/tenants/{tenantId}/programs/{programId}/card-buckets")]
public sealed class CardBucketsController(
    ICardBucketsAppService appService,
    IValidator<CreateCardBucketRequest> createValidator,
    IValidator<UpdateCardBucketRequest> updateValidator,
    IValidator<SetCardBucketStatusRequest> statusValidator) : LoyaltyControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var tenantId = RequireTenantScope(RouteParam("tenantId"));
        var programId = RouteGuidParam("programId");

        var filter = new CardBucketListFilter((string?)Request.Query["status"]);

        return JsonResponses.Ok(await appService.ListAsync(tenantId, programId, filter, ReadPageRequest(), ct));
    }

    [HttpGet("{bucketId}")]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        var tenantId = RequireTenantScope(RouteParam("tenantId"));
        var programId = RouteGuidParam("programId");
        var bucketId = RouteGuidParam("bucketId");
        return JsonResponses.Ok(await appService.GetAsync(tenantId, programId, bucketId, ct));
    }

    [HttpPost]
    public async Task<IActionResult> Create(CancellationToken ct)
    {
        var tenantId = RequireTenantScope(RouteParam("tenantId"));
        var programId = RouteGuidParam("programId");
        var request = await Request.ReadValidatedJsonBodyAsync(createValidator, ct);
        return JsonResponses.Ok(await appService.CreateAsync(tenantId, programId, request, ct), HttpStatusCode.Created);
    }

    [HttpPatch("{bucketId}")]
    public async Task<IActionResult> Update(CancellationToken ct)
    {
        var tenantId = RequireTenantScope(RouteParam("tenantId"));
        var programId = RouteGuidParam("programId");
        var bucketId = RouteGuidParam("bucketId");
        var request = await Request.ReadValidatedJsonBodyAsync(updateValidator, ct);
        return JsonResponses.Ok(await appService.UpdateAsync(tenantId, programId, bucketId, request, ct));
    }

    [HttpPatch("{bucketId}/status")]
    public async Task<IActionResult> SetStatus(CancellationToken ct)
    {
        var tenantId = RequireTenantScope(RouteParam("tenantId"));
        var programId = RouteGuidParam("programId");
        var bucketId = RouteGuidParam("bucketId");
        var request = await Request.ReadValidatedJsonBodyAsync(statusValidator, ct);
        return JsonResponses.Ok(await appService.SetStatusAsync(tenantId, programId, bucketId, request.Status, ct));
    }

    [HttpDelete("{bucketId}")]
    public async Task<IActionResult> Delete(CancellationToken ct)
    {
        var tenantId = RequireTenantScope(RouteParam("tenantId"));
        var programId = RouteGuidParam("programId");
        var bucketId = RouteGuidParam("bucketId");
        await appService.DeleteAsync(tenantId, programId, bucketId, ct);
        return JsonResponses.NoContent();
    }
}
