using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using dEngage.Loyalty.Api.Client;
using dEngage.Loyalty.Api.Framework.Auth;
using dEngage.Loyalty.Api.Framework.Json;
using dEngage.Loyalty.Api.Platform;
using dEngage.Loyalty.Contract;
using dEngage.Loyalty.IntegrationTests.Fixtures;
using dEngage.Loyalty.Schema.Entities;
using dEngage.Loyalty.Shared;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace dEngage.Loyalty.IntegrationTests.Api;

// dEngage.Loyalty.Api.Client against the real Public.Api host: results instead of exceptions for
// HTTP errors, the API's error body, idempotency keys, and the wire format (camelCase, decimals as
// strings, escaped path segments, base addresses with a path prefix).
public sealed class LoyaltyApiClientTests : IClassFixture<CustomWebApplicationFactory>, IAsyncLifetime
{
    private const string TenantSlug = "api-client";

    private readonly CustomWebApplicationFactory _factory;
    private string _apiKey = "";

    public LoyaltyApiClientTests(CustomWebApplicationFactory factory) => _factory = factory;

    public async Task InitializeAsync()
    {
        await _factory.EnsureDbCreatedAsync();
        await using (var db = _factory.CreateDbContext())
        {
            if (!db.Tenants.Any(t => t.Slug == TenantSlug))
            {
                db.Tenants.Add(new Tenant { Id = Guid.NewGuid(), Slug = TenantSlug, Name = TenantSlug, Status = TenantStatus.Active, CreatedAt = DateTime.UtcNow });
                await db.SaveChangesAsync();
            }
        }
        _apiKey = await IssueApiKeyAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Publishes_an_event_and_uses_the_idempotency_key_as_the_event_id()
    {
        var client = Client(_apiKey);
        var key = $"order-{Guid.NewGuid():N}";

        var result = await client.PublishOrderCreatedAsync(TenantSlug,
            new OrderCreatedRequest("c-1", 10.5m, "web", null, [new OrderItemDto("sku-1", "shoes", 1m, 10.5m)]), key);

        result.IsSuccess.Should().BeTrue();
        result.StatusCode.Should().Be(202);
        result.Value!.EventId.Should().Be(key);
        var envelope = _factory.EventPublisher.Published.Single(e => e.EventId == key);
        envelope.EventType.Should().Be(EventTypes.OrderCreated);
        envelope.Data.GetProperty("amount").GetString().Should().Be("10.5");
    }

    [Fact]
    public async Task A_validation_failure_comes_back_as_the_apis_error_body()
    {
        var result = await Client(_apiKey).PublishOrderCreatedAsync(TenantSlug, new OrderCreatedRequest("", 0m, null, null, null));

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(400);
        result.Value.Should().BeNull();
        result.Problem!.Code.Should().Be("invalid_request");
        result.Problem.Errors!.Keys.Should().Contain(["ContactKey", "Amount"]);
        result.Problem.TraceId.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task An_unknown_api_key_is_unauthorized()
    {
        var result = await Client("nope.nope").PublishCashAddedAsync(TenantSlug, new CashAddedRequest("c-1", 5m, Guid.NewGuid()));

        result.StatusCode.Should().Be(401);
        result.Problem!.Code.Should().Be("unauthorized");
    }

    [Fact]
    public async Task An_unknown_event_is_not_found()
    {
        var result = await Client(_apiKey).GetEventStatusAsync(TenantSlug, "missing-event");

        result.StatusCode.Should().Be(404);
        result.Problem!.Code.Should().Be("not_found");
    }

    [Fact]
    public async Task A_tenant_without_a_configured_key_fails_before_any_request()
    {
        var handler = new RecordingHandler();
        var client = new LoyaltyApiClient(new HttpClient(handler) { BaseAddress = new Uri("http://loyalty/") },
            new StaticLoyaltyApiKeyProvider(new Dictionary<string, string>()));

        var act = () => client.PublishCashSpentAsync(TenantSlug, new CashSpentRequest("c-1", 5m, Guid.NewGuid()));

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*No loyalty API key*");
        handler.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task Registration_keeps_the_base_path_and_the_wire_format_matches_the_api()
    {
        var handler = new RecordingHandler();
        var services = new ServiceCollection();
        services.AddLoyaltyApiClient("http://gateway.local/loyalty-public",
                new Dictionary<string, string> { ["tenant a"] = "key-a" })
            .ConfigurePrimaryHttpMessageHandler(() => handler);
        var client = services.BuildServiceProvider().GetRequiredService<ILoyaltyApiClient>();

        await client.PublishPointsTransferAsync("tenant a", new PointsTransferRequest("c-1", "c-2", 1234.5678m, Guid.Empty), "idem-1");

        var request = handler.Requests.Single();
        request.Uri.Should().Be("http://gateway.local/loyalty-public/api/v1/tenants/tenant%20a/events/points-transfer");
        request.Headers["X-Api-Key"].Should().Be("key-a");
        request.Headers["Idempotency-Key"].Should().Be("idem-1");
        request.ContentType.Should().Be("application/json");
        request.Body.Should().Contain("\"contactKey\":\"c-1\"").And.Contain("\"amount\":\"1234.5678\"");
    }

    [Fact]
    public async Task A_response_that_is_not_the_apis_error_body_still_gives_a_problem()
    {
        var handler = new RecordingHandler(new HttpResponseMessage(HttpStatusCode.BadGateway)
        {
            ReasonPhrase = "Bad Gateway",
            Content = new StringContent("<html>upstream down</html>", Encoding.UTF8, "text/html")
        });
        var client = new LoyaltyApiClient(new HttpClient(handler) { BaseAddress = new Uri("http://loyalty/") },
            new StaticLoyaltyApiKeyProvider(new Dictionary<string, string> { [TenantSlug] = "k" }));

        var result = await client.GetEventStatusAsync(TenantSlug, "e-1");

        result.StatusCode.Should().Be(502);
        result.Problem!.Code.Should().Be("http_502");
        result.Problem.Title.Should().Be("Bad Gateway");
    }

    private ILoyaltyApiClient Client(string apiKey) =>
        new LoyaltyApiClient(_factory.PublicApi.CreateClient(),
            new StaticLoyaltyApiKeyProvider(new Dictionary<string, string> { [TenantSlug] = apiKey }));

    private async Task<string> IssueApiKeyAsync()
    {
        using var admin = _factory.CreateClient();
        admin.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            JwtIssuingHelper.IssuePlatformAdminToken(_factory.Services.GetRequiredService<IJwtTokenService>()));
        var response = await admin.PostAsync($"/api/v1/platform/tenants/{TenantSlug}/api-keys", null);
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<CreateApiKeyResponse>(JsonConventions.Options))!.RawKey;
    }

    private sealed record RecordedRequest(string Uri, Dictionary<string, string> Headers, string? ContentType, string Body);

    // Records what the client sends and answers with a fixed response (202 by default).
    private sealed class RecordingHandler(HttpResponseMessage? response = null) : HttpMessageHandler
    {
        public List<RecordedRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add(new RecordedRequest(
                request.RequestUri!.AbsoluteUri,
                request.Headers.ToDictionary(h => h.Key, h => string.Join(",", h.Value)),
                request.Content?.Headers.ContentType?.MediaType,
                request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct)));
            return response ?? new HttpResponseMessage(HttpStatusCode.Accepted)
            {
                Content = new StringContent("{\"eventId\":\"e-1\",\"status\":\"accepted\"}", Encoding.UTF8, "application/json")
            };
        }
    }
}
