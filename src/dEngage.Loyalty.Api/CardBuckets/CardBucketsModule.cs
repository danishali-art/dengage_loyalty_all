using FluentValidation;
using dEngage.Loyalty.Api.Framework.Json;
using dEngage.Loyalty.Api.Framework.Modules;
using dEngage.Loyalty.Api.Framework.Validation;
using HttpStatusCode = System.Net.HttpStatusCode;

namespace dEngage.Loyalty.Api.CardBuckets;

// /api/v1/tenants/{tenantId}/programs/{programId}/card-buckets
public sealed class CardBucketsModule : TenantScopedModule
{
    public CardBucketsModule(
        ICardBucketsAppService appService,
        IValidator<CreateCardBucketRequest> createValidator,
        IValidator<UpdateCardBucketRequest> updateValidator,
        IValidator<SetCardBucketStatusRequest> statusValidator)
        : base("/api/v1/tenants/{tenantId}/programs/{programId}/card-buckets")
    {
        MapGet("", async (parameters, ct) =>
        {
            var tenantId = RequireTenantScope(RouteParam(parameters, "tenantId"));
            var programId = RouteGuidParam(parameters, "programId");

            var filter = new CardBucketListFilter((string?)Request.Query["status"]);

            return JsonResponses.Ok(await appService.ListAsync(tenantId, programId, filter, ReadPageRequest(), ct));
        });

        MapGet("/{bucketId}", async (parameters, ct) =>
        {
            var tenantId = RequireTenantScope(RouteParam(parameters, "tenantId"));
            var programId = RouteGuidParam(parameters, "programId");
            var bucketId = RouteGuidParam(parameters, "bucketId");
            return JsonResponses.Ok(await appService.GetAsync(tenantId, programId, bucketId, ct));
        });

        MapPost("", async (parameters, ct) =>
        {
            var tenantId = RequireTenantScope(RouteParam(parameters, "tenantId"));
            var programId = RouteGuidParam(parameters, "programId");
            var request = await Request.ReadValidatedJsonBodyAsync(createValidator, ct);
            return JsonResponses.Ok(await appService.CreateAsync(tenantId, programId, request, ct), HttpStatusCode.Created);
        });

        MapPatch("/{bucketId}", async (parameters, ct) =>
        {
            var tenantId = RequireTenantScope(RouteParam(parameters, "tenantId"));
            var programId = RouteGuidParam(parameters, "programId");
            var bucketId = RouteGuidParam(parameters, "bucketId");
            var request = await Request.ReadValidatedJsonBodyAsync(updateValidator, ct);
            return JsonResponses.Ok(await appService.UpdateAsync(tenantId, programId, bucketId, request, ct));
        });

        MapPatch("/{bucketId}/status", async (parameters, ct) =>
        {
            var tenantId = RequireTenantScope(RouteParam(parameters, "tenantId"));
            var programId = RouteGuidParam(parameters, "programId");
            var bucketId = RouteGuidParam(parameters, "bucketId");
            var request = await Request.ReadValidatedJsonBodyAsync(statusValidator, ct);
            return JsonResponses.Ok(await appService.SetStatusAsync(tenantId, programId, bucketId, request.Status, ct));
        });

        MapDelete("/{bucketId}", async (parameters, ct) =>
        {
            var tenantId = RequireTenantScope(RouteParam(parameters, "tenantId"));
            var programId = RouteGuidParam(parameters, "programId");
            var bucketId = RouteGuidParam(parameters, "bucketId");
            await appService.DeleteAsync(tenantId, programId, bucketId, ct);
            return JsonResponses.NoContent();
        });
    }
}
