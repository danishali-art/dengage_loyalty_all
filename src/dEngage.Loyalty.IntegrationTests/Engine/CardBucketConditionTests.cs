using System.Text.Json;
using dEngage.Loyalty.IntegrationTests.Fixtures;
using dEngage.Loyalty.RuleEngine.Cache;
using dEngage.Loyalty.RuleEngine.Models;
using dEngage.Loyalty.Shared;
using FluentAssertions;
using Moq;
using Xunit;

namespace dEngage.Loyalty.IntegrationTests.Engine;

// Tiqmo TQ08 — card.transaction: only a captured transaction (not pre_auth) earns cashback.
public sealed class CardCapturedVsPreAuthTests : IDisposable
{
    private readonly RuleEngineTestHarness _harness = new();
    private readonly Guid _programId;
    private readonly Guid _cashAccountTypeId;

    public CardCapturedVsPreAuthTests()
    {
        _programId = _harness.AddProgram();
        _cashAccountTypeId = _harness.AddAccountType(_programId, "CASH", "CASH");

        _harness.AddRule(new CachedRule
        {
            Id = Guid.NewGuid(),
            ProgramId = _programId,
            Name = "Card Captured Cashback",
            Type = RuleTypes.FixedBonusRule,
            Trigger = "card.transaction",
            Conditions = JsonSerializer.Deserialize<ConditionTree>("""
                {"op":"AND","groups":[{"op":"AND","conditions":[
                    {"field":"tx_status","operator":"eq","value":{"type":"string","data":"captured"}},
                    {"field":"amount","operator":"gte","value":{"type":"money","data":100}}
                ]}]}
                """),
            Calculation = new RuleCalculation { FixedValue = 3m },
            TargetAccountTypeId = _cashAccountTypeId,
            Priority = 100,
            Stackable = false,
            Version = 1
        });
    }

    public void Dispose() => _harness.Dispose();

    private static EvaluationEvent CardEvent(string contact, decimal amount, string txStatus) => new()
    {
        EventType = "card.transaction",
        ContactKey = contact,
        Amount = amount,
        Channel = "card",
        OccurredAt = DateTime.UtcNow,
        Data = JsonSerializer.SerializeToElement(new { contact_key = contact, amount = amount.ToString("F2"), channel = "card", tx_status = txStatus })
    };

    [Fact]
    public async Task Captured_transaction_earns_the_fixed_cashback()
    {
        await _harness.Engine.ProcessEventAsync(
            RuleEngineTestHarness.TenantSlug, _programId, "evt-1", CardEvent("tq_j", 150m, "captured"), CancellationToken.None);

        _harness.GetBalance("tq_j", _cashAccountTypeId).Should().Be(3m);
    }

    [Fact]
    public async Task Pre_auth_transaction_earns_nothing_until_it_is_captured()
    {
        await _harness.Engine.ProcessEventAsync(
            RuleEngineTestHarness.TenantSlug, _programId, "evt-1", CardEvent("tq_j", 150m, "pre_auth"), CancellationToken.None);
        _harness.GetBalance("tq_j", _cashAccountTypeId).Should().Be(0m);

        await _harness.Engine.ProcessEventAsync(
            RuleEngineTestHarness.TenantSlug, _programId, "evt-2", CardEvent("tq_j", 150m, "captured"), CancellationToken.None);
        _harness.GetBalance("tq_j", _cashAccountTypeId).Should().Be(3m);
    }
}

// Tiqmo TQ09 — a single rule combining MCC bucket + amount + zone(country) + segment, with a
// per-customer-per-day cap that clips a second otherwise-eligible transaction to zero once reached.
public sealed class CardBucketCombinedRuleTests : IDisposable
{
    private readonly RuleEngineTestHarness _harness = new();
    private readonly Guid _programId;
    private readonly Guid _cashAccountTypeId;
    private readonly Guid _ruleId = Guid.NewGuid();

