using dEngage.Loyalty.Api.Framework.Auth;
using dEngage.Loyalty.Api.Framework.Tenancy;
using dEngage.Loyalty.Schema;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nancy;

namespace dEngage.Loyalty.Api.Framework.Bootstrap;

// Global Before hook: assigns a correlation id, resolves whichever credential the caller presented
// (JWT bearer for the dashboard, X-Api-Key for server-to-server ingestion) into one shared
// AuthenticatedPrincipal shape, and stashes it on both NancyContext.Items and the ambient
// TenantContextAccessor. Never short-circuits on missing/invalid credentials by itself — routes
// decide what auth they require via TenantScopedModule's guard methods, since public routes
// (login, health) share this same pipeline.
public static class AuthenticationPipelineHook
{
    public static async Task<Response?> HandleAsync(NancyContext context, CancellationToken ct)
    {
        var correlationId = context.Request.Headers["X-Correlation-Id"].FirstOrDefault()
            ?? Guid.NewGuid().ToString("N");
        context.Items["CorrelationId"] = correlationId;

        var provider = AmbientServiceProviderAccessor.Current;
        if (provider is null)
        {
            context.Items["TenantContext"] = new TenantContext { CorrelationId = correlationId };
            return null;
        }

        AuthenticatedPrincipal? principal = null;

        var authHeader = context.Request.Headers.Authorization;
        var apiKeyHeader = context.Request.Headers["X-Api-Key"].FirstOrDefault();

        if (!string.IsNullOrEmpty(authHeader) && authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            var token = authHeader["Bearer ".Length..].Trim();
            principal = provider.GetRequiredService<IJwtTokenService>().ValidateToken(token);
        }
        else if (!string.IsNullOrEmpty(apiKeyHeader))
        {
            principal = await ResolveApiKeyAsync(provider, apiKeyHeader, ct);
        }

        var tenantContext = new TenantContext { CorrelationId = correlationId, Principal = principal };
        context.Items["TenantContext"] = tenantContext;
        provider.GetRequiredService<ITenantContextAccessor>().Current = tenantContext;

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
