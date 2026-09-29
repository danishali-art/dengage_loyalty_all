using FluentValidation;
using dEngage.Loyalty.Api.Framework.Json;
using dEngage.Loyalty.Api.Framework.Modules;
using dEngage.Loyalty.Api.Framework.Validation;
using HttpStatusCode = System.Net.HttpStatusCode;

namespace dEngage.Loyalty.Api.Tiers;

// /api/v1/tenants/{tenantId}/programs/{programId}/tiers
public sealed class TiersModule : TenantScopedModule
{
    public TiersModule(
        ITiersAppService appService,
        IValidator<CreateTierRequest> createValidator,
        IValidator<UpdateTierRequest> updateValidator,
        IValidator<ReorderTiersRequest> reorderValidator)
        : base("/api/v1/tenants/{tenantId}/programs/{programId}/tiers")
    {
        MapGet("", async (parameters, ct) =>
        {
            var tenantId = RequireTenantScope(RouteParam(parameters, "tenantId"));
            var programId = RouteGuidParam(parameters, "programId");
            return JsonResponses.Ok(await appService.ListAsync(tenantId, programId, ReadPageRequest(), ct));
        });

        MapPost("", async (parameters, ct) =>
        {
            var tenantId = RequireTenantScope(RouteParam(parameters, "tenantId"));
            var programId = RouteGuidParam(parameters, "programId");
            var principal = RequireAuthenticated();
            var request = await Request.ReadValidatedJsonBodyAsync(createValidator, ct);
            return JsonResponses.Ok(await appService.CreateAsync(tenantId, programId, request, principal, ct), HttpStatusCode.Created);
        });

        MapPatch("/{tierId}", async (parameters, ct) =>
        {
            var tenantId = RequireTenantScope(RouteParam(parameters, "tenantId"));
            var programId = RouteGuidParam(parameters, "programId");
            var tierId = RouteGuidParam(parameters, "tierId");
            var principal = RequireAuthenticated();
            var request = await Request.ReadValidatedJsonBodyAsync(updateValidator, ct);
            return JsonResponses.Ok(await appService.UpdateAsync(tenantId, programId, tierId, request, principal, ct));
        });

        MapPost("/reorder", async (parameters, ct) =>
        {
            var tenantId = RequireTenantScope(RouteParam(parameters, "tenantId"));
            var programId = RouteGuidParam(parameters, "programId");
            var request = await Request.ReadValidatedJsonBodyAsync(reorderValidator, ct);
            await appService.ReorderAsync(tenantId, programId, request, ct);
            return JsonResponses.NoContent();
        });

        MapDelete("/{tierId}", async (parameters, ct) =>
        {
            var tenantId = RequireTenantScope(RouteParam(parameters, "tenantId"));
            var programId = RouteGuidParam(parameters, "programId");
            var tierId = RouteGuidParam(parameters, "tierId");
            var principal = RequireAuthenticated();
            await appService.DeleteAsync(tenantId, programId, tierId, principal, ct);
            return JsonResponses.NoContent();
        });
    }
}
