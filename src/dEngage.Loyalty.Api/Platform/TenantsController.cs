using FluentValidation;
using dEngage.Loyalty.Api.Framework.Json;
using dEngage.Loyalty.Api.Framework.Controllers;
using dEngage.Loyalty.Api.Framework.Validation;
using HttpStatusCode = System.Net.HttpStatusCode;
using Microsoft.AspNetCore.Mvc;


namespace dEngage.Loyalty.Api.Platform;

// /api/v1/platform/tenants — platform_admin only. Onboards tenants (partitions provisioned
// transactionally, see PlatformAppService), manages their API keys, and creates admin users.
[Route("api/v1/platform/tenants")]
public sealed class TenantsController(
    IPlatformAppService appService,
    IValidator<CreateTenantRequest> createTenantValidator,
    IValidator<UpdateTenantRequest> updateTenantValidator,
    IValidator<CreateAdminUserRequest> createAdminUserValidator) : LoyaltyControllerBase
{
    [HttpGet]
    public async Task<IActionResult> ListTenants(CancellationToken ct)
    {
        RequirePlatformAdmin();
        var page = ReadPageRequest();
        var search = (string?)Request.Query["search"];
        return JsonResponses.Ok(await appService.ListTenantsAsync(page, search, ct));
    }

    [HttpGet("{tenantId}")]
    public async Task<IActionResult> GetTenant(CancellationToken ct)
    {
        RequirePlatformAdmin();
        return JsonResponses.Ok(await appService.GetTenantAsync(RouteParam("tenantId"), ct));
    }

    [HttpPost]
    public async Task<IActionResult> CreateTenant(CancellationToken ct)
    {
        RequirePlatformAdmin();
        var request = await Request.ReadValidatedJsonBodyAsync(createTenantValidator, ct);
        return JsonResponses.Ok(await appService.CreateTenantAsync(request, ct), HttpStatusCode.Created);
    }

    [HttpPatch("{tenantId}")]
    public async Task<IActionResult> UpdateTenant(CancellationToken ct)
    {
        RequirePlatformAdmin();
        var request = await Request.ReadValidatedJsonBodyAsync(updateTenantValidator, ct);
        return JsonResponses.Ok(await appService.UpdateTenantAsync(RouteParam("tenantId"), request, ct));
    }

    [HttpPost("{tenantId}/api-keys")]
    public async Task<IActionResult> CreateApiKey(CancellationToken ct)
    {
        RequirePlatformAdmin();
        var tenantId = RouteParam("tenantId");
        return JsonResponses.Ok(await appService.CreateApiKeyAsync(tenantId, ct), HttpStatusCode.Created);
    }

    [HttpGet("{tenantId}/api-keys")]
    public async Task<IActionResult> ListApiKeys(CancellationToken ct)
    {
        RequirePlatformAdmin();
        var tenantId = RouteParam("tenantId");
        return JsonResponses.Ok(await appService.ListApiKeysAsync(tenantId, ReadPageRequest(), ct));
    }

    [HttpDelete("{tenantId}/api-keys/{keyId}")]
    public async Task<IActionResult> RevokeApiKey(CancellationToken ct)
    {
        RequirePlatformAdmin();
        var tenantId = RouteParam("tenantId");
        var keyId = RouteGuidParam("keyId");
        await appService.RevokeApiKeyAsync(tenantId, keyId, ct);
        return JsonResponses.NoContent();
    }

    [HttpPost("{tenantId}/admin-users")]
    public async Task<IActionResult> CreateAdminUser(CancellationToken ct)
    {
        RequirePlatformAdmin();
        var tenantId = RouteParam("tenantId");
        var request = await Request.ReadValidatedJsonBodyAsync(createAdminUserValidator, ct);
        return JsonResponses.Ok(await appService.CreateAdminUserAsync(tenantId, request, ct), HttpStatusCode.Created);
    }

    [HttpGet("{tenantId}/admin-users")]
    public async Task<IActionResult> ListAdminUsers(CancellationToken ct)
    {
        RequirePlatformAdmin();
        var tenantId = RouteParam("tenantId");
        return JsonResponses.Ok(await appService.ListAdminUsersAsync(tenantId, ReadPageRequest(), ct));
    }
}
