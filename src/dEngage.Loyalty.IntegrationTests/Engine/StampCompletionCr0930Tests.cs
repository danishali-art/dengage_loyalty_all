using System.Text.Json;
using dEngage.Loyalty.IntegrationTests.Fixtures;
using dEngage.Loyalty.RuleEngine.Cache;
using dEngage.Loyalty.RuleEngine.Models;
using dEngage.Loyalty.Shared;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace dEngage.Loyalty.IntegrationTests.Engine;

// CR 2026-09-30 (A1 + O1): stamp-completion reward definitions are retired, but a full stamp card
// still resets and is still announced — loyalty.reward.earned named after the STAMP account's
// config.reward_type, with reward_type null — so existing listeners keep working.
public sealed class StampCompletionCr0930Tests : IDisposable
{
    private readonly RuleEngineTestHarness _harness = new();
    private readonly Guid _programId;
    private readonly Guid _stampAccountTypeId;

    public StampCompletionCr0930Tests()
    {
        _programId = _harness.AddProgram();
        _stampAccountTypeId = _harness.AddAccountType(_programId, "STAMP", "Coffee card",
            config: """{"stamp_target": 3, "reward_type": "free_coffee"}""");

        _harness.LimitCache.Setup(c => c.GetTotalAsync(RuleEngineTestHarness.TenantSlug, It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0m);
        _harness.LimitCache.Setup(c => c.GetDailyAsync(RuleEngineTestHarness.TenantSlug, It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0m);

        _harness.AddRule(new CachedRule
        {
            Id = Guid.NewGuid(), ProgramId = _programId, Name = "Stamp per coffee",
            Type = RuleTypes.StampRule, Trigger = "order.created",
            Calculation = new RuleCalculation(),
            TargetAccountTypeId = _stampAccountTypeId,
            Priority = 10, Stackable = false, Version = 1
        });
    }

    public void Dispose() => _harness.Dispose();

    private Task BuyAsync(string eventId) =>
        _harness.Engine.ProcessEventAsync(RuleEngineTestHarness.TenantSlug, _programId, eventId, new EvaluationEvent
        {
            EventType = "order.created",
            ContactKey = "c1",
            Amount = 5m,
            OccurredAt = DateTime.UtcNow,
            Data = JsonSerializer.SerializeToElement(new { contact_key = "c1", amount = "5" })
        }, CancellationToken.None);

    [Fact]
    public async Task A_full_stamp_card_still_publishes_reward_earned_without_a_reward_definition()
    {
        await BuyAsync("evt-1");
        await BuyAsync("evt-2");
        await BuyAsync("evt-3");

        _harness.GetBalance("c1", _stampAccountTypeId).Should().Be(0m); // reset after the 3rd stamp

        var log = await _harness.Db.RewardLogs.AsNoTracking().SingleAsync(l => l.ContactKey == "c1");
        log.RewardName.Should().Be("free_coffee");
        log.RewardDefinitionId.Should().BeNull();
        log.Status.Should().Be(RewardLogStatus.Notified);

        _harness.OutboxHasDedupKey($"reward_earned:evt-3:{_stampAccountTypeId}").Should().BeTrue();
        var payload = _harness.Db.OutboxEvents.AsNoTracking()
            .Single(o => o.DedupKey == $"reward_earned:evt-3:{_stampAccountTypeId}").Payload;
        payload.Should().Contain("free_coffee").And.Contain(RewardAcquisition.StampCompletion);
    }
}
