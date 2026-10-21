using System.Net;
using dEngage.Loyalty.IntegrationTests.Fixtures;
using FluentAssertions;
using Xunit;

namespace dEngage.Loyalty.IntegrationTests.Api;

// The Api runs on the dEngage platform host (DengageWebApplicationBuilder, alignment D1). That host
// adds /health-check, /restart and debug-view to every app. /restart stops the process and
// debug-view prints the configuration, so outside Development Program.cs must mark the app as
// external (CommonConstants.MarkAsExternal) — the API is reached from browsers and client systems.
// The test host runs as "Testing", i.e. not Development.
public sealed class PlatformHostingTests : IClassFixture<CustomWebApplicationFactory>, IAsyncLifetime
{
    private readonly CustomWebApplicationFactory _factory;

    public PlatformHostingTests(CustomWebApplicationFactory factory) => _factory = factory;

    public Task InitializeAsync() => _factory.EnsureDbCreatedAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task The_platform_health_check_answers()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/health-check");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Restart_is_not_available_outside_development()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/restart");
        var body = await response.Content.ReadAsStringAsync();

        body.Should().NotContain("Service is stopping");
        response.StatusCode.Should().NotBe(HttpStatusCode.OK);

        // The host is still serving after the attempt.
        (await client.GetAsync("/health-check")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Debug_view_is_not_available_outside_development()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/debug-view");

        response.StatusCode.Should().NotBe(HttpStatusCode.OK);
    }
}
