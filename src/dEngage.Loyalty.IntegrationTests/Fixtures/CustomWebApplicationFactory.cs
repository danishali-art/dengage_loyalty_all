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
using Microsoft.EntityFrameworkCore.Infrastructure;
using Moq;
using StackExchange.Redis;

namespace dEngage.Loyalty.IntegrationTests.Fixtures;

// Boots the real Api host (ASP.NET Core controllers, per Program.cs) against an in-memory
// Sqlite database instead of Postgres, with RabbitMQ publishing and rate limiting swapped
// for in-process fakes — no external services required to run these tests.
//
// LoyaltyDbContext is registered upstream via a factory-based AddDbContext (options resolve
// ApiOptions at configure-time), which also registers a non-generic DbContextOptions service;
// both must be removed before re-adding the Sqlite-backed registration. Since EF Core 9 the
// upstream UseNpgsql(...) action is also kept as an IDbContextOptionsConfiguration<T>, which
// must go too — otherwise both providers end up configured and EF refuses to start.
public sealed class CustomWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private PublicApiWebApplicationFactory? _publicApi;
    public FakeEventPublisher EventPublisher { get; } = new();
    public FakeRateLimiter RateLimiter { get; } = new();

    // The ingestion routes live in Public.Api (platform alignment D9). This second host shares
    // this factory's database, event publisher and rate limiter, so a test can issue an API key
    // through the admin Api and post events with it.
    public PublicApiWebApplicationFactory PublicApi => _publicApi ??= new PublicApiWebApplicationFactory(this);

    internal SqliteConnection Connection => _connection;

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
            services.RemoveAll<IDbContextOptionsConfiguration<LoyaltyDbContext>>();
            services.AddDbContext<LoyaltyDbContext>(options => options.UseSqlite(_connection)
                .AddInterceptors(new TenantSettingsSeedingInterceptor()));

            services.RemoveAll<IEventPublisher>();
            services.AddSingleton<IEventPublisher>(EventPublisher);

            services.RemoveAll<IRateLimiter>();
            services.AddSingleton<IRateLimiter>(RateLimiter);

            // Several RuleEngine cache services inject IConnectionMultiplexer directly, not just
            // through IRateLimiter, so it must be faked too or the real
            // ConnectionMultiplexer.Connect(...) call fails as soon as a controller needs one.
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
        // EnsureCreated doesn't run migrations, so seed loyalty_config here: the app has no
        // defaults of its own and fails on a missing setting (alignment D4).
        await LoyaltyConfigDefaults.EnsureSeededAsync(db);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _publicApi?.Dispose();
        base.Dispose(disposing);
        if (disposing) _connection.Dispose();
    }
}
