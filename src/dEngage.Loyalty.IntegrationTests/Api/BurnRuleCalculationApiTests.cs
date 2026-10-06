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

// CR 2026-10-05 item 1 through the real Rules API: redeem / transfer rules carry the wallet's
// fields (cash per point, minimum, Redeem into / daily transfer limit), and a redeem rule that
// pays cash needs a second admin (D2) — on create, after a change to what it pays, and when a
// disabled one is switched back on.
public sealed class BurnRuleCalculationApiTests : IClassFixture<CustomWebApplicationFactory>, IAsyncLifetime
{
    private const string TenantSlug = "cr1005-burn-rules";
    private const string OtherTenantSlug = "cr1005-burn-rules-b";

    private readonly CustomWebApplicationFactory _factory;
    private readonly HttpClient _adminA;   // creates rules
    private readonly HttpClient _adminB;   // a different admin, approves them

    private Guid _tenantId;
    private Guid _programId;
    private Guid _pointsId;
    private Guid _otherPointsId;
    private Guid _cashId;
    private Guid _otherProgramCashId;
    private Guid _otherTenantCashId;

    public BurnRuleCalculationApiTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _adminA = factory.CreateClient();
        _adminB = factory.CreateClient();
    }

    public async Task InitializeAsync()
    {
        await _factory.EnsureDbCreatedAsync();
        await using (var db = _factory.CreateDbContext())
        {
            var tenant = Ensure(db, TenantSlug);
            var otherTenant = Ensure(db, OtherTenantSlug);
            _tenantId = tenant.Id;

            // Unique per test: the class fixture shares one database across tests.
            _programId = Guid.NewGuid();
            var otherProgramId = Guid.NewGuid();
            var otherTenantProgramId = Guid.NewGuid();
            db.Programs.AddRange(
                new ProgramEntity { Id = _programId, TenantId = tenant.Id, Name = "burn", Status = ProgramStatus.Inactive, CreatedAt = DateTime.UtcNow },
                new ProgramEntity { Id = otherProgramId, TenantId = tenant.Id, Name = "other", Status = ProgramStatus.Inactive, CreatedAt = DateTime.UtcNow },
                new ProgramEntity { Id = otherTenantProgramId, TenantId = otherTenant.Id, Name = "b", Status = ProgramStatus.Inactive, CreatedAt = DateTime.UtcNow });

            _pointsId = Guid.NewGuid();
            _otherPointsId = Guid.NewGuid();
            _cashId = Guid.NewGuid();
            _otherProgramCashId = Guid.NewGuid();
            _otherTenantCashId = Guid.NewGuid();
            db.AccountTypes.AddRange(
                Account(_pointsId, tenant.Id, _programId, "POINTS", """{"decimals": 0}"""),
                Account(_otherPointsId, tenant.Id, _programId, "POINTS", """{"decimals": 0}"""),
                Account(_cashId, tenant.Id, _programId, "CASH", """{"currency": "SAR", "decimals": 2}"""),
                Account(_otherProgramCashId, tenant.Id, otherProgramId, "CASH", """{"currency": "SAR", "decimals": 2}"""),
                Account(_otherTenantCashId, otherTenant.Id, otherTenantProgramId, "CASH", """{"currency": "SAR", "decimals": 2}"""));
            await db.SaveChangesAsync();
        }

        var jwt = _factory.Services.GetRequiredService<IJwtTokenService>();
        _adminA.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", JwtIssuingHelper.IssuePlatformAdminToken(jwt, "admin-a@test.local"));
        _adminB.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", JwtIssuingHelper.IssuePlatformAdminToken(jwt, "admin-b@test.local"));
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private static Tenant Ensure(Schema.LoyaltyDbContext db, string slug)
    {
        var tenant = db.Tenants.SingleOrDefault(t => t.Slug == slug);
        if (tenant is not null) return tenant;
        tenant = new Tenant { Id = Guid.NewGuid(), Slug = slug, Name = slug, Status = TenantStatus.Active, CreatedAt = DateTime.UtcNow };
        db.Tenants.Add(tenant);
        return tenant;
    }

    private static AccountTypeEntity Account(Guid id, Guid tenantId, Guid programId, string type, string config) =>
        new() { Id = id, TenantId = tenantId, ProgramId = programId, Type = type, Name = $"{type}-{id:N}"[..16], Config = config, CreatedAt = DateTime.UtcNow };

    private string RulesUrl => $"/api/v1/tenants/{TenantSlug}/programs/{_programId}/rules";

    private static StringContent Json(object body) => new(
        System.Text.Json.JsonSerializer.Serialize(body, JsonConventions.Options), System.Text.Encoding.UTF8, "application/json");

    private CreateRuleRequest Redeem(RuleCalculation calculation, Guid? target = null) => new(
        $"redeem-{Guid.NewGuid():N}"[..20], EventTypes.PointsRedeem, target ?? _pointsId, RuleTypes.RedemptionRule,
        calculation, null, null, 10, false, null, null, null, null, null);

    private CreateRuleRequest Transfer(RuleCalculation calculation) => new(
        $"transfer-{Guid.NewGuid():N}"[..20], EventTypes.PointsTransfer, _pointsId, RuleTypes.TransferRule,
        calculation, null, null, 10, false, null, null, null, null, null);

    private RuleCalculation ValidRedeem(decimal rate = 0.01m) => new() { Factor = rate, MinRedeem = 100m, CashAccountTypeId = _cashId };

    private static async Task<RuleResponse> Read(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<RuleResponse>(JsonConventions.Options))!;

    private async Task<RuleResponse> CreateApprovedRedeemAsync()
    {
        var created = await Read(await _adminA.PostAsync(RulesUrl, Json(Redeem(ValidRedeem()))));
        var approved = await _adminB.PatchAsync($"{RulesUrl}/{created.Id}/approve", Json(new { }));
        approved.StatusCode.Should().Be(HttpStatusCode.OK, await approved.Content.ReadAsStringAsync());
        return await Read(approved);
    }

    [Fact]
    public async Task A_redeem_rule_that_pays_cash_waits_for_a_second_admin()
    {
        var response = await _adminA.PostAsync(RulesUrl, Json(Redeem(ValidRedeem())));

        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        var rule = await Read(response);
        rule.Status.Should().Be(RuleStatus.PendingApproval);
        rule.Calculation!.CashAccountTypeId.Should().Be(_cashId);

        (await _adminA.PatchAsync($"{RulesUrl}/{rule.Id}/approve", Json(new { }))).StatusCode
            .Should().Be(HttpStatusCode.BadRequest); // the creator can't approve
        (await Read(await _adminB.PatchAsync($"{RulesUrl}/{rule.Id}/approve", Json(new { })))).Status
            .Should().Be(RuleStatus.Active);
    }

    public static TheoryData<string> InvalidRedeemCases => new() { "no-rate", "ratio", "no-cash", "negative-min" };

    [Theory]
    [MemberData(nameof(InvalidRedeemCases))]
    public async Task An_incomplete_or_legacy_redeem_calculation_is_rejected(string invalid)
    {
        var calculation = ValidRedeem();
        switch (invalid)
        {
            case "no-rate": calculation.Factor = null; break;
            case "ratio": calculation.Ratio = 100m; break; // the retired "points per currency unit"
            case "no-cash": calculation.CashAccountTypeId = null; break;
            case "negative-min": calculation.MinRedeem = -1m; break;
        }

        var response = await _adminA.PostAsync(RulesUrl, Json(Redeem(calculation)));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest, invalid);
    }

    [Fact]
    public async Task A_redeem_rule_without_a_minimum_is_accepted()
    {
        var calculation = ValidRedeem();
        calculation.MinRedeem = null;

        var response = await _adminA.PostAsync(RulesUrl, Json(Redeem(calculation)));

        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Redeem_into_must_be_a_cash_wallet_of_this_program_and_tenant()
    {
        foreach (var wallet in new[] { _otherProgramCashId, _otherTenantCashId, _otherPointsId })
        {
            var calculation = ValidRedeem();
            calculation.CashAccountTypeId = wallet;

            var response = await _adminA.PostAsync(RulesUrl, Json(Redeem(calculation)));

            response.StatusCode.Should().Be(HttpStatusCode.BadRequest, $"wallet {wallet}");
        }
    }

    [Fact]
    public async Task A_transfer_rule_needs_a_daily_limit_and_goes_live_without_approval()
    {
        (await _adminA.PostAsync(RulesUrl, Json(Transfer(new RuleCalculation { Ratio = 1m })))).StatusCode
            .Should().Be(HttpStatusCode.BadRequest);
        (await _adminA.PostAsync(RulesUrl, Json(Transfer(new RuleCalculation { MaxPerDay = 0m })))).StatusCode
            .Should().Be(HttpStatusCode.BadRequest);

        var response = await _adminA.PostAsync(RulesUrl, Json(Transfer(new RuleCalculation { MaxPerDay = 5000m })));

        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        (await Read(response)).Status.Should().Be(RuleStatus.Active);
    }

    [Fact]
    public async Task Changing_what_an_approved_redeem_rule_pays_sends_it_back_for_approval()
    {
        var rule = await CreateApprovedRedeemAsync();

        var renamed = await Read(await _adminA.PatchAsync($"{RulesUrl}/{rule.Id}", Json(new { name = "renamed" })));
        renamed.Status.Should().Be(RuleStatus.Active); // nothing it pays changed

        var repriced = await _adminA.PatchAsync($"{RulesUrl}/{rule.Id}", Json(new { calculation = ValidRedeem(rate: 0.02m) }));
        repriced.StatusCode.Should().Be(HttpStatusCode.OK, await repriced.Content.ReadAsStringAsync());
        (await Read(repriced)).Status.Should().Be(RuleStatus.PendingApproval);
    }

    [Fact]
    public async Task Moving_a_pending_redeem_rule_to_another_points_wallet_keeps_it_pending()
    {
        var rule = await Read(await _adminA.PostAsync(RulesUrl, Json(Redeem(ValidRedeem()))));

        var moved = await Read(await _adminA.PatchAsync($"{RulesUrl}/{rule.Id}", Json(new { targetAccountTypeId = _otherPointsId })));

        moved.Status.Should().Be(RuleStatus.PendingApproval);
    }

    [Fact]
    public async Task An_edit_with_an_invalid_redeem_calculation_is_rejected()
    {
        var rule = await CreateApprovedRedeemAsync();

        var response = await _adminA.PatchAsync($"{RulesUrl}/{rule.Id}", Json(new { calculation = new RuleCalculation { Ratio = 100m } }));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Switching_a_redeem_rule_back_on_after_a_payout_change_needs_approval_again()
    {
        var rule = await CreateApprovedRedeemAsync();
        await _adminA.PatchAsync($"{RulesUrl}/{rule.Id}/status", Json(new SetRuleStatusRequest(RuleStatus.Disabled)));
        await _adminA.PatchAsync($"{RulesUrl}/{rule.Id}", Json(new { calculation = ValidRedeem(rate: 0.05m) }));

        var enabled = await _adminA.PatchAsync($"{RulesUrl}/{rule.Id}/status", Json(new SetRuleStatusRequest(RuleStatus.Active)));

        enabled.StatusCode.Should().Be(HttpStatusCode.OK, await enabled.Content.ReadAsStringAsync());
        (await Read(enabled)).Status.Should().Be(RuleStatus.PendingApproval);
    }

    [Fact]
    public async Task Switching_an_approved_redeem_rule_back_on_unchanged_makes_it_active()
    {
        var rule = await CreateApprovedRedeemAsync();
        await _adminA.PatchAsync($"{RulesUrl}/{rule.Id}/status", Json(new SetRuleStatusRequest(RuleStatus.Disabled)));

        var enabled = await Read(await _adminA.PatchAsync($"{RulesUrl}/{rule.Id}/status", Json(new SetRuleStatusRequest(RuleStatus.Active))));

        enabled.Status.Should().Be(RuleStatus.Active);
    }

    // R-O11: the rules the deploy migration disabled were saved under the old model.
    [Fact]
    public async Task A_legacy_rule_cannot_be_switched_on_until_it_is_completed()
    {
        var legacyId = Guid.NewGuid();
        await using (var db = _factory.CreateDbContext())
        {
            db.Rules.Add(new Rule
            {
                Id = legacyId, TenantId = _tenantId, ProgramId = _programId, Name = "legacy redeem",
                Type = RuleTypes.RedemptionRule, Trigger = EventTypes.PointsRedeem, TargetAccountTypeId = _pointsId,
                Calculation = """{"ratio": 100}""", Priority = 1, Status = RuleStatus.Disabled,
                CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }

        var response = await _adminA.PatchAsync($"{RulesUrl}/{legacyId}/status", Json(new SetRuleStatusRequest(RuleStatus.Active)));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
