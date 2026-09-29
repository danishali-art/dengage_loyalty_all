using dEngage.Loyalty.Api.Framework.Auth;
using dEngage.Loyalty.Api.Framework.ErrorHandling;
using dEngage.Loyalty.Api.Framework.Pagination;
using dEngage.Loyalty.Api.Framework.Tenancy;
using Nancy;

namespace dEngage.Loyalty.Api.Framework.Modules;

// Base class for every business Nancy module — thin, deliberately: input validation happens via
// FluentValidation validators (called explicitly per route), business logic lives in app services,
// and this class only carries the auth/tenant guard helpers shared by all of them (plan §1, §7).
//
// Map* methods concatenate basePath + relativePath themselves rather than passing basePath to the
// NancyModule(string) constructor — verified empirically that Nancy's own ModulePath prefixing
// does not apply to routes registered via the modern Get(path, handler) method form (only the
// classic Get["/path"] indexer syntax did, in earlier Nancy versions), so relying on it silently
// produces routes that never match anything.
public abstract class TenantScopedModule : NancyModule
{
    private readonly string _basePath;

    protected TenantScopedModule(string basePath)
    {
        _basePath = basePath.TrimEnd('/');
    }

    protected void MapGet(string relativePath, Func<dynamic, CancellationToken, Task<object>> handler) =>
        Get(_basePath + relativePath, handler);

    protected void MapPost(string relativePath, Func<dynamic, CancellationToken, Task<object>> handler) =>
        Post(_basePath + relativePath, handler);

    protected void MapPatch(string relativePath, Func<dynamic, CancellationToken, Task<object>> handler) =>
        Patch(_basePath + relativePath, handler);

    protected void MapDelete(string relativePath, Func<dynamic, CancellationToken, Task<object>> handler) =>
        Delete(_basePath + relativePath, handler);

    protected TenantContext TenantContext =>
        (TenantContext)Context.Items["TenantContext"];

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

    protected string RouteParam(dynamic parameters, string name)
    {
        var value = (string?)parameters[name];
        if (string.IsNullOrWhiteSpace(value))
            throw new ValidationApiException($"Route parameter '{name}' is required.");
        return value;
    }

    protected Guid RouteGuidParam(dynamic parameters, string name)
    {
        string raw = RouteParam(parameters, name);
        if (!Guid.TryParse(raw, out var value))
            throw new ValidationApiException($"Route parameter '{name}' must be a valid identifier.");
        return value;
    }
}
