using dEngage.Loyalty.Api.Rules;
using dEngage.Loyalty.RuleEngine.Models;
using FluentAssertions;
using Xunit;

namespace dEngage.Loyalty.IntegrationTests.Api;

// Rule limits were unvalidated server-side. Same cases as the portal's rule-limits.spec.ts —
// the API and the form must accept exactly the same values.
public sealed class RuleLimitsValidatorTests
{
    private readonly RuleLimitsValidator _validator = new();

    private bool IsValid(RuleLimits limits) => _validator.Validate(limits).IsValid;

    [Fact]
    public void Empty_limits_are_valid() => IsValid(new RuleLimits()).Should().BeTrue();

    [Theory]
    [InlineData("250", true)]
    [InlineData("12.5", true)]
    [InlineData("0.0001", true)]
    [InlineData("1.23456", false)] // more than 4 decimal places
    [InlineData("0", false)]
    [InlineData("-5", false)]
    public void Point_and_amount_caps_are_positive_with_at_most_4_decimals(string value, bool valid)
    {
        var amount = decimal.Parse(value, System.Globalization.CultureInfo.InvariantCulture);
        IsValid(new RuleLimits { PerCustomerTotal = amount }).Should().Be(valid);
        IsValid(new RuleLimits { MaxPerEvent = amount }).Should().Be(valid);
        IsValid(new RuleLimits { MinEventAmount = amount }).Should().Be(valid);
        IsValid(new RuleLimits { RuleBudgetTotal = amount }).Should().Be(valid);
    }

    [Theory]
    [InlineData("24", true)]
    [InlineData("1.5", true)]
    [InlineData("1.25", true)]
    [InlineData("1.255", false)]
    [InlineData("0", false)]
    public void Cooldown_hours_are_positive_with_at_most_2_decimals(string value, bool valid) =>
        IsValid(new RuleLimits { CooldownHours = decimal.Parse(value, System.Globalization.CultureInfo.InvariantCulture) })
            .Should().Be(valid);

    [Theory]
    [InlineData(1, true)]
    [InlineData(1000, true)]
    [InlineData(0, false)]
    [InlineData(-1, false)]
    public void Max_customers_is_at_least_one(int value, bool valid) =>
        IsValid(new RuleLimits { MaxCustomers = value }).Should().Be(valid);

    [Fact]
    public void A_per_period_cap_needs_its_period()
    {
        IsValid(new RuleLimits { PerCustomerPerPeriod = 50 }).Should().BeFalse();
        IsValid(new RuleLimits { RuleBudgetPerPeriod = 50 }).Should().BeFalse();
        IsValid(new RuleLimits { RuleBudgetPerPeriod = 50, Period = "Week" }).Should().BeTrue();
    }

    [Theory]
    [InlineData("Fortnight", "Calendar", "Clamp")]
    [InlineData("Day", "Sliding", "Clamp")]
    [InlineData("Day", "Calendar", "Partial")]
    public void Period_reset_window_and_on_breach_are_closed_sets(string period, string resetWindow, string onBreach) =>
        IsValid(new RuleLimits { Period = period, ResetWindow = resetWindow, OnBreach = onBreach }).Should().BeFalse();

    [Fact]
    public void Smaller_windows_cannot_exceed_the_cap_above_them()
    {
        IsValid(new RuleLimits { PerCustomerTotal = 100m, PerCustomerPerDay = 100.0001m }).Should().BeFalse();
        IsValid(new RuleLimits { PerCustomerTotal = 100m, PerCustomerPerDay = 100m }).Should().BeTrue();
        IsValid(new RuleLimits { PerCustomerTotal = 100m, PerCustomerPerPeriod = 101m, Period = "Month" }).Should().BeFalse();
        IsValid(new RuleLimits { RuleBudgetTotal = 500m, RuleBudgetPerPeriod = 600m, Period = "Month" }).Should().BeFalse();
    }
}
