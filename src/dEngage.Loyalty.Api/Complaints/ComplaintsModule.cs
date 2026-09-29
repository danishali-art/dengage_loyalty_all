using FluentValidation;
using dEngage.Loyalty.Api.Framework.Json;
using dEngage.Loyalty.Api.Framework.Modules;
using dEngage.Loyalty.Api.Framework.Validation;
using HttpStatusCode = System.Net.HttpStatusCode;

namespace dEngage.Loyalty.Api.Complaints;

// /api/v1/tenants/{tenantId}/complaints — tenant-wide, not nested under a program since
// ProgramId is optional (a complaint may not be tied to a specific program).
public sealed class ComplaintsModule : TenantScopedModule
{
    public ComplaintsModule(
        IComplaintsAppService appService,
        IValidator<CreateComplaintRequest> createValidator,
        IValidator<UpdateComplaintStatusRequest> statusValidator)
        : base("/api/v1/tenants/{tenantId}/complaints")
    {
        MapGet("", async (parameters, ct) =>
        {
            var tenantId = RequireTenantScope(RouteParam(parameters, "tenantId"));
            var programId = Guid.TryParse((string?)Request.Query["programId"], out var pid) ? pid : (Guid?)null;
            var filter = new ComplaintListFilter((string?)Request.Query["status"], programId);
            return JsonResponses.Ok(await appService.ListAsync(tenantId, filter, ReadPageRequest(), ct));
        });

        MapGet("/summary", async (parameters, ct) =>
        {
            var tenantId = RequireTenantScope(RouteParam(parameters, "tenantId"));
            var programId = Guid.TryParse((string?)Request.Query["programId"], out var pid) ? pid : (Guid?)null;
            return JsonResponses.Ok(await appService.SummaryAsync(tenantId, programId, ct));
        });

        MapGet("/{complaintId}", async (parameters, ct) =>
        {
            var tenantId = RequireTenantScope(RouteParam(parameters, "tenantId"));
            return JsonResponses.Ok(await appService.GetAsync(tenantId, RouteGuidParam(parameters, "complaintId"), ct));
        });

        MapPost("", async (parameters, ct) =>
        {
            var tenantId = RequireTenantScope(RouteParam(parameters, "tenantId"));
            var request = await Request.ReadValidatedJsonBodyAsync(createValidator, ct);
            return JsonResponses.Ok(await appService.CreateAsync(tenantId, request, ct), HttpStatusCode.Created);
        });

        MapPatch("/{complaintId}/status", async (parameters, ct) =>
        {
            var tenantId = RequireTenantScope(RouteParam(parameters, "tenantId"));
            var request = await Request.ReadValidatedJsonBodyAsync(statusValidator, ct);
            return JsonResponses.Ok(await appService.UpdateStatusAsync(tenantId, RouteGuidParam(parameters, "complaintId"), request, ct));
        });
    }
}
