using dEngage.Loyalty.Api.Framework.Controllers;
using dEngage.Loyalty.Api.Framework.Json;
using dEngage.Loyalty.Schema;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace dEngage.Loyalty.Api.Modules;

// Smoke-test routes proving requests under /api reach a controller end to end, including
// constructor-injected scoped dependencies (LoyaltyDbContext). Unauthenticated.
[Route("api/v1/ping")]
public sealed class PingController(LoyaltyDbContext db) : LoyaltyControllerBase
{
    [HttpGet]
    public IActionResult Ping() => JsonResponses.Ok(new { pong = true, at = DateTime.UtcNow });

    [HttpGet("db")]
    public async Task<IActionResult> Db(CancellationToken ct)
    {
        var tenantCount = await db.Tenants.CountAsync(ct);
        return JsonResponses.Ok(new { tenantCount });
    }
}
