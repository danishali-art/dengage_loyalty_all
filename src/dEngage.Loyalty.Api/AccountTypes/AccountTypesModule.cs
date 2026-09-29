using FluentValidation;
using dEngage.Loyalty.Api.Framework.Json;
using dEngage.Loyalty.Api.Framework.Modules;
using dEngage.Loyalty.Api.Framework.Validation;
using HttpStatusCode = System.Net.HttpStatusCode;

namespace dEngage.Loyalty.Api.AccountTypes;

// /api/v1/tenants/{tenantId}/programs/{programId}/account-types
public sealed class AccountTypesModule : TenantScopedModule
{
    public AccountTypesModule(
        IAccountTypesAppService appService,
        IValidator<CreateAccountTypeRequest> createValidator,
        IValidator<UpdateAccountTypeRequest> updateValidator)
        : base("/api/v1/tenants/{tenantId}/programs/{programId}/account-types")
    {
        MapGet("", async (parameters, ct) =>
        {
            var tenantId = RequireTenantScope(RouteParam(parameters, "tenantId"));
            var programId = RouteGuidParam(parameters, "programId");
            return JsonResponses.Ok(await appService.ListAsync(tenantId, programId, ReadPageRequest(), ct));
        });

        MapGet("/{accountTypeId}", async (parameters, ct) =>
        {
            var tenantId = RequireTenantScope(RouteParam(parameters, "tenantId"));
            var programId = RouteGuidParam(parameters, "programId");
            var accountTypeId = RouteGuidParam(parameters, "accountTypeId");
            return JsonResponses.Ok(await appService.GetAsync(tenantId, programId, accountTypeId, ct));
        });

        MapPost("", async (parameters, ct) =>
        {
            var tenantId = RequireTenantScope(RouteParam(parameters, "tenantId"));
            var programId = RouteGuidParam(parameters, "programId");
            var request = await Request.ReadValidatedJsonBodyAsync(createValidator, ct);
            return JsonResponses.Ok(await appService.CreateAsync(tenantId, programId, request, ct), HttpStatusCode.Created);
        });

        MapPatch("/{accountTypeId}", async (parameters, ct) =>
        {
            var tenantId = RequireTenantScope(RouteParam(parameters, "tenantId"));
            var programId = RouteGuidParam(parameters, "programId");
            var accountTypeId = RouteGuidParam(parameters, "accountTypeId");
            var request = await Request.ReadValidatedJsonBodyAsync(updateValidator, ct);
            return JsonResponses.Ok(await appService.UpdateAsync(tenantId, programId, accountTypeId, request, ct));
        });
    }
}
