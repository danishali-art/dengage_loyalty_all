using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using dEngage.Loyalty.Api.Events;
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

// CR 2026-09-30 P1: the burn-trigger rule-type allowlist through the real Rules API, and the
// event-types `publishable` list the Event Simulator uses (O9).
public sealed class BurnTriggerRuleTypesApiTests : IClassFixture<CustomWebApplicationFactory>, IAsyncLifetime
{
    private readonly CustomWebApplicationFactory _factory;
    private readonly HttpClient _client;
    private const string TenantSlug = "cr0930-burn";
    private Guid _programId;
    private Guid _pointsId;

    public BurnTriggerRuleTypesApiTests(CustomWebApplicationFactory factory)
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
            db.Programs.Add(new ProgramEntity { Id = _programId, TenantId = tenant.Id, Name = "burn", Status = ProgramStatus.Inactive, CreatedAt = DateTime.UtcNow });
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

    // A calculation valid for the type (CR 2026-10-05 fields), so these tests only ever exercise
    // the trigger → rule-type allowlist.
    private CreateRuleRequest Burn(string trigger, string type) => new(
        $"burn-{Guid.NewGuid():N}"[..20], trigger, _pointsId, type,
        type == RuleTypes.TransferRule
            ? new RuleCalculation { MaxPerDay = 1000m }
            : new RuleCalculation { Factor = 0.01m, CashAccountTypeId = Guid.NewGuid() },
        null, null, 10, false, null, null, null, null, null);

    [Fact]
    public async Task A_redemption_rule_on_points_transfer_is_rejected()
    {
        var response = await _client.PostAsync(RulesUrl, Json(Burn(EventTypes.PointsTransfer, RuleTypes.RedemptionRule)));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task A_transfer_rule_on_points_transfer_is_still_accepted()
    {
        var response = await _client.PostAsync(RulesUrl, Json(Burn(EventTypes.PointsTransfer, RuleTypes.TransferRule)));

        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Metadata_lists_no_rule_type_for_reward_purchase_and_only_its_own_for_burn_events()
    {
        var metadata = await _client.GetFromJsonAsync<RulesMetadataResponse>(
            $"/api/v1/tenants/{TenantSlug}/programs/{_programId}/rules/metadata", JsonConventions.Options);

        var byType = metadata!.Events.ToDictionary(e => e.EventType, e => e.CompatibleRuleTypes);
        byType[EventTypes.RewardPurchase].Should().BeEmpty();
        byType[EventTypes.PointsTransfer].Should().Equal(RuleTypes.TransferRule);
        byType[EventTypes.PointsRedeem].Should().Equal(RuleTypes.RedemptionRule);
    }

    [Fact]
    public async Task Event_types_no_longer_list_the_retired_scheduled_triggers()
    {
        var types = await _client.GetFromJsonAsync<EventTypesResponse>(
            $"/api/v1/tenants/{TenantSlug}/events/types", JsonConventions.Options);

        // points.expired (D15) and birthdaybonus (addendum A-D4) were retired by CR 2026-10-05.
        types!.BuiltIn.Should().NotContain([EventTypes.BirthdayBonus, EventTypes.PointsExpired]);
        types.Publishable.Should().NotContain([EventTypes.BirthdayBonus, EventTypes.PointsExpired]);
        types.Publishable.Should().Contain([EventTypes.OrderCreated, EventTypes.PointsRedeem, EventTypes.RewardPurchase]);
    }
}
