using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using dEngage.Loyalty.Api.Dashboard;
using dEngage.Loyalty.Api.Framework.Auth;
using dEngage.Loyalty.Api.Framework.Json;
using dEngage.Loyalty.Api.Programs;
using dEngage.Loyalty.IntegrationTests.Fixtures;
using dEngage.Loyalty.Schema.Entities;
using dEngage.Loyalty.Shared;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;
using Xunit;

namespace dEngage.Loyalty.IntegrationTests.E2E;

// Dashboard/summary sums CustomerAccounts.Balance (decimal) via SumAsync — Sqlite's EF Core
// provider cannot translate a decimal SUM aggregate (decimal is TEXT-mapped there), so this route
// needs a real Postgres to test over HTTP. Excluded from the default `dotnet test` run — requires
// Docker. Run explicitly with: dotnet test --filter Category=E2E
[Trait("Category", "E2E")]
public sealed class DashboardEndpointE2ETests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder().WithImage("postgres:16-alpine").Build();
    private PostgresWebApplicationFactory _factory = default!;
    private HttpClient _client = default!;
    private const string TenantSlug = "t1";

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        _factory = new PostgresWebApplicationFactory(_container.GetConnectionString());
        await _factory.MigrateDbAsync();

        await using (var db = _factory.CreateDbContext())
        {
            db.Tenants.Add(new Tenant { Id = Guid.NewGuid(), Slug = TenantSlug, Name = TenantSlug, Status = TenantStatus.Active, CreatedAt = DateTime.UtcNow });
            await db.SaveChangesAsync();
        }

        _client = _factory.CreateClient();
        var jwt = _factory.Services.GetRequiredService<IJwtTokenService>();
        var token = JwtIssuingHelper.IssuePlatformAdminToken(jwt);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
        await _container.DisposeAsync();
    }

    private static StringContent Json(object body) => new(
        System.Text.Json.JsonSerializer.Serialize(body, JsonConventions.Options), System.Text.Encoding.UTF8, "application/json");

    [Fact]
    public async Task Dashboard_summary_reflects_created_programs()
    {
        await _client.PostAsync($"/api/v1/tenants/{TenantSlug}/programs", Json(
            new CreateProgramRequest("Dashboard Test Program", null)));

        var response = await _client.GetAsync($"/api/v1/tenants/{TenantSlug}/dashboard/summary");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var summary = await response.Content.ReadFromJsonAsync<DashboardSummaryResponse>(JsonConventions.Options);

        summary!.ProgramCount.Should().Be(1);
        summary.TotalBalance.Should().Be(0m);
    }
}
