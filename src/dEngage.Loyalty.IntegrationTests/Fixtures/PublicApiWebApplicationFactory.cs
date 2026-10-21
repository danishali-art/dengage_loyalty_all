using dEngage.Loyalty.Api.Framework.Messaging;
using dEngage.Loyalty.Api.Framework.RateLimiting;
using dEngage.Loyalty.Schema;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Moq;
using StackExchange.Redis;
using PublicApiEntryPoint = dEngage.Loyalty.Public.Api.Events.EventsController;

namespace dEngage.Loyalty.IntegrationTests.Fixtures;

// Boots the real Public.Api host (ingestion, API keys only) against the admin Api factory's
// in-memory Sqlite database, with its event publisher and rate limiter, so both hosts see the same
// tenants, API keys and published events. Created through CustomWebApplicationFactory.PublicApi.
//
// The entry point is a public type of the Public.Api assembly rather than its Program class:
// both hosts have a top-level Program, and Public.Api's is internal.
public sealed class PublicApiWebApplicationFactory(CustomWebApplicationFactory api)
    : WebApplicationFactory<PublicApiEntryPoint>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                // ApiOptionsValidator only requires these to be non-blank; the DbContext and the
                // Redis-backed services are replaced below.
                ["ConnectionString"] = "unused-sqlite-in-memory-is-used-instead",
                ["RedisConnectionString"] = "unused-fake-rate-limiter-is-used-instead",
                // The broker connection is required at startup (BrokerConnectionSettings); never
                // opened, since the event publisher is replaced by a fake.
                ["RabbitMq:Host"] = "unused-fake-event-publisher-is-used-instead",
                ["RabbitMq:User"] = "unused",
                ["RabbitMq:Password"] = "unused",
            });
        });

        builder.ConfigureServices(services =>
        {
            // Same replacement as CustomWebApplicationFactory (see there for why all three go).
            services.RemoveAll<DbContextOptions<LoyaltyDbContext>>();
            services.RemoveAll<DbContextOptions>();
            services.RemoveAll<IDbContextOptionsConfiguration<LoyaltyDbContext>>();
            services.AddDbContext<LoyaltyDbContext>(options => options.UseSqlite(api.Connection)
                .AddInterceptors(new TenantSettingsSeedingInterceptor()));

            services.RemoveAll<IEventPublisher>();
            services.AddSingleton<IEventPublisher>(api.EventPublisher);

            services.RemoveAll<IRateLimiter>();
            services.AddSingleton<IRateLimiter>(api.RateLimiter);

            services.RemoveAll<IConnectionMultiplexer>();
            services.AddSingleton(new Mock<IConnectionMultiplexer> { DefaultValue = DefaultValue.Mock }.Object);
        });
    }
}
