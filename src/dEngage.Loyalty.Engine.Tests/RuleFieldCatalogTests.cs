using dEngage.Loyalty.RuleEngine.Metadata;
using dEngage.Loyalty.RuleEngine.Models;
using dEngage.Loyalty.Shared;
using dEngage.Loyalty.Shared.Events;
using FluentAssertions;
using Xunit;
using F = dEngage.Loyalty.RuleEngine.Metadata.RuleFieldCatalog;

namespace dEngage.Loyalty.Engine.Tests;

// CR 2026-10-06 Phase 2: the fields shown and accepted per trigger group — the tables in
// docs/scope-changes/2026-10-05-rule-config-limits-by-trigger.md §3.2. The portal reads the same
// list from GET rules/metadata, so these cases are the single place the tables are checked.
public class RuleFieldCatalogTests
{
    private static F.ApplicableFields For(string? trigger, string type) =>
        F.For(trigger is null ? null : EventTypes.Describe(trigger), type);

    [Theory]
    [InlineData(EventTypes.OrderCreated, RuleTypes.SpendRule)]
    [InlineData(EventTypes.CardTransaction, RuleTypes.SpendRule)]
    [InlineData(EventTypes.Remittance, RuleTypes.SpendRule)]
    [InlineData(EventTypes.CashAdded, RuleTypes.SpendRule)]
    [InlineData(EventTypes.CashSpent, RuleTypes.SpendRule)]
    public void Amount_based_earn_with_spend_gets_every_field(string trigger, string type)
    {
        var fields = For(trigger, type);
        fields.Configuration.Should().BeEquivalentTo(F.AllConfiguration);
        fields.Limits.Should().BeEquivalentTo(F.AllLimits);
    }

    [Fact]
    public void A_fixed_bonus_on_an_amount_event_has_no_max_per_event()
    {
        var fields = For(EventTypes.OrderCreated, RuleTypes.FixedBonusRule);
        fields.Configuration.Should().BeEquivalentTo(F.AllConfiguration);
        fields.Limits.Should().NotContain(F.MaxPerEvent).And.HaveCount(F.AllLimits.Count - 1);
    }

    [Theory]
    [InlineData(EventTypes.Signup)]
    [InlineData(EventTypes.KycCompleted)]
    public void One_time_earn_drops_amount_customer_caps_delayed_posting_and_reversible(string trigger)
    {
        var fields = For(trigger, RuleTypes.FixedBonusRule);
        fields.Configuration.Should().BeEquivalentTo([F.Rounding, F.ExpiryOverrideDays, F.NotifyOnAward]);
        fields.Limits.Should().BeEquivalentTo([F.MaxCustomers, F.RuleBudgetTotal, F.RuleBudgetPerPeriod, F.Period, F.ResetWindow, F.OnBreach]);
    }

    [Theory]
    [InlineData(EventTypes.PointsRedeem, RuleTypes.RedemptionRule)]
    [InlineData(EventTypes.PointsTransfer, RuleTypes.TransferRule)]
    public void Burn_rules_keep_only_cooldown_max_customers_and_budgets(string trigger, string type)
    {
        var fields = For(trigger, type);
        fields.Configuration.Should().BeEmpty();
        fields.Limits.Should().BeEquivalentTo([F.CooldownHours, F.MaxCustomers, F.RuleBudgetTotal, F.RuleBudgetPerPeriod, F.Period, F.ResetWindow]);
    }

    [Fact]
    public void A_manual_adjustment_keeps_only_rounding()
    {
        var fields = For(EventTypes.PointsAdjusted, RuleTypes.ManualAdjustmentRule);
        fields.Configuration.Should().Equal(F.Rounding);
        fields.Limits.Should().BeEmpty();
    }

    [Theory]
    [InlineData("tenant.order_cancelled")]
    [InlineData(null)]
    public void A_reversal_rule_has_no_configuration_or_limits_even_on_a_tenant_defined_event(string? trigger)
    {
        var fields = For(trigger, RuleTypes.ReversalRule);
        fields.Configuration.Should().BeEmpty();
        fields.Limits.Should().BeEmpty();
    }

    [Fact]
    public void A_tenant_defined_trigger_keeps_every_field()
    {
        var fields = For(null, RuleTypes.SpendRule);
        fields.Configuration.Should().BeEquivalentTo(F.AllConfiguration);
        fields.Limits.Should().BeEquivalentTo(F.AllLimits);
    }

    [Fact]
    public void Defaults_are_never_reported_as_set()
    {
        var none = For(EventTypes.Signup, RuleTypes.FixedBonusRule);
        F.SetButNotApplicable(none, new RuleSettings(), new RuleLimits()).Should().BeEmpty(
            "Immediate posting, reversible, no limits and On breach Clamp are the defaults a client may always send");
    }

    [Fact]
    public void Non_default_values_in_fields_that_dont_apply_are_reported()
    {
        var oneTime = For(EventTypes.KycCompleted, RuleTypes.FixedBonusRule);
        var set = F.SetButNotApplicable(oneTime,
            new RuleSettings { Posting = "Delayed", HoldDays = 3, Reversible = false, NotifyOnAward = true },
            new RuleLimits { MinEventAmount = 10m, PerCustomerTotal = 100m, MaxCustomers = 1000 });

        set.Should().BeEquivalentTo([F.Posting, F.HoldDays, F.Reversible, F.MinEventAmount, F.PerCustomerTotal]);
    }
}
