using FluentValidation;
using dEngage.Loyalty.Api.Framework.Json;
using dEngage.Loyalty.Api.Framework.Modules;
using dEngage.Loyalty.Api.Framework.Validation;
using HttpStatusCode = System.Net.HttpStatusCode;

namespace dEngage.Loyalty.Api.Rules;

// /api/v1/tenants/{tenantId}/programs/{programId}/rules
public sealed class RulesModule : TenantScopedModule
{
    public RulesModule(
        IRulesAppService appService,
        IValidator<CreateRuleRequest> createValidator,
        IValidator<UpdateRuleRequest> updateValidator,
        IValidator<SetRuleStatusRequest> statusValidator)
        : base("/api/v1/tenants/{tenantId}/programs/{programId}/rules")
    {
        MapGet("", async (parameters, ct) =>
        {
            var tenantId = RequireTenantScope(RouteParam(parameters, "tenantId"));
            var programId = RouteGuidParam(parameters, "programId");

            var filter = new RuleListFilter(
                (string?)Request.Query["event"],
                (string?)Request.Query["type"],
                Guid.TryParse((string?)Request.Query["targetAccountTypeId"], out var atId) ? atId : null,
                (string?)Request.Query["status"],
                bool.TryParse((string?)Request.Query["stackable"], out var stackable) ? stackable : null);

            return JsonResponses.Ok(await appService.ListAsync(tenantId, programId, filter, ReadPageRequest(), ct));
        });

        // CR-03: must be registered before "/{ruleId}" — Nancy matches routes in registration
        // order and "/{ruleId}" would otherwise swallow "/metadata" as a route parameter.
        MapGet("/metadata", (parameters, ct) =>
        {
            RequireTenantScope(RouteParam(parameters, "tenantId"));
            return Task.FromResult<object>(JsonResponses.Ok(appService.GetMetadata()));
        });

        MapGet("/{ruleId}", async (parameters, ct) =>
        {
            var tenantId = RequireTenantScope(RouteParam(parameters, "tenantId"));
            var programId = RouteGuidParam(parameters, "programId");
            var ruleId = RouteGuidParam(parameters, "ruleId");
            return JsonResponses.Ok(await appService.GetAsync(tenantId, programId, ruleId, ct));
        });

        MapPost("", async (parameters, ct) =>
        {
            var tenantId = RequireTenantScope(RouteParam(parameters, "tenantId"));
            var programId = RouteGuidParam(parameters, "programId");
            var request = await Request.ReadValidatedJsonBodyAsync(createValidator, ct);
            var createdBy = RequireAuthenticated().SubjectId;
            return JsonResponses.Ok(await appService.CreateAsync(tenantId, programId, request, createdBy, ct), HttpStatusCode.Created);
        });

        MapPatch("/{ruleId}", async (parameters, ct) =>
        {
            var tenantId = RequireTenantScope(RouteParam(parameters, "tenantId"));
            var programId = RouteGuidParam(parameters, "programId");
            var ruleId = RouteGuidParam(parameters, "ruleId");
            var request = await Request.ReadValidatedJsonBodyAsync(updateValidator, ct);
            return JsonResponses.Ok(await appService.UpdateAsync(tenantId, programId, ruleId, request, ct));
        });

        MapPatch("/{ruleId}/status", async (parameters, ct) =>
        {
            var tenantId = RequireTenantScope(RouteParam(parameters, "tenantId"));
            var programId = RouteGuidParam(parameters, "programId");
            var ruleId = RouteGuidParam(parameters, "ruleId");
            var request = await Request.ReadValidatedJsonBodyAsync(statusValidator, ct);
            return JsonResponses.Ok(await appService.SetStatusAsync(tenantId, programId, ruleId, request.Status, ct));
        });

        // CR-04: distinct from PATCH /{ruleId}/status — only moves a CASH rule out of
        // PendingApproval, and requires a different admin than the one who created it
        // (enforced in RulesAppService.ApproveAsync).
        MapPatch("/{ruleId}/approve", async (parameters, ct) =>
        {
            var tenantId = RequireTenantScope(RouteParam(parameters, "tenantId"));
            var programId = RouteGuidParam(parameters, "programId");
            var ruleId = RouteGuidParam(parameters, "ruleId");
            var approvedBy = RequireAuthenticated().SubjectId;
            return JsonResponses.Ok(await appService.ApproveAsync(tenantId, programId, ruleId, approvedBy, ct));
        });

        MapDelete("/{ruleId}", async (parameters, ct) =>
        {
            var tenantId = RequireTenantScope(RouteParam(parameters, "tenantId"));
            var programId = RouteGuidParam(parameters, "programId");
            var ruleId = RouteGuidParam(parameters, "ruleId");
            await appService.DeleteAsync(tenantId, programId, ruleId, ct);
            return JsonResponses.NoContent();
        });
    }
}
