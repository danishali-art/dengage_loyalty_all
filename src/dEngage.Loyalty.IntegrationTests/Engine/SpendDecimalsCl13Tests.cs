using System.Text.Json;
using dEngage.Loyalty.IntegrationTests.Fixtures;
using dEngage.Loyalty.RuleEngine.Cache;
using dEngage.Loyalty.RuleEngine.Models;
using dEngage.Loyalty.Schema;
using dEngage.Loyalty.Shared;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using StackExchange.Redis;
using Xunit;

namespace dEngage.Loyalty.IntegrationTests.Engine;

// 1.3.CL item 2: Spend rules round DOWN to the target wallet's configured decimal places instead
// of always to a whole number — through the real pipeline (WinnerSelector → LedgerPoster).
public sealed class SpendDecimalsCl13Tests : IDisposable
{
    private readonly RuleEngineTestHarness _harness = new();
    private readonly Guid _programId;
    private readonly Guid _pointsAccountTypeId;

    public SpendDecimalsCl13Tests()
    {
        _programId = _harness.AddProgram();
        _pointsAccountTypeId = _harness.AddAccountType(_programId, "POINTS", "Points", config: """{"decimals": 2}""");
    }

    public void Dispose() => _harness.Dispose();

    private void AddSpendRule(int targetDecimals, RuleSettings? configuration = null) => _harness.AddRule(new CachedRule
    {
        Id = Guid.NewGuid(),
        ProgramId = _programId,
        Name = "Spend",
        Type = RuleTypes.SpendRule,
        Trigger = "remittance",
        Calculation = new RuleCalculation { Factor = 0.1m },
        TargetAccountTypeId = _pointsAccountTypeId,
        TargetDecimals = targetDecimals,
        Configuration = configuration,
        Priority = 100,
        Version = 1
    });

    private Task ProcessAsync(string eventId, decimal amount) => _harness.Engine.ProcessEventAsync(
        RuleEngineTestHarness.TenantSlug, _programId, eventId, new EvaluationEvent
        {
            EventType = "remittance",
            ContactKey = "dec_c",
            Amount = amount,
            OccurredAt = DateTime.UtcNow,
            Data = JsonSerializer.SerializeToElement(new { contact_key = "dec_c", amount = amount.ToString("F2") })
        }, CancellationToken.None);

    [Fact]
    public async Task Spend_earn_is_floored_to_the_wallets_decimal_places_and_posted_once()
    {
        AddSpendRule(targetDecimals: 2);

        await ProcessAsync("evt-dec-1", 1055.99m); // 105.599 → 105.59
        await ProcessAsync("evt-dec-1", 1055.99m); // same event id: idempotent

        _harness.GetBalance("dec_c", _pointsAccountTypeId).Should().Be(105.59m);
        _harness.LedgerCount("dec_c", _pointsAccountTypeId, LedgerReason.Earn).Should().Be(1);
    }

    [Fact]
    public async Task Explicit_rule_rounding_uses_the_wallet_precision_not_a_fixed_two_places()
    {
        AddSpendRule(targetDecimals: 3, new RuleSettings { Rounding = "down" });

        await ProcessAsync("evt-dec-2", 1055.9999m); // 105.59999 → 105.599 (not 105.59)

        _harness.GetBalance("dec_c", _pointsAccountTypeId).Should().Be(105.599m);
    }

    [Fact]
    public async Task Rule_cache_carries_the_target_wallets_decimals()
    {
        AddSpendRule(targetDecimals: 0); // the DB row; the cache load must read decimals itself

        var redis = new Mock<IConnectionMultiplexer>();
        redis.Setup(r => r.GetDatabase(It.IsAny<int>(), It.IsAny<object>())).Returns(Mock.Of<IDatabase>());
        var cache = new RuleCacheService(redis.Object, _harness.Db,
            new TenantSlugResolver(_harness.Db, new TenantSlugCache()), NullLogger<RuleCacheService>.Instance);

        var rules = await cache.LoadFromDbAsync(RuleEngineTestHarness.TenantSlug, _programId);

        rules.Should().ContainSingle().Which.TargetDecimals.Should().Be(2);
    }
}
