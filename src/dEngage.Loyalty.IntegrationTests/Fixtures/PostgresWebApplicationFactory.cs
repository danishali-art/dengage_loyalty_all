using dEngage.Loyalty.Api.Framework.Messaging;
using dEngage.Loyalty.Api.Framework.RateLimiting;
using dEngage.Loyalty.Schema;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Moq;
using StackExchange.Redis;

namespace dEngage.Loyalty.IntegrationTests.Fixtures;

// Same shape as CustomWebApplicationFactory, but backs LoyaltyDbContext with a real Postgres
// (via Testcontainers) instead of Sqlite in-memory. Needed for any route whose app service does
// something Sqlite's EF Core provider cannot translate — e.g. decimal SUM/AVG aggregates, which
// Sqlite maps decimal to TEXT and refuses to aggregate over (Dashboard's TotalBalance sum is the
// case that surfaced this).
public sealed class PostgresWebApplicationFactory(string connectionString) : WebApplicationFactory<Program>
{
    public FakeEventPublisher EventPublisher { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionString"] = connectionString,
                ["RedisConnectionString"] = "unused-fake-rate-limiter-is-used-instead",
                ["Jwt:SigningKey"] = "test-signing-key-not-for-production-32chars-min",
            });
        });

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<DbContextOptions<LoyaltyDbContext>>();
            services.RemoveAll<DbContextOptions>();
            services.AddDbContext<LoyaltyDbContext>(options => options.UseNpgsql(connectionString));

            services.RemoveAll<IEventPublisher>();
            services.AddSingleton<IEventPublisher>(EventPublisher);

            services.RemoveAll<IRateLimiter>();
            services.AddSingleton<IRateLimiter, FakeRateLimiter>();

            services.RemoveAll<IConnectionMultiplexer>();
            // See CustomWebApplicationFactory — GetDatabase() must not return null.
            services.AddSingleton(new Mock<IConnectionMultiplexer> { DefaultValue = DefaultValue.Mock }.Object);
        });
    }

    public async Task MigrateDbAsync()
    {
        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LoyaltyDbContext>();
        await db.Database.MigrateAsync();
    }

    public LoyaltyDbContext CreateDbContext() =>
        Services.GetRequiredService<IServiceScopeFactory>().CreateScope().ServiceProvider.GetRequiredService<LoyaltyDbContext>();
}
