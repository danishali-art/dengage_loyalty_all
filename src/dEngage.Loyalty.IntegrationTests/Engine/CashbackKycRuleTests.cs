using System.Text.Json;
using dEngage.Loyalty.IntegrationTests.Fixtures;
using dEngage.Loyalty.RuleEngine.Models;
using dEngage.Loyalty.Shared;
using FluentAssertions;
using Xunit;

namespace dEngage.Loyalty.IntegrationTests.Engine;

// Tiqmo TQ01 — cashback rule: KYC completed within 1 hour of signup grants +2 CASH exactly once,
// and is a no-op outside the window or with no anchoring signup at all.
public sealed class CashbackKycRuleTests : IDisposable
{
    private readonly RuleEngineTestHarness _harness = new();
    private readonly Guid _programId;
    private readonly Guid _cashAccountTypeId;
    private readonly Guid _ruleId = Guid.NewGuid();

    public CashbackKycRuleTests()
    {
        _programId = _harness.AddProgram();
        _cashAccountTypeId = _harness.AddAccountType(_programId, "CASH", "CASH");

        _harness.AddRule(new CachedRule
        {
            Id = _ruleId,
            ProgramId = _programId,
            Name = "KYC Fast Cashback",
            Type = RuleTypes.FixedBonusRule,
            Trigger = "kyc.completed",
            // CR-05: occurred_within maps onto agg.hoursSince.<eventType> (see
            // GroupedConditionEvaluator/TierContextLoader remarks) — "within 1 hour of signup"
            // becomes agg.hoursSince.signup <= 1 (absent/negative-elapsed treated as no match,
            // same as the old occurred_within semantics).
            Conditions = JsonSerializer.Deserialize<ConditionTree>("""
                {"op":"AND","groups":[{"op":"AND","conditions":[
                    {"field":"profile.kyc_status","operator":"eq","value":{"type":"string","data":"none"}},
                    {"field":"agg.hoursSince.signup","operator":"lte","value":{"type":"number","data":1}}
                ]}]}
                """),
            Calculation = new RuleCalculation { FixedValue = 2m },
            TargetAccountTypeId = _cashAccountTypeId,
            Priority = 100,
            Stackable = false,
            Version = 1
        });
    }

    public void Dispose() => _harness.Dispose();

    private static EvaluationEvent KycEvent(string contact, DateTime occurredAt) => new()
    {
        EventType = "kyc.completed",
        ContactKey = contact,
        OccurredAt = occurredAt,
        Data = JsonSerializer.SerializeToElement(new { contact_key = contact, profile = new { kyc_status = "none" } })
    };

    [Fact]
    public async Task Kyc_completed_within_one_hour_of_signup_grants_fixed_cashback()
    {
        var now = DateTime.UtcNow;
        _harness.LogEvent("evt-signup", "tq_a", "signup", now.AddMinutes(-30));

        await _harness.Engine.ProcessEventAsync(RuleEngineTestHarness.TenantSlug, _programId, "evt-kyc", KycEvent("tq_a", now), CancellationToken.None);

        _harness.GetBalance("tq_a", _cashAccountTypeId).Should().Be(2m);
        _harness.OutboxHasDedupKey("points_earned:evt-kyc").Should().BeTrue();
    }

    [Fact]
    public async Task Replaying_the_same_event_id_does_not_pay_twice()
    {
        var now = DateTime.UtcNow;
        _harness.LogEvent("evt-signup", "tq_a", "signup", now.AddMinutes(-30));

        await _harness.Engine.ProcessEventAsync(RuleEngineTestHarness.TenantSlug, _programId, "evt-kyc", KycEvent("tq_a", now), CancellationToken.None);
        await _harness.Engine.ProcessEventAsync(RuleEngineTestHarness.TenantSlug, _programId, "evt-kyc", KycEvent("tq_a", now), CancellationToken.None);

        _harness.GetBalance("tq_a", _cashAccountTypeId).Should().Be(2m);
        _harness.LedgerCount("tq_a", _cashAccountTypeId, LedgerReason.Earn).Should().Be(1);
    }

    [Fact]
    public async Task Kyc_completed_outside_the_one_hour_window_earns_nothing()
    {
        var now = DateTime.UtcNow;
        _harness.LogEvent("evt-signup", "tq_b", "signup", now.AddHours(-2));

        await _harness.Engine.ProcessEventAsync(RuleEngineTestHarness.TenantSlug, _programId, "evt-kyc", KycEvent("tq_b", now), CancellationToken.None);

        _harness.GetBalance("tq_b", _cashAccountTypeId).Should().Be(0m);
    }

    [Fact]
    public async Task Kyc_completed_with_no_prior_signup_earns_nothing()
    {
        await _harness.Engine.ProcessEventAsync(
            RuleEngineTestHarness.TenantSlug, _programId, "evt-kyc", KycEvent("tq_c", DateTime.UtcNow), CancellationToken.None);

        _harness.GetBalance("tq_c", _cashAccountTypeId).Should().Be(0m);
    }
}
