using dEngage.Loyalty.Api.Framework.Auth;
using dEngage.Loyalty.Api.Framework.ErrorHandling;
using dEngage.Loyalty.Api.Framework.Tenancy;
using dEngage.Loyalty.Schema;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace dEngage.Loyalty.Api.Framework.Controllers;

// Runs for every request under "/api" (health checks and the platform's own endpoints are left
// alone) and wraps the controllers:
//   - assigns a correlation id and resolves whichever credential the caller presented (JWT bearer
//     for the portal, X-Api-Key for server-to-server ingestion) into one AuthenticatedPrincipal.
//     It never rejects a request by itself: controllers decide what auth an action requires via
//     LoyaltyControllerBase's guards, since public routes (login) share this pipeline;
//   - turns any exception into the API's ProblemDetails body (see ErrorResponseFactory), with the
//     detail in the title only in Development;
//   - gives unmatched routes (404) and wrong verbs (405) the same body shape.
public sealed class LoyaltyApiMiddleware(RequestDelegate next, IHostEnvironment environment)
{
    public const string TenantContextItem = "TenantContext";
    public const string CorrelationIdItem = "CorrelationId";

    public async Task InvokeAsync(HttpContext httpContext)
    {
        if (!httpContext.Request.Path.StartsWithSegments("/api"))
        {
            await next(httpContext);
            return;
        }

        var correlationId = httpContext.Request.Headers["X-Correlation-Id"].FirstOrDefault()
            ?? Guid.NewGuid().ToString("N");
        httpContext.Items[CorrelationIdItem] = correlationId;

        try
        {
            var principal = await ResolvePrincipalAsync(httpContext);
            var tenantContext = new TenantContext { CorrelationId = correlationId, Principal = principal };
            httpContext.Items[TenantContextItem] = tenantContext;
            httpContext.RequestServices.GetRequiredService<ITenantContextAccessor>().Current = tenantContext;

            await next(httpContext);

            if (!httpContext.Response.HasStarted
                && httpContext.Response.StatusCode is StatusCodes.Status404NotFound or StatusCodes.Status405MethodNotAllowed
                && httpContext.Response.ContentType != "application/problem+json")
            {
                var notFound = httpContext.Response.StatusCode == StatusCodes.Status404NotFound;
                await ErrorResponseFactory.FromStatus(
                        (System.Net.HttpStatusCode)httpContext.Response.StatusCode,
                        notFound ? "not_found" : "method_not_allowed",
                        notFound ? "The requested route was not found." : "This HTTP method is not allowed for this route.",
                        correlationId)
                    .WriteAsync(httpContext);
            }
        }
        catch (Exception ex) when (!httpContext.Response.HasStarted)
        {
            // No Response.Clear(): it would also drop headers set before the exception, such as
            // CORS, and a browser couldn't read the error body. Nothing has been written yet;
            // status and Content-Type are set by the error result.
            await ErrorResponseFactory.FromException(ex, correlationId, environment.IsDevelopment())
                .WriteAsync(httpContext);
        }
    }

    private static async Task<AuthenticatedPrincipal?> ResolvePrincipalAsync(HttpContext httpContext)
    {
        var provider = httpContext.RequestServices;
        var authHeader = httpContext.Request.Headers.Authorization.FirstOrDefault();
        var apiKeyHeader = httpContext.Request.Headers["X-Api-Key"].FirstOrDefault();

        // Only a host that registered JWT (AddLoyaltyJwtAuthentication: the admin Api) accepts a
        // bearer token; Public.Api ignores one and authenticates by API key alone.
        var jwt = provider.GetService<IJwtTokenService>();
        if (jwt is not null && !string.IsNullOrEmpty(authHeader) && authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            var token = authHeader["Bearer ".Length..].Trim();
            return jwt.ValidateToken(token);
        }

        if (!string.IsNullOrEmpty(apiKeyHeader))
            return await ResolveApiKeyAsync(provider, apiKeyHeader, httpContext.RequestAborted);

        return null;
    }

    private static async Task<AuthenticatedPrincipal?> ResolveApiKeyAsync(
        IServiceProvider provider, string rawKey, CancellationToken ct)
    {
        var prefix = rawKey.Split('.', 2)[0];
        var db = provider.GetRequiredService<LoyaltyDbContext>();

        var key = await db.TenantApiKeys.AsNoTracking().Include(x => x.Tenant)
            .FirstOrDefaultAsync(x => x.KeyPrefix == prefix && x.RevokedAt == null, ct);
        if (key is null)
            return null;

        var hasher = provider.GetRequiredService<IPasswordService>();
        if (!hasher.Verify(key.HashedKey, rawKey))
            return null;

        await db.TenantApiKeys.Where(x => x.Id == key.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.LastUsedAt, DateTime.UtcNow), ct);

        return new AuthenticatedPrincipal
        {
            Kind = PrincipalKind.ApiKey,
            Role = "api_key",
            TenantId = key.Tenant.Slug,
            SubjectId = key.Id.ToString()
        };
    }
}
