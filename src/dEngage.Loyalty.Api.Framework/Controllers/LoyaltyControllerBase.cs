using dEngage.Loyalty.Api.Framework.Auth;
using dEngage.Loyalty.Api.Framework.ErrorHandling;
using dEngage.Loyalty.Api.Framework.Pagination;
using dEngage.Loyalty.Api.Framework.Tenancy;
using Microsoft.AspNetCore.Mvc;

namespace dEngage.Loyalty.Api.Framework.Controllers;

// Base class for every business controller (platform alignment D7: Nancy → ASP.NET Core
// controllers). Thin, deliberately: input validation happens via FluentValidation validators
// (called explicitly per action), business logic lives in app services, and this class only
// carries the auth/tenant guard helpers shared by all of them.
//
// Deliberately not [ApiController] and no model binding: request bodies are read and validated
// explicitly (ReadValidatedJsonBodyAsync) and route values through RouteParam/RouteGuidParam, so
// malformed input gets the same ProblemDetails bodies the API has always returned, rather than
// MVC's automatic 400 responses.
public abstract class LoyaltyControllerBase : ControllerBase
{
    protected TenantContext TenantContext =>
        (TenantContext)HttpContext.Items[LoyaltyApiMiddleware.TenantContextItem]!;

    protected AuthenticatedPrincipal RequireAuthenticated()
    {
        return TenantContext.Principal ?? throw new UnauthorizedApiException();
    }

    // For routes under /tenants/{tenantId}/... — verifies the caller's principal may act on the
    // path's tenant (platform_admin may act on any tenant; everyone else must match exactly).
    protected string RequireTenantScope(string routeTenantId)
    {
        var principal = RequireAuthenticated();
        if (!principal.CanActOnTenant(routeTenantId))
            throw new ForbiddenApiException($"Principal is not authorized for tenant '{routeTenantId}'.");

        TenantContext.RouteTenantId = routeTenantId;
        return routeTenantId;
    }

    protected void RequirePlatformAdmin()
    {
        var principal = RequireAuthenticated();
        if (!principal.IsPlatformAdmin)
            throw new ForbiddenApiException("This action requires platform_admin.");
    }

    protected void RequireApiKeyPrincipal()
    {
        var principal = RequireAuthenticated();
        if (principal.Kind != PrincipalKind.ApiKey)
            throw new ForbiddenApiException("This route requires API-key authentication.");
    }

    protected PageRequest ReadPageRequest()
    {
        int? page = int.TryParse((string?)Request.Query["page"], out var p) ? p : null;
        int? pageSize = int.TryParse((string?)Request.Query["pageSize"], out var ps) ? ps : null;
        return PageRequest.From(page, pageSize);
    }

    protected string RouteParam(string name)
    {
        var value = RouteData.Values[name] as string;
        if (string.IsNullOrWhiteSpace(value))
            throw new ValidationApiException($"Route parameter '{name}' is required.");
        return value;
    }

    protected Guid RouteGuidParam(string name)
    {
        var raw = RouteParam(name);
        if (!Guid.TryParse(raw, out var value))
            throw new ValidationApiException($"Route parameter '{name}' must be a valid identifier.");
        return value;
    }
}
