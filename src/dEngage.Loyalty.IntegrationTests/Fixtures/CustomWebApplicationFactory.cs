using dEngage.Loyalty.Api.Framework.Messaging;
using dEngage.Loyalty.Api.Framework.RateLimiting;
using dEngage.Loyalty.Schema;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.EntityFrameworkCore;
using Moq;
using StackExchange.Redis;

namespace dEngage.Loyalty.IntegrationTests.Fixtures;

// Boots the real Api host (Nancy-on-ASP.NET-Core, per Program.cs) against an in-memory
// Sqlite database instead of Postgres, with RabbitMQ publishing and rate limiting swapped
// for in-process fakes — no external services required to run these tests.
//
// LoyaltyDbContext is registered upstream via a factory-based AddDbContext (options resolve
// ApiOptions at configure-time), which also registers a non-generic DbContextOptions service;
// both must be removed before re-adding the Sqlite-backed registration.
public sealed class CustomWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    public FakeEventPublisher EventPublisher { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                // ApiOptionsValidator only requires these to be non-blank — the real
                // connection is never opened since the DbContext registration is replaced below.
                ["ConnectionString"] = "unused-sqlite-in-memory-is-used-instead",
                ["RedisConnectionString"] = "unused-fake-rate-limiter-is-used-instead",
                // JwtOptionsValidator requires >= 32 chars.
                ["Jwt:SigningKey"] = "test-signing-key-not-for-production-32chars-min",
            });
        });

        builder.ConfigureServices(services =>
        {
            _connection.Open();

            services.RemoveAll<DbContextOptions<LoyaltyDbContext>>();
            services.RemoveAll<DbContextOptions>();
            services.AddDbContext<LoyaltyDbContext>(options => options.UseSqlite(_connection));

            services.RemoveAll<IEventPublisher>();
            services.AddSingleton<IEventPublisher>(EventPublisher);

            services.RemoveAll<IRateLimiter>();
            services.AddSingleton<IRateLimiter, FakeRateLimiter>();

            // Nancy's RouteCache resolves every module (and their app-service dependency
            // graphs) eagerly at host startup — several RuleEngine cache services inject
            // IConnectionMultiplexer directly, not just through IRateLimiter, so it must be
            // faked too or the real ConnectionMultiplexer.Connect(...) call blows up the boot.
            // DefaultValue.Mock so GetDatabase() hands back a no-op IDatabase instead of null —
            // app services that refresh a cache after saving (e.g. IRuleCacheService) write to it.
            services.RemoveAll<IConnectionMultiplexer>();
            services.AddSingleton(new Mock<IConnectionMultiplexer> { DefaultValue = DefaultValue.Mock }.Object);
        });
    }

    public LoyaltyDbContext CreateDbContext()
    {
        var db = Services.GetRequiredService<IServiceScopeFactory>().CreateScope().ServiceProvider
            .GetRequiredService<LoyaltyDbContext>();
        return db;
    }

    public async Task EnsureDbCreatedAsync()
    {
        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LoyaltyDbContext>();
        await db.Database.EnsureCreatedAsync();
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing) _connection.Dispose();
    }
}
