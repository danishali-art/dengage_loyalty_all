using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using dEngage.Loyalty.Api.AccountTypes;
using dEngage.Loyalty.Api.Framework.Auth;
using dEngage.Loyalty.Api.Framework.Json;
using dEngage.Loyalty.Api.Framework.Pagination;
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
using RuleEntity = dEngage.Loyalty.Schema.Entities.Rule;

namespace dEngage.Loyalty.IntegrationTests.Api;

// CR 2026-10-05 (D1, D8, D15): STAMP wallets, StampRule, ExpiryRule and the points.expired
// trigger are retired. Nothing new can use them; existing rows stay readable as history but
// can't be edited or switched back on. Guards against any of these paths coming back.
public sealed class RetiredStampsAndExpiryRuleApiTests : IClassFixture<CustomWebApplicationFactory>, IAsyncLifetime
{
    private const string TenantSlug = "cr1005-retired";

    private readonly CustomWebApplicationFactory _factory;
    private readonly HttpClient _client;
    private Guid _programId;
    private Guid _pointsId;
    private Guid _stampId;
    private Guid _stampRuleId;
    private Guid _stampTargetedRuleId;
    private Guid _expiryTriggerRuleId;

    public RetiredStampsAndExpiryRuleApiTests(CustomWebApplicationFactory factory)
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
            _stampId = Guid.NewGuid();
            db.Programs.Add(new ProgramEntity { Id = _programId, TenantId = tenant.Id, Name = "retired", Status = ProgramStatus.Inactive, CreatedAt = DateTime.UtcNow });
            db.AccountTypes.AddRange(
                new AccountTypeEntity { Id = _pointsId, TenantId = tenant.Id, ProgramId = _programId, Type = "POINTS", Name = "Points", Config = """{"decimals": 0}""", CreatedAt = DateTime.UtcNow },
                // A STAMP wallet created before the retirement.
                new AccountTypeEntity { Id = _stampId, TenantId = tenant.Id, ProgramId = _programId, Type = "STAMP", Name = "Coffee card", Config = """{"stamp_target": 10}""", CreatedAt = DateTime.UtcNow });

