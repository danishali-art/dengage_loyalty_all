using dEngage.Loyalty.Schema;
using Microsoft.EntityFrameworkCore;
using StackExchange.Redis;

namespace dEngage.Loyalty.Api;

// Plain ASP.NET Core minimal endpoints, deliberately outside the Nancy bridge (only paths under
// "/api" are routed into Nancy) — a load balancer / orchestrator probe shouldn't depend on the
// Nancy engine being warmed up, and shouldn't be tenant/auth-scoped.
public static class HealthChecks
{
    public static void MapHealthChecks(this WebApplication app)
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
