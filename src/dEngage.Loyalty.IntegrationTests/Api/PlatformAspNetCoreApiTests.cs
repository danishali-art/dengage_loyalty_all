using System.Net;
using System.Text.Json;
using dEngage.Loyalty.IntegrationTests.Fixtures;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace dEngage.Loyalty.IntegrationTests.Api;

// The HTTP hosts run on the platform's dEngage.Common.AspNetCore setup. What loyalty changes on it:
// errors keep the API's own body (the platform's { message } exception filter is removed), Swagger
// only when SwaggerSettings:IsActive, and error responses keep their CORS headers.
public sealed class PlatformAspNetCoreApiTests : IClassFixture<CustomWebApplicationFactory>, IAsyncLifetime
{
    private readonly CustomWebApplicationFactory _factory;

    public PlatformAspNetCoreApiTests(CustomWebApplicationFactory factory) => _factory = factory;

    public Task InitializeAsync() => _factory.EnsureDbCreatedAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task An_error_thrown_in_an_action_keeps_the_apis_error_body()
    {
        using var client = _factory.CreateClient();

        // Unauthenticated: the action's guard throws.
        var response = await client.GetAsync("/api/v1/tenants/fintech/programs");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        body.RootElement.GetProperty("code").GetString().Should().Be("unauthorized");
        body.RootElement.TryGetProperty("message", out _).Should().BeFalse("the platform's { message } body is not used");
    }

    [Fact]
    public async Task Swagger_is_off_unless_enabled()
    {
        using var client = _factory.CreateClient();
        (await client.GetAsync("/swagger/v1/swagger.json")).StatusCode.Should().Be(HttpStatusCode.NotFound);

        using var enabled = _factory.WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, config) =>
            config.AddInMemoryCollection(new Dictionary<string, string?> { ["SwaggerSettings:IsActive"] = "true" })));
        var spec = await enabled.CreateClient().GetAsync("/swagger/v1/swagger.json");

        spec.StatusCode.Should().Be(HttpStatusCode.OK);
        (await spec.Content.ReadAsStringAsync()).Should().Contain("/api/v1/tenants/{tenantId}/programs");
    }

    [Fact]
    public async Task An_error_response_keeps_its_cors_headers()
    {
        const string origin = "https://portal.example.com";
        using var factory = _factory.WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, config) =>
            config.AddInMemoryCollection(new Dictionary<string, string?> { ["Cors:AllowedOrigins:0"] = origin })));
        using var client = factory.CreateClient();

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/tenants/fintech/programs");
        request.Headers.Add("Origin", origin);
        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        response.Headers.GetValues("Access-Control-Allow-Origin").Should().Equal(origin);
    }
}
