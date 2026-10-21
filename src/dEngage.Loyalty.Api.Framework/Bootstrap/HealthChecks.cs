using dEngage.Loyalty.Schema;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace dEngage.Loyalty.Api.Framework.Bootstrap;

// Plain ASP.NET Core minimal endpoints, deliberately outside "/api" (and so outside
// LoyaltyApiMiddleware) — a load balancer / orchestrator probe shouldn't be tenant/auth-scoped.
// Shared by the admin Api and Public.Api.
public static class HealthChecks
{
    public static void MapLoyaltyHealthChecks(this IEndpointRouteBuilder app)
    {
        app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

        // The probe is unauthenticated, so the response only says which dependency failed. The
        // exception text (host names, connection details) goes to the log, not to the caller.
        app.MapGet("/health/ready", async (LoyaltyDbContext db, dEngage.Loyalty.Api.Framework.Messaging.IRabbitMqConnectionHolder rabbit, IConnectionMultiplexer redis, ILoggerFactory loggerFactory) =>
        {
            var logger = loggerFactory.CreateLogger(typeof(HealthChecks));
            var checks = new Dictionary<string, string>();

            try
            {
                await db.Database.ExecuteSqlRawAsync("SELECT 1");
                checks["postgres"] = "ok";
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Readiness check failed: postgres");
                checks["postgres"] = "error";
            }

            try
            {
                await redis.GetDatabase().PingAsync();
                checks["redis"] = "ok";
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Readiness check failed: redis");
                checks["redis"] = "error";
            }

            checks["rabbitmq"] = rabbit.Connection.IsOpen ? "ok" : "error";

            var healthy = checks.Values.All(v => v == "ok");
            return Results.Json(new { status = healthy ? "ready" : "not_ready", checks }, statusCode: healthy ? 200 : 503);
        });
    }
}
