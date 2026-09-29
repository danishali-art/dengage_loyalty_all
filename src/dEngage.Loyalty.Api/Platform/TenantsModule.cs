using FluentValidation;
using dEngage.Loyalty.Api.Framework.Json;
using dEngage.Loyalty.Api.Framework.Modules;
using dEngage.Loyalty.Api.Framework.Validation;
using HttpStatusCode = System.Net.HttpStatusCode;

namespace dEngage.Loyalty.Api.Platform;

// /api/v1/platform/tenants — platform_admin only. Onboards tenants (partitions provisioned
// transactionally, see PlatformAppService), manages their API keys, and creates admin users.
public sealed class TenantsModule : TenantScopedModule
{
    public TenantsModule(
        IPlatformAppService appService,
        IValidator<CreateTenantRequest> createTenantValidator,
        IValidator<UpdateTenantRequest> updateTenantValidator,
        IValidator<CreateAdminUserRequest> createAdminUserValidator)
        : base("/api/v1/platform/tenants")
    {
        MapGet("", async (_, ct) =>
        {
            RequirePlatformAdmin();
            var page = ReadPageRequest();
            var search = (string?)Request.Query["search"];
            return JsonResponses.Ok(await appService.ListTenantsAsync(page, search, ct));
        });

        MapGet("/{tenantId}", async (parameters, ct) =>
        {
            RequirePlatformAdmin();
            return JsonResponses.Ok(await appService.GetTenantAsync(RouteParam(parameters, "tenantId"), ct));
        });

        MapPost("", async (_, ct) =>
        {
            RequirePlatformAdmin();
            var request = await Request.ReadValidatedJsonBodyAsync(createTenantValidator, ct);
            return JsonResponses.Ok(await appService.CreateTenantAsync(request, ct), HttpStatusCode.Created);
        });

        MapPatch("/{tenantId}", async (parameters, ct) =>
        {
            RequirePlatformAdmin();
            var request = await Request.ReadValidatedJsonBodyAsync(updateTenantValidator, ct);
            return JsonResponses.Ok(await appService.UpdateTenantAsync(RouteParam(parameters, "tenantId"), request, ct));
        });

        MapPost("/{tenantId}/api-keys", async (parameters, ct) =>
        {
            RequirePlatformAdmin();
            var tenantId = RouteParam(parameters, "tenantId");
            return JsonResponses.Ok(await appService.CreateApiKeyAsync(tenantId, ct), HttpStatusCode.Created);
        });

        MapGet("/{tenantId}/api-keys", async (parameters, ct) =>
        {
            RequirePlatformAdmin();
            var tenantId = RouteParam(parameters, "tenantId");
            return JsonResponses.Ok(await appService.ListApiKeysAsync(tenantId, ReadPageRequest(), ct));
        });

        MapDelete("/{tenantId}/api-keys/{keyId}", async (parameters, ct) =>
        {
            RequirePlatformAdmin();
            var tenantId = RouteParam(parameters, "tenantId");
            var keyId = RouteGuidParam(parameters, "keyId");
            await appService.RevokeApiKeyAsync(tenantId, keyId, ct);
            return JsonResponses.NoContent();
        });

        MapPost("/{tenantId}/admin-users", async (parameters, ct) =>
        {
            RequirePlatformAdmin();
            var tenantId = RouteParam(parameters, "tenantId");
            var request = await Request.ReadValidatedJsonBodyAsync(createAdminUserValidator, ct);
            return JsonResponses.Ok(await appService.CreateAdminUserAsync(tenantId, request, ct), HttpStatusCode.Created);
        });

        MapGet("/{tenantId}/admin-users", async (parameters, ct) =>
        {
            RequirePlatformAdmin();
            var tenantId = RouteParam(parameters, "tenantId");
            return JsonResponses.Ok(await appService.ListAdminUsersAsync(tenantId, ReadPageRequest(), ct));
        });
    }
}
