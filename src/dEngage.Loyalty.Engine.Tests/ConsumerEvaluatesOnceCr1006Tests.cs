using System.Text.Json;
using dEngage.Loyalty.Consumer;
using dEngage.Loyalty.Consumer.Handlers;
using dEngage.Loyalty.Engine.Framework.Consumers;
using dEngage.Loyalty.Schema;
using dEngage.Loyalty.Shared;
using dEngage.Loyalty.Shared.Events;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace dEngage.Loyalty.Engine.Tests;

// CR 2026-10-06 D18 (R5) through the real EventConsumerWorker path: every event type runs the rule
// engine exactly once per delivery — in its handler or in the worker, never both — so budgets and
// limit counters move once. HandlerEvaluatedEventsTests checks the lists match; this checks the
// runtime result. R15: a failed delivery is processed again, which is why the engine itself must
// recognise an event it already posted (RedeliveryCr1006Tests).
public sealed class ConsumerEvaluatesOnceCr1006Tests : IDisposable
{
    private const string Tenant = "t1";

    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly Mock<ICampaignEvaluationService> _campaignEval = new();
    private readonly ServiceProvider _services;

    public ConsumerEvaluatesOnceCr1006Tests()
    {
        _connection.Open();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<LoyaltyDbContext>(o => o.UseSqlite(_connection));
        services.AddSingleton(_campaignEval.Object);
        services.AddScoped<IEventHandler, OrderCreatedHandler>();
        services.AddScoped<IEventHandler, SignupHandler>();
        services.AddScoped<IEventHandler, KycCompletedHandler>();
        services.AddScoped<IEventHandler, CardTransactionHandler>();
        services.AddScoped<IEventHandler, RemittanceHandler>();
        services.AddScoped<IEventHandler, PointsAdjustedHandler>();
        services.AddScoped<IEventHandler, GenericEventHandler>();
        services.AddScoped<IEventHandlerRegistry, EventHandlerRegistry>();
        _services = services.BuildServiceProvider();

        using var scope = _services.CreateScope();
        scope.ServiceProvider.GetRequiredService<LoyaltyDbContext>().Database.EnsureCreated();
    }

    public void Dispose()
    {
        _services.Dispose();
        _connection.Dispose();
    }

    // ProcessAsync doesn't use RuleSyncService (only the RabbitMQ listener waits on it).
    private EventConsumerWorker Worker() =>
        new(_services.GetRequiredService<IServiceScopeFactory>(), null!, NullLogger<EventConsumerWorker>.Instance);

    private static EventEnvelope Event(string eventType, string eventId) => new()
    {
        EventId = eventId,
        EventType = eventType,
        Tenant = Tenant,
        OccurredAt = DateTime.UtcNow,
        Data = JsonSerializer.SerializeToElement(new { contact_key = "c1", amount = "10", channel = "web" })
    };

    private void VerifyEvaluated(string eventId, int times) =>
        _campaignEval.Verify(c => c.EvaluateAsync(It.Is<EventEnvelope>(e => e.EventId == eventId), It.IsAny<CancellationToken>()),
            Times.Exactly(times));

    [Theory]
    [InlineData(EventTypes.OrderCreated)]
    [InlineData(EventTypes.Signup)]
    [InlineData(EventTypes.KycCompleted)]
    [InlineData(EventTypes.CardTransaction)]
    [InlineData(EventTypes.Remittance)]
    [InlineData(EventTypes.PointsAdjusted)]
    [InlineData("tenant.custom_event")]
    public async Task Each_event_type_runs_the_rule_engine_once_per_delivery(string eventType)
    {
        await Worker().ProcessAsync(Event(eventType, "e1"), CancellationToken.None);

        VerifyEvaluated("e1", 1);
    }

    [Fact]
    public async Task A_processed_event_is_not_evaluated_again()
    {
        var worker = Worker();
        await worker.ProcessAsync(Event(EventTypes.OrderCreated, "e2"), CancellationToken.None);
        await worker.ProcessAsync(Event(EventTypes.OrderCreated, "e2"), CancellationToken.None);

        VerifyEvaluated("e2", 1);
    }

    [Fact]
    public async Task A_failed_delivery_is_evaluated_again_when_redelivered()
    {
        _campaignEval.SetupSequence(c => c.EvaluateAsync(It.Is<EventEnvelope>(e => e.EventId == "e3"), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("boom"))
            .Returns(Task.CompletedTask);
        var worker = Worker();

        var first = () => worker.ProcessAsync(Event(EventTypes.OrderCreated, "e3"), CancellationToken.None);
        await first.Should().ThrowAsync<InvalidOperationException>();
        await worker.ProcessAsync(Event(EventTypes.OrderCreated, "e3"), CancellationToken.None);

        VerifyEvaluated("e3", 2);
        using var scope = _services.CreateScope();
        scope.ServiceProvider.GetRequiredService<LoyaltyDbContext>().EventInbox.AsNoTracking()
            .Single(x => x.TenantId == Tenant && x.EventId == "e3").Status.Should().Be(InboxStatus.Processed);
    }
}
