using System.Text.Json;
using dEngage.Loyalty.IntegrationTests.Fixtures;
using dEngage.Loyalty.RuleEngine.Models;
using dEngage.Loyalty.Shared;
using FluentAssertions;
using Xunit;

namespace dEngage.Loyalty.IntegrationTests.Engine;

// Tiqmo TQ02 — SpendRule on a generic "remittance" event: rate 0.1, floored, and a delta that
// floors to zero must not touch the ledger at all (the winner slot is reserved for a rule that
// actually produces a delta).
public sealed class SpendRuleHandlerTests : IDisposable
{
    private readonly RuleEngineTestHarness _harness = new();
    private readonly Guid _programId;
    private readonly Guid _pointsAccountTypeId;

    public SpendRuleHandlerTests()
    {
        _programId = _harness.AddProgram();
        _pointsAccountTypeId = _harness.AddAccountType(_programId, "POINTS", "FinPuan");

        _harness.AddRule(new CachedRule
        {
            Id = Guid.NewGuid(),
            ProgramId = _programId,
            Name = "Remittance Points",
            Type = RuleTypes.SpendRule,
            Trigger = "remittance",
            Conditions = null,
            Calculation = new RuleCalculation { Factor = 0.1m },
            TargetAccountTypeId = _pointsAccountTypeId,
            Priority = 100,
            Stackable = false,
            Version = 1
        });
    }

    public void Dispose() => _harness.Dispose();

    private static EvaluationEvent RemittanceEvent(string contact, decimal amount) => new()
    {
        EventType = "remittance",
        ContactKey = contact,
        Amount = amount,
        OccurredAt = DateTime.UtcNow,
        Data = JsonSerializer.SerializeToElement(new { contact_key = contact, amount = amount.ToString("F2"), channel = "app" })
    };

    [Fact]
    public async Task Remittance_earns_points_at_rate_floored_down()
    {
        await _harness.Engine.ProcessEventAsync(
            RuleEngineTestHarness.TenantSlug, _programId, "evt-1", RemittanceEvent("tq_d", 1055.99m), CancellationToken.None);

        _harness.GetBalance("tq_d", _pointsAccountTypeId).Should().Be(105m);
    }

    [Fact]
    public async Task A_delta_that_floors_to_zero_is_not_written_to_the_ledger()
    {
        await _harness.Engine.ProcessEventAsync(
            RuleEngineTestHarness.TenantSlug, _programId, "evt-1", RemittanceEvent("tq_d", 1055.99m), CancellationToken.None);
        await _harness.Engine.ProcessEventAsync(
            RuleEngineTestHarness.TenantSlug, _programId, "evt-2", RemittanceEvent("tq_d", 9.99m), CancellationToken.None);

        _harness.GetBalance("tq_d", _pointsAccountTypeId).Should().Be(105m);
        _harness.LedgerCount("tq_d", _pointsAccountTypeId, LedgerReason.Earn).Should().Be(1);
    }
}
