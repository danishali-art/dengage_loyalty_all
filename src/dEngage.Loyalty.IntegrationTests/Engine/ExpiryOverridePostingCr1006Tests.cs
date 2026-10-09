using System.Text.Json;
using dEngage.Loyalty.IntegrationTests.Fixtures;
using dEngage.Loyalty.RuleEngine.Models;
using dEngage.Loyalty.RuleEngine.Processing;
using dEngage.Loyalty.Shared;
using dEngage.Loyalty.Shared.Events;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace dEngage.Loyalty.IntegrationTests.Engine;

// CR 2026-10-06 Phase 5 (§3.9): points earned through a rule with an expiry override are dated
// from their posting; without one they take the wallet's expiration_days; a held (Delayed)
// posting is dated from its release, not from the event.
public sealed class ExpiryOverridePostingCr1006Tests : IDisposable
{
    private readonly RuleEngineTestHarness _harness = new();
    private readonly Guid _programId;
    private readonly Guid _points;

    public ExpiryOverridePostingCr1006Tests()
    {
        _programId = _harness.AddProgram();
        _points = _harness.AddAccountType(_programId, "POINTS", "Points", config: """{"expiration_days": 365}""");
    }

    public void Dispose() => _harness.Dispose();

    private CachedRule AddRule(RuleSettings? configuration)
    {
        var rule = new CachedRule
        {
            Id = Guid.NewGuid(), ProgramId = _programId, Name = $"bonus-{Guid.NewGuid():N}", Type = RuleTypes.FixedBonusRule,
            Trigger = EventTypes.OrderCreated, Calculation = new RuleCalculation { FixedValue = 10m },
            TargetAccountTypeId = _points, Configuration = configuration, Priority = 10, Version = 1
        };
        _harness.AddRule(rule);
        // The harness stores no configuration on the rule row; the release job reads it from there.
        if (configuration is not null)
        {
            var json = JsonSerializer.Serialize(configuration);
            _harness.Db.Rules.Where(r => r.Id == rule.Id).ExecuteUpdate(s => s.SetProperty(r => r.Configuration, json));
        }
        return rule;
    }

    private Task ProcessAsync(string eventId, string contact) =>
        _harness.Engine.ProcessEventAsync(RuleEngineTestHarness.TenantSlug, _programId, eventId, new EvaluationEvent
        {
            EventType = EventTypes.OrderCreated, ContactKey = contact, Amount = 50m, OccurredAt = DateTime.UtcNow,
            Data = JsonSerializer.SerializeToElement(new { contact_key = contact, amount = "50" })
        }, CancellationToken.None);

    private DateTime? ExpiresAt(Guid ruleId) =>
        _harness.Db.LedgerEntries.AsNoTracking().Single(l => l.RuleId == ruleId).ExpiresAt;

    [Fact]
    public async Task A_rules_expiry_override_dates_the_points_from_their_posting()
    {
        var rule = AddRule(new RuleSettings { ExpiryOverrideDays = 30 });

        await ProcessAsync("e1", "c1");

        ExpiresAt(rule.Id).Should().BeCloseTo(DateTime.UtcNow.AddDays(30), TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task Without_an_override_the_points_take_the_wallets_expiry()
    {
        var rule = AddRule(null);

        await ProcessAsync("e1", "c2");

        ExpiresAt(rule.Id).Should().BeCloseTo(DateTime.UtcNow.AddDays(365), TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task A_held_posting_is_dated_from_its_release_not_from_the_event()
    {
        var rule = AddRule(new RuleSettings { Posting = "Delayed", HoldDays = 14, ExpiryOverrideDays = 30 });
        await ProcessAsync("e1", "c3");

        // 14 days later the hold ends and the release job posts it.
        var held = _harness.Db.HeldPostings.Single(h => h.RuleId == rule.Id);
        held.HoldUntil = DateTime.UtcNow.AddMinutes(-1);
        _harness.Db.SaveChanges();
        var resolver = new dEngage.Loyalty.Schema.TenantSlugResolver(_harness.Db, new dEngage.Loyalty.Schema.TenantSlugCache());
        await new DelayedPostingPromotionJob(_harness.Db, new dEngage.Loyalty.Ledger.LedgerService(_harness.Db, resolver),
                new dEngage.Loyalty.Ledger.OutboxService(_harness.Db, resolver), Mock.Of<dEngage.Loyalty.RuleEngine.ITierEvaluationService>(),
                NullLogger<DelayedPostingPromotionJob>.Instance)
            .RunAsync(CancellationToken.None);

        ExpiresAt(rule.Id).Should().BeCloseTo(DateTime.UtcNow.AddDays(30), TimeSpan.FromMinutes(1));
    }
}
