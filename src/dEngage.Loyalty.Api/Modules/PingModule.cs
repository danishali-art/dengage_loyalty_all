using dEngage.Loyalty.Api.Framework.Json;
using dEngage.Loyalty.Schema;
using Microsoft.EntityFrameworkCore;
using Nancy;

namespace dEngage.Loyalty.Api.Modules;

// Smoke-test route proving the ASP.NET Core -> Nancy bridge actually dispatches requests end to
// end, including constructor-injected scoped dependencies (LoyaltyDbContext). Safe to remove once
// real business modules are in place and verified the same way.
public sealed class PingModule : NancyModule
{
    public PingModule(LoyaltyDbContext db)
    {
        Get("/api/v1/ping", (_, _) => Task.FromResult<object>(
            JsonResponses.Ok(new { pong = true, at = DateTime.UtcNow })));

        Get("/api/v1/ping/db", async (_, ct) =>
        {
            var tenantCount = await db.Tenants.CountAsync(ct);
            return JsonResponses.Ok(new { tenantCount });
        });
    }
}
