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

// CR 2026-10-06 Phase 2: a NEW rule may set only the Configuration / Limits fields that apply to
// its trigger and type (D5, RuleFieldCatalog); edits of existing rules are not checked.
public sealed class RuleFieldsByTriggerCr1006ApiTests : IClassFixture<CustomWebApplicationFactory>, IAsyncLifetime
{
    private readonly CustomWebApplicationFactory _factory;
    private readonly HttpClient _client;
    private const string TenantSlug = "cr1006-fields";
    private Guid _programId;
    private Guid _pointsId;
    private Guid _cashId;

    public RuleFieldsByTriggerCr1006ApiTests(CustomWebApplicationFactory factory)
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
            _cashId = Guid.NewGuid();
            db.Programs.Add(new ProgramEntity { Id = _programId, TenantId = tenant.Id, Name = "fields", Status = ProgramStatus.Inactive, CreatedAt = DateTime.UtcNow });
            db.AccountTypes.AddRange(
                new AccountTypeEntity { Id = _pointsId, TenantId = tenant.Id, ProgramId = _programId, Type = "POINTS", Name = "Points", Config = """{"decimals": 0}""", CreatedAt = DateTime.UtcNow },
                new AccountTypeEntity { Id = _cashId, TenantId = tenant.Id, ProgramId = _programId, Type = "CASH", Name = "Cash", Config = """{"currency": "SAR", "decimals": 2}""", CreatedAt = DateTime.UtcNow });
            await db.SaveChangesAsync();
        }

        var jwt = _factory.Services.GetRequiredService<IJwtTokenService>();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", JwtIssuingHelper.IssuePlatformAdminToken(jwt));
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private string RulesUrl => $"/api/v1/tenants/{TenantSlug}/programs/{_programId}/rules";

    private static StringContent Json(object body) => new(
        System.Text.Json.JsonSerializer.Serialize(body, JsonConventions.Options), System.Text.Encoding.UTF8, "application/json");

    private CreateRuleRequest Bonus(string trigger, RuleLimits? limits = null, RuleSettings? configuration = null, Guid? target = null) => new(
        $"r-{Guid.NewGuid():N}"[..20], trigger, target ?? _pointsId, RuleTypes.FixedBonusRule,
        new RuleCalculation { FixedValue = 10m }, null, limits, 10, false, null, null, configuration, null, null);

    private CreateRuleRequest Transfer(RuleLimits limits) => new(
        $"t-{Guid.NewGuid():N}"[..20], EventTypes.PointsTransfer, _pointsId, RuleTypes.TransferRule,
        new RuleCalculation { MaxPerDay = 1000m }, null, limits, 10, false, null, null, null, null, null);

    private async Task<string> RejectedAsync(CreateRuleRequest request)
    {
        var response = await _client.PostAsync(RulesUrl, Json(request));
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest, body);
        return body;
    }

    private async Task AcceptedAsync(CreateRuleRequest request)
    {
        var response = await _client.PostAsync(RulesUrl, Json(request));
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Min_event_amount_on_kyc_completed_is_rejected_because_the_rule_would_never_fire()
    {
        var body = await RejectedAsync(Bonus(EventTypes.KycCompleted, new RuleLimits { MinEventAmount = 10m }));
        body.Should().Contain("field_not_applicable").And.Contain("min_event_amount");
    }

    [Fact]
    public async Task Per_customer_total_and_delayed_posting_on_signup_are_rejected()
    {
        var body = await RejectedAsync(Bonus(EventTypes.Signup, new RuleLimits { PerCustomerTotal = 100m },
            new RuleSettings { Posting = "Delayed", HoldDays = 3 }));
        body.Should().Contain("per_customer_total").And.Contain("posting").And.Contain("holdDays");
    }

    [Fact]
    public async Task A_signup_rule_with_only_its_fields_and_the_defaults_is_accepted()
    {
        await AcceptedAsync(Bonus(EventTypes.Signup, new RuleLimits { MaxCustomers = 1000, RuleBudgetTotal = 50000m },
            new RuleSettings { NotifyOnAward = true }));
    }

    [Fact]
    public async Task A_transfer_rule_accepts_cooldown_but_not_min_event_amount_or_on_breach_skip()
    {
        await AcceptedAsync(Transfer(new RuleLimits { CooldownHours = 24m }));
        var body = await RejectedAsync(Transfer(new RuleLimits { MinEventAmount = 10m, RuleBudgetTotal = 1000m, OnBreach = "Skip" }));
        body.Should().Contain("min_event_amount").And.Contain("on_breach");
    }

    [Fact]
    public async Task A_tenant_defined_trigger_accepts_every_field()
    {
        await AcceptedAsync(Bonus("tenant.custom_event", new RuleLimits { MinEventAmount = 10m, PerCustomerTotal = 100m },
            new RuleSettings { Posting = "Delayed", HoldDays = 3 }));
    }

    [Fact]
    public async Task An_expiry_override_on_a_cash_target_is_rejected()
    {
        var response = await _client.PostAsync(RulesUrl, Json(Bonus(EventTypes.OrderCreated,
            configuration: new RuleSettings { ExpiryOverrideDays = 30 }, target: _cashId)));

        // ValidationApiException's message isn't in the response body (framework behaviour), so
        // the cause is pinned by the same rule on a POINTS wallet being accepted.
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        await AcceptedAsync(Bonus(EventTypes.OrderCreated, configuration: new RuleSettings { ExpiryOverrideDays = 30 }));
    }

    // D5: only new rules are checked — an existing rule keeps and can still save its values.
    [Fact]
    public async Task An_edit_of_an_existing_rule_is_not_checked_against_its_trigger()
    {
        var created = await _client.PostAsync(RulesUrl, Json(Bonus(EventTypes.KycCompleted)));
        var rule = await created.Content.ReadFromJsonAsync<RuleResponse>(JsonConventions.Options);

        var update = new UpdateRuleRequest(null, null, null, null, null,
            new RuleLimits { MinEventAmount = 10m }, null, null, null, null, null, null, null);
        var response = await _client.PatchAsync($"{RulesUrl}/{rule!.Id}", Json(update));

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Metadata_serves_the_applicable_fields_per_rule_type()
    {
        var metadata = await _client.GetFromJsonAsync<RulesMetadataResponse>($"{RulesUrl}/metadata", JsonConventions.Options);

        var kyc = metadata!.Events.Single(e => e.EventType == EventTypes.KycCompleted);
        var bonus = kyc.ApplicableFields.Should().ContainSingle(f => f.RuleType == RuleTypes.FixedBonusRule).Subject;
        bonus.Limits.Should().NotContain("min_event_amount").And.NotContain("per_customer_total").And.Contain("max_customers");
        bonus.Configuration.Should().NotContain("posting").And.Contain("notifyOnAward");

        var refunded = metadata.Events.Single(e => e.EventType == EventTypes.OrderRefunded);
        refunded.ApplicableFields.Should().BeEmpty("order.refunded accepts no rule type (D22)");
    }
}
