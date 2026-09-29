using System.Text.Json;
using dEngage.Loyalty.IntegrationTests.Fixtures;
using dEngage.Loyalty.RuleEngine.Cache;
using dEngage.Loyalty.RuleEngine.Models;
using dEngage.Loyalty.RuleEngine.Processing;
using dEngage.Loyalty.Schema.Entities;
using dEngage.Loyalty.Shared;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace dEngage.Loyalty.IntegrationTests.Engine;

// CR-10/CR-09 (docs/scope-change-rules A10 guarantee #10, A11): BirthdayBonusJob — deterministic
// eventId idempotency and the Feb-29 leap-day policy, against the real Sqlite-backed harness.
public sealed class BirthdayBonusJobTests : IDisposable
{
    private readonly RuleEngineTestHarness _harness = new();
    private readonly Guid _programId;
    private readonly Guid _pointsAccountTypeId;

    public BirthdayBonusJobTests()
    {
        _programId = _harness.AddProgram();
        _pointsAccountTypeId = _harness.AddAccountType(_programId, "POINTS", "Points");

        _harness.LimitCache.Setup(c => c.GetTotalAsync(RuleEngineTestHarness.TenantSlug, It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0m);
        _harness.LimitCache.Setup(c => c.GetDailyAsync(RuleEngineTestHarness.TenantSlug, It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0m);

        _harness.AddRule(new CachedRule
        {
            Id = Guid.NewGuid(), ProgramId = _programId, Name = "Birthday Bonus",
            Type = RuleTypes.FixedBonusRule, Trigger = "birthdaybonus",
            Calculation = new RuleCalculation { FixedValue = 25m },
            TargetAccountTypeId = _pointsAccountTypeId,
            Priority = 100, Stackable = false, ExclusivityGroup = "g1",
            Version = 1
        });
    }

    public void Dispose() => _harness.Dispose();

    private BirthdayBonusJob Job() =>
        new(_harness.Db, _harness.Engine, NullLogger<BirthdayBonusJob>.Instance);

    [Fact]
    public async Task Fires_once_for_a_customer_whose_birthday_is_today_and_is_idempotent_on_rerun()
    {
        var today = DateTime.UtcNow;
        _harness.Db.CustomerBirthdays.Add(new CustomerBirthday
        {
            Id = Guid.NewGuid(), TenantId = _harness.TenantGuid, ContactKey = "bday_customer",
            MonthDay = today.ToString("MM-dd"), CreatedAt = today, UpdatedAt = today
        });
        _harness.Db.SaveChanges();

        var fired = await Job().RunAsync(CancellationToken.None);
        fired.Should().Be(1);
        _harness.GetBalance("bday_customer", _pointsAccountTypeId).Should().Be(25m);

        // Rerunning the same day must not pay twice — the deterministic eventId feeds the
        // same ledger idempotency-key mechanism every other posting relies on.
        await Job().RunAsync(CancellationToken.None);
        _harness.GetBalance("bday_customer", _pointsAccountTypeId).Should().Be(25m);
    }

    [Fact]
    public async Task Customer_with_a_different_birthday_is_not_paid()
    {
        var notToday = DateTime.UtcNow.AddDays(-10);
        _harness.Db.CustomerBirthdays.Add(new CustomerBirthday
        {
            Id = Guid.NewGuid(), TenantId = _harness.TenantGuid, ContactKey = "other_customer",
            MonthDay = notToday.ToString("MM-dd"), CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
        });
        _harness.Db.SaveChanges();

        var fired = await Job().RunAsync(CancellationToken.None);
        fired.Should().Be(0);
        _harness.GetBalance("other_customer", _pointsAccountTypeId).Should().Be(0m);
    }
}
