using FluentValidation;
using dEngage.Loyalty.Api.Framework.Json;
using dEngage.Loyalty.Api.Framework.Modules;
using dEngage.Loyalty.Api.Framework.Validation;
using HttpStatusCode = System.Net.HttpStatusCode;

namespace dEngage.Loyalty.Api.Programs;

// /api/v1/tenants/{tenantId}/programs
public sealed class ProgramsModule : TenantScopedModule
{
    public ProgramsModule(
        IProgramsAppService appService,
        IValidator<CreateProgramRequest> createValidator,
        IValidator<UpdateProgramRequest> updateValidator)
        : base("/api/v1/tenants/{tenantId}/programs")
    {
        MapGet("", async (parameters, ct) =>
        {
            var tenantId = RequireTenantScope(RouteParam(parameters, "tenantId"));
            return JsonResponses.Ok(await appService.ListAsync(tenantId, ReadPageRequest(), ct));
        });

        MapGet("/{programId}", async (parameters, ct) =>
        {
            var tenantId = RequireTenantScope(RouteParam(parameters, "tenantId"));
            return JsonResponses.Ok(await appService.GetAsync(tenantId, RouteGuidParam(parameters, "programId"), ct));
        });

        MapPost("", async (parameters, ct) =>
        {
            var tenantId = RequireTenantScope(RouteParam(parameters, "tenantId"));
            var principal = RequireAuthenticated();
            var request = await Request.ReadValidatedJsonBodyAsync(createValidator, ct);
            return JsonResponses.Ok(await appService.CreateAsync(tenantId, request, principal, ct), HttpStatusCode.Created);
        });

        MapPatch("/{programId}", async (parameters, ct) =>
        {
            var tenantId = RequireTenantScope(RouteParam(parameters, "tenantId"));
            var principal = RequireAuthenticated();
            var request = await Request.ReadValidatedJsonBodyAsync(updateValidator, ct);
            return JsonResponses.Ok(await appService.UpdateAsync(tenantId, RouteGuidParam(parameters, "programId"), request, principal, ct));
        });

        // 1.3.CL items 8/9: Draft → Published, writing one ProgramPublication ConfigVersion.
        MapPost("/{programId}/publish", async (parameters, ct) =>
        {
            var tenantId = RequireTenantScope(RouteParam(parameters, "tenantId"));
            var principal = RequireAuthenticated();
            return JsonResponses.Ok(await appService.PublishAsync(tenantId, RouteGuidParam(parameters, "programId"), principal, ct));
        });

        MapDelete("/{programId}", async (parameters, ct) =>
        {
            var tenantId = RequireTenantScope(RouteParam(parameters, "tenantId"));
            var principal = RequireAuthenticated();
            await appService.DeleteAsync(tenantId, RouteGuidParam(parameters, "programId"), principal, ct);
            return JsonResponses.NoContent();
        });
    }
}
