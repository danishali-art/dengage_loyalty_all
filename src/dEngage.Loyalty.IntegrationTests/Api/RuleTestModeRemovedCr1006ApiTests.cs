using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using dEngage.Loyalty.Api.Framework.Auth;
using dEngage.Loyalty.Api.Framework.Json;
using dEngage.Loyalty.Api.Rules;
using dEngage.Loyalty.IntegrationTests.Fixtures;
using dEngage.Loyalty.RuleEngine.Models;
using dEngage.Loyalty.Schema.Entities;
using dEngage.Loyalty.Shared;
using dEngage.Loyalty.Shared.Events;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using AccountTypeEntity = dEngage.Loyalty.Schema.Entities.AccountType;
using ProgramEntity = dEngage.Loyalty.Schema.Entities.Program;

namespace dEngage.Loyalty.IntegrationTests.Api;

// CR 2026-10-06 D20: test mode was removed from every rule. The field stays on the wire (existing
// fields are frozen), but only false is accepted, and a flag stored before is reported as false.
public sealed class RuleTestModeRemovedCr1006ApiTests : IClassFixture<CustomWebApplicationFactory>, IAsyncLifetime
{
    private readonly CustomWebApplicationFactory _factory;
    private readonly HttpClient _client;
    private const string TenantSlug = "cr1006-testmode";
    private Guid _tenantId;
    private Guid _programId;
    private Guid _pointsId;

    public RuleTestModeRemovedCr1006ApiTests(CustomWebApplicationFactory factory)
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
            _tenantId = tenant.Id;
            _programId = Guid.NewGuid();
            _pointsId = Guid.NewGuid();
            db.Programs.Add(new ProgramEntity { Id = _programId, TenantId = tenant.Id, Name = "testmode", Status = ProgramStatus.Inactive, CreatedAt = DateTime.UtcNow });
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

    private CreateRuleRequest Bonus(RuleSettings? configuration) => new(
        $"bonus-{Guid.NewGuid():N}"[..20], EventTypes.CardTransaction, _pointsId, RuleTypes.FixedBonusRule,
        new RuleCalculation { FixedValue = 10m }, null, null, 10, false, null, null, configuration, null, null);

    [Fact]
    public async Task A_new_rule_with_test_mode_on_is_rejected()
    {
        var response = await _client.PostAsync(RulesUrl, Json(Bonus(new RuleSettings { TestMode = true })));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("test_mode_removed");
    }

    [Fact]
    public async Task A_new_rule_with_test_mode_off_or_omitted_is_accepted()
    {
        var off = await _client.PostAsync(RulesUrl, Json(Bonus(new RuleSettings { TestMode = false })));
        var omitted = await _client.PostAsync(RulesUrl, Json(Bonus(null)));

        off.StatusCode.Should().Be(HttpStatusCode.Created, await off.Content.ReadAsStringAsync());
        omitted.StatusCode.Should().Be(HttpStatusCode.Created, await omitted.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task An_edit_that_turns_test_mode_on_is_rejected()
    {
        var created = await _client.PostAsync(RulesUrl, Json(Bonus(null)));
        var rule = await created.Content.ReadFromJsonAsync<RuleResponse>(JsonConventions.Options);

        var update = new UpdateRuleRequest(null, null, null, null, null, null, null, null, null, null,
            new RuleSettings { TestMode = true }, null, null);
        var response = await _client.PatchAsync($"{RulesUrl}/{rule!.Id}", Json(update));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("test_mode_removed");
    }

    [Fact]
    public async Task A_test_mode_flag_stored_before_is_reported_as_off()
    {
        var ruleId = Guid.NewGuid();
        await using (var db = _factory.CreateDbContext())
        {
            db.Rules.Add(new Rule
            {
                Id = ruleId, TenantId = _tenantId, ProgramId = _programId, Name = "legacy test mode",
                Type = RuleTypes.FixedBonusRule, Trigger = EventTypes.CardTransaction, TargetAccountTypeId = _pointsId,
                Calculation = """{"amount":10}""", Configuration = """{"posting":"Immediate","reversible":true,"testMode":true,"notifyOnAward":false}""",
                Priority = 10, Status = RuleStatus.Disabled, CurrentVersion = 1,
                CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }

        var rule = await _client.GetFromJsonAsync<RuleResponse>($"{RulesUrl}/{ruleId}", JsonConventions.Options);

        rule!.Configuration!.TestMode.Should().BeFalse();
    }
}