    public CardBucketCombinedRuleTests()
    {
        _programId = _harness.AddProgram();
        _cashAccountTypeId = _harness.AddAccountType(_programId, "CASH", "CASH");

        _harness.AddRule(new CachedRule
        {
            Id = _ruleId,
            ProgramId = _programId,
            Name = "Combined Bucket",
            Type = RuleTypes.FixedBonusRule,
            Trigger = "card.transaction",
            Conditions = JsonSerializer.Deserialize<ConditionTree>("""
                {"op":"AND","groups":[{"op":"AND","conditions":[
                    {"field":"mcc","operator":"in","value":{"type":"string[]","data":["5411","5541"]}},
                    {"field":"amount","operator":"gte","value":{"type":"money","data":100}},
                    {"field":"country","operator":"eq","value":{"type":"string","data":"SA"}},
                    {"field":"profile.segment","operator":"eq","value":{"type":"string","data":"premium"}}
                ]}]}
                """),
            Calculation = new RuleCalculation { FixedValue = 8m },
            TargetAccountTypeId = _cashAccountTypeId,
            Limits = new RuleLimits { PerCustomerPerDay = 8m },
            Priority = 100,
            Stackable = false,
            Version = 1
        });
    }

    public void Dispose() => _harness.Dispose();

    private static EvaluationEvent CardEvent(string contact, string mcc, string segment) => new()
    {
        EventType = "card.transaction",
        ContactKey = contact,
        Amount = 250m,
        Channel = "card",
        OccurredAt = DateTime.UtcNow,
        Data = JsonSerializer.SerializeToElement(new
        {
            contact_key = contact,
            amount = "250.00",
            channel = "card",
            mcc,
            country = "SA",
            profile = new { segment }
        })
    };

    // Mirrors the daily counter LimitCacheService would report in production: the sum of
    // today's ledger deltas already posted for this rule/contact.
    private void SetupDailyLimitFromLedger() =>
        _harness.LimitCache.Setup(c => c.GetDailyAsync(RuleEngineTestHarness.TenantSlug, _ruleId, "tq_k", It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => _harness.Db.LedgerEntries.Where(l => l.RuleId == _ruleId && l.ContactKey == "tq_k").ToList().Sum(l => l.Delta));

    [Fact]
    public async Task All_dimensions_matching_grants_the_fixed_bonus()
    {
        SetupDailyLimitFromLedger();

        await _harness.Engine.ProcessEventAsync(
            RuleEngineTestHarness.TenantSlug, _programId, "evt-1", CardEvent("tq_k", "5411", "premium"), CancellationToken.None);

        _harness.GetBalance("tq_k", _cashAccountTypeId).Should().Be(8m);
    }

    [Fact]
    public async Task Wrong_segment_does_not_match()
    {
        SetupDailyLimitFromLedger();

        await _harness.Engine.ProcessEventAsync(
            RuleEngineTestHarness.TenantSlug, _programId, "evt-1", CardEvent("tq_k", "5411", "standard"), CancellationToken.None);

        _harness.GetBalance("tq_k", _cashAccountTypeId).Should().Be(0m);
    }

    [Fact]
    public async Task Mcc_outside_the_bucket_does_not_match()
    {
        SetupDailyLimitFromLedger();

        await _harness.Engine.ProcessEventAsync(
            RuleEngineTestHarness.TenantSlug, _programId, "evt-1", CardEvent("tq_k", "5999", "premium"), CancellationToken.None);

        _harness.GetBalance("tq_k", _cashAccountTypeId).Should().Be(0m);
    }

    [Fact]
    public async Task Second_eligible_transaction_same_day_is_clipped_to_zero_by_the_daily_cap()
    {
        SetupDailyLimitFromLedger();

        await _harness.Engine.ProcessEventAsync(
            RuleEngineTestHarness.TenantSlug, _programId, "evt-1", CardEvent("tq_k", "5411", "premium"), CancellationToken.None);
        await _harness.Engine.ProcessEventAsync(
            RuleEngineTestHarness.TenantSlug, _programId, "evt-2", CardEvent("tq_k", "5541", "premium"), CancellationToken.None);

        _harness.GetBalance("tq_k", _cashAccountTypeId).Should().Be(8m);
    }
}
