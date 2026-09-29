using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using dEngage.Loyalty.Api.Framework.Auth;
using dEngage.Loyalty.Api.Framework.Json;
using dEngage.Loyalty.Api.Framework.Pagination;
using dEngage.Loyalty.Api.Rules;
using dEngage.Loyalty.IntegrationTests.Fixtures;
using dEngage.Loyalty.RuleEngine.Models;
using dEngage.Loyalty.Schema.Entities;
using dEngage.Loyalty.Shared;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using AccountTypeEntity = dEngage.Loyalty.Schema.Entities.AccountType;
using ProgramEntity = dEngage.Loyalty.Schema.Entities.Program;

namespace dEngage.Loyalty.IntegrationTests.Api;

// 1.3.CL items 5–6: exclusivity groups and multiplier stacking are retired on the Rules API, and
// the list's `stackable` flag / ?stackable= filter back the portal's new Stackable column.
public sealed class RuleStackingCl13ApiTests : IClassFixture<CustomWebApplicationFactory>, IAsyncLifetime
{
    private readonly CustomWebApplicationFactory _factory;
    private readonly HttpClient _client;
    private const string TenantSlug = "cl13-rules";
    private Guid _programId;
    private Guid _pointsId;

    public RuleStackingCl13ApiTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    public async Task InitializeAsync()
    {
        await _factory.EnsureDbCreatedAsync();
        await using (var db = _factory.CreateDbContext())
        {
            var tenant = db.Tenants.SingleOrDefault(t => t.Slug == TenantSlug);
            if (tenant is null)
            {
                tenant = new Tenant { Id = Guid.NewGuid(), Slug = TenantSlug, Name = TenantSlug, Status = TenantStatus.Active, CreatedAt = DateTime.UtcNow };
                db.Tenants.Add(tenant);
            }
            _programId = Guid.NewGuid();
            _pointsId = Guid.NewGuid();
            db.Programs.Add(new ProgramEntity { Id = _programId, TenantId = tenant.Id, Name = "rules", Status = ProgramStatus.Inactive, CreatedAt = DateTime.UtcNow });
            db.AccountTypes.Add(new AccountTypeEntity { Id = _pointsId, TenantId = tenant.Id, ProgramId = _programId, Type = "POINTS", Name = "Points", Config = """{"decimals": 0}""", CreatedAt = DateTime.UtcNow });
            await db.SaveChangesAsync();
        }

        var jwt = _factory.Services.GetRequiredService<IJwtTokenService>();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", JwtIssuingHelper.IssuePlatformAdminToken(jwt));
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private string RulesUrl => $"/api/v1/tenants/{TenantSlug}/programs/{_programId}/rules";

    private static StringContent Json(object body) => new(
        System.Text.Json.JsonSerializer.Serialize(body, JsonConventions.Options), System.Text.Encoding.UTF8, "application/json");

    private CreateRuleRequest Spend(bool stackable, string? group = null, string? stackMode = null) => new(
        $"spend-{Guid.NewGuid():N}"[..20], "remittance", _pointsId, RuleTypes.SpendRule,
        new RuleCalculation { Factor = 0.1m }, null, null, 10, stackable, group, stackMode, null, null, null);

    [Fact]
    public async Task An_exclusive_rule_no_longer_needs_an_exclusivity_group()
    {
        var response = await _client.PostAsync(RulesUrl, Json(Spend(stackable: false)));
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());

        var rule = await response.Content.ReadFromJsonAsync<RuleResponse>(JsonConventions.Options);
        rule!.Stackable.Should().BeFalse();
        rule.ExclusivityGroup.Should().BeNull();
        rule.StackMode.Should().Be(RuleStackMode.Additive);
    }

    [Theory]
    [InlineData(false, "earn-rate", null)]
    [InlineData(true, null, RuleStackMode.Multiplier)]
    [InlineData(true, "earn-rate", RuleStackMode.Additive)]
    public async Task Retired_stacking_fields_are_rejected_on_create(bool stackable, string? group, string? stackMode)
    {
        var response = await _client.PostAsync(RulesUrl, Json(Spend(stackable, group, stackMode)));
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("stacking_field_retired");
    }

    [Fact]
    public async Task Retired_stacking_fields_are_rejected_on_update()
    {
        var created = await (await _client.PostAsync(RulesUrl, Json(Spend(stackable: true))))
            .Content.ReadFromJsonAsync<RuleResponse>(JsonConventions.Options);

        var update = new UpdateRuleRequest(null, null, null, null, null, null, null, null, null, RuleStackMode.Multiplier, null, null, null);
        (await _client.PatchAsync($"{RulesUrl}/{created!.Id}", Json(update))).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task List_exposes_stackable_and_filters_on_it()
    {
        var stackable = await (await _client.PostAsync(RulesUrl, Json(Spend(stackable: true))))
            .Content.ReadFromJsonAsync<RuleResponse>(JsonConventions.Options);
        var exclusive = await (await _client.PostAsync(RulesUrl, Json(Spend(stackable: false))))
            .Content.ReadFromJsonAsync<RuleResponse>(JsonConventions.Options);

        var onlyStackable = await (await _client.GetAsync($"{RulesUrl}?stackable=true&pageSize=100"))
            .Content.ReadFromJsonAsync<PagedResult<RuleResponse>>(JsonConventions.Options);

        onlyStackable!.Data.Should().Contain(r => r.Id == stackable!.Id);
        onlyStackable.Data.Should().NotContain(r => r.Id == exclusive!.Id);
        onlyStackable.Data.Should().OnlyContain(r => r.Stackable);
    }
}