            // Rules as the RetireStampsAndExpiryRuleCr1005 migration leaves them: disabled.
            _stampRuleId = Guid.NewGuid();
            _stampTargetedRuleId = Guid.NewGuid();
            _expiryTriggerRuleId = Guid.NewGuid();
            db.Rules.AddRange(
                DisabledRule(_stampRuleId, tenant.Id, RuleTypes.StampRule, EventTypes.OrderCreated, _stampId),
                DisabledRule(_stampTargetedRuleId, tenant.Id, RuleTypes.ManualAdjustmentRule, EventTypes.PointsAdjusted, _stampId),
                DisabledRule(_expiryTriggerRuleId, tenant.Id, RuleTypes.FixedBonusRule, EventTypes.PointsExpired, _pointsId));
            await db.SaveChangesAsync();
        }

        var jwt = _factory.Services.GetRequiredService<IJwtTokenService>();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", JwtIssuingHelper.IssuePlatformAdminToken(jwt));
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private RuleEntity DisabledRule(Guid id, Guid tenantId, string type, string trigger, Guid target) => new()
    {
        Id = id, TenantId = tenantId, ProgramId = _programId, Name = $"{type} on {trigger}",
        Type = type, Trigger = trigger, Calculation = "{}", TargetAccountTypeId = target,
        Status = RuleStatus.Disabled, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
    };

    private string AccountTypesUrl => $"/api/v1/tenants/{TenantSlug}/programs/{_programId}/account-types";
    private string RulesUrl => $"/api/v1/tenants/{TenantSlug}/programs/{_programId}/rules";

    private static StringContent Json(object body) => new(
        System.Text.Json.JsonSerializer.Serialize(body, JsonConventions.Options), System.Text.Encoding.UTF8, "application/json");

    private CreateRuleRequest Rule(string type, string trigger, RuleCalculation calculation) => new(
        $"r-{Guid.NewGuid():N}"[..20], trigger, _pointsId, type,
        calculation, null, null, 10, false, null, null, null, null, null);

    [Fact]
    public async Task A_new_stamp_account_type_is_rejected()
    {
        var response = await _client.PostAsync(AccountTypesUrl, Json(new { type = "STAMP", name = "Stamps", config = new { } }));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task An_existing_stamp_wallet_is_hidden_from_the_list_but_still_readable_by_id()
    {
        var page = await _client.GetFromJsonAsync<PagedResult<AccountTypeResponse>>(AccountTypesUrl, JsonConventions.Options);
        page!.Data.Select(a => a.Id).Should().Contain(_pointsId).And.NotContain(_stampId);

        var byId = await _client.GetAsync($"{AccountTypesUrl}/{_stampId}");
        byId.StatusCode.Should().Be(HttpStatusCode.OK, await byId.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task An_existing_stamp_wallet_cannot_be_edited()
    {
        var response = await _client.PatchAsync($"{AccountTypesUrl}/{_stampId}", Json(new { name = "Renamed" }));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await response.Content.ReadAsStringAsync()).Should().Contain("account_type_retired");
    }

    [Theory]
    [InlineData(RuleTypes.StampRule)]
    [InlineData(RuleTypes.ExpiryRule)]
    public async Task A_new_rule_of_a_retired_type_is_rejected(string type)
    {
        var response = await _client.PostAsync(RulesUrl, Json(Rule(type, EventTypes.OrderCreated, new RuleCalculation())));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task A_new_rule_on_the_points_expired_trigger_is_rejected()
    {
        var response = await _client.PostAsync(RulesUrl, Json(Rule(RuleTypes.FixedBonusRule, EventTypes.PointsExpired, new RuleCalculation { FixedValue = 5m })));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("trigger_retired");
    }

    [Fact]
    public async Task A_valid_rule_is_still_accepted()
    {
        var response = await _client.PostAsync(RulesUrl, Json(Rule(RuleTypes.FixedBonusRule, EventTypes.Signup, new RuleCalculation { FixedValue = 5m })));

        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Retired_rules_cannot_be_switched_back_on()
    {
        foreach (var ruleId in new[] { _stampRuleId, _stampTargetedRuleId, _expiryTriggerRuleId })
        {
            var response = await _client.PatchAsync($"{RulesUrl}/{ruleId}/status", Json(new SetRuleStatusRequest(RuleStatus.Active)));

            response.StatusCode.Should().Be(HttpStatusCode.Conflict, $"rule {ruleId}");
            (await response.Content.ReadAsStringAsync()).Should().Contain("rule_type_retired");
        }
    }

    [Fact]
    public async Task A_retired_rule_cannot_be_edited()
    {
        var response = await _client.PatchAsync($"{RulesUrl}/{_stampRuleId}", Json(new { name = "Renamed" }));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await response.Content.ReadAsStringAsync()).Should().Contain("rule_type_retired");
    }

    [Fact]
    public async Task Metadata_offers_no_retired_rule_type_trigger_or_stamp_target()
    {
        var metadata = await _client.GetFromJsonAsync<RulesMetadataResponse>($"{RulesUrl}/metadata", JsonConventions.Options);

        metadata!.RuleTypes.Select(t => t.RuleType).Should().NotContain([RuleTypes.StampRule, RuleTypes.ExpiryRule]);
        metadata.RuleTypes.SelectMany(t => t.ValidTargetAccountKinds).Should().NotContain("STAMP");
        metadata.Events.Select(e => e.EventType).Should().NotContain(EventTypes.PointsExpired)
            .And.Contain([EventTypes.Signup, EventTypes.BirthdayBonus, EventTypes.KycCompleted]);
    }

    [Fact]
    public async Task A_streak_campaign_on_the_points_expired_trigger_is_rejected()
    {
        var response = await _client.PostAsync($"/api/v1/tenants/{TenantSlug}/programs/{_programId}/streak-campaigns", Json(new
        {
            name = "expiry streak",
            trigger = EventTypes.PointsExpired,
            targetAccountTypeId = _pointsId,
            config = new { }
        }));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("trigger_retired");
    }
}
