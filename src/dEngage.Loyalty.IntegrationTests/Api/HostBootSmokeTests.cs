using dEngage.Loyalty.IntegrationTests.Fixtures;
using FluentAssertions;
using Xunit;

namespace dEngage.Loyalty.IntegrationTests.Api;

// Proves the test host (Nancy-on-ASP.NET-Core, Sqlite in-memory, fake publisher/rate-limiter)
// actually boots before any real Tiqmo-scenario test is written against it.
public sealed class HostBootSmokeTests : IClassFixture<CustomWebApplicationFactory>, IAsyncLifetime
{
    private readonly CustomWebApplicationFactory _factory;

    public HostBootSmokeTests(CustomWebApplicationFactory factory) => _factory = factory;

    public Task InitializeAsync() => _factory.EnsureDbCreatedAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Unauthenticated_request_to_a_tenant_scoped_route_is_rejected_not_500()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/v1/tenants/fintech/events/types");

        // No bearer token / api key supplied — the framework's auth guard should reject this
        // cleanly (401/403), which proves the whole DI graph (DbContext, Nancy bootstrapper,
        // fake publisher/rate-limiter) wired up without throwing during startup.
        ((int)response.StatusCode).Should().BeOneOf(401, 403);
    }
}
