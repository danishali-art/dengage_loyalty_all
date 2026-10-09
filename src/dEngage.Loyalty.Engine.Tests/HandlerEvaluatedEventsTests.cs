using System.Runtime.CompilerServices;
using dEngage.Loyalty.Consumer;
using dEngage.Loyalty.Engine.Framework.Consumers;
using dEngage.Loyalty.Shared.Events;
using FluentAssertions;
using Xunit;

namespace dEngage.Loyalty.Engine.Tests;

// CR 2026-10-06 D18: the rule engine runs exactly once per event — either in the event's handler
// (the types in HandlerEvaluatedEvents) or in EventConsumerWorker, never both. A handler that
// takes ICampaignEvaluationService evaluates itself, so the two lists must match exactly.
public class HandlerEvaluatedEventsTests
{
    private static readonly IReadOnlyList<Type> HandlerTypes = typeof(EventConsumerWorker).Assembly.GetTypes()
        .Where(t => t is { IsClass: true, IsAbstract: false } && typeof(IEventHandler).IsAssignableFrom(t))
        .ToList();

    // EventType is a constant-returning property, so it can be read without the constructor's
    // dependencies.
    private static string? EventTypeOf(Type handlerType) =>
        ((IEventHandler)RuntimeHelpers.GetUninitializedObject(handlerType)).EventType;

    private static bool EvaluatesItself(Type handlerType) =>
        handlerType.GetConstructors().Any(c => c.GetParameters().Any(p => p.ParameterType == typeof(ICampaignEvaluationService)));

    [Fact]
    public void Every_handler_that_runs_the_engine_itself_is_left_out_of_the_workers_evaluation()
    {
        var evaluatingHandlers = HandlerTypes.Where(EvaluatesItself).Select(EventTypeOf).ToList();

        evaluatingHandlers.Should().NotContainNulls();
        evaluatingHandlers.Should().BeEquivalentTo(HandlerEvaluatedEvents.All,
            "a handler that calls the engine and a worker that calls it too run it twice for one event, " +
            "and a listed type whose handler doesn't call it would never be evaluated");
    }

    [Theory]
    [InlineData(EventTypes.Signup)]
    [InlineData(EventTypes.KycCompleted)]
    [InlineData(EventTypes.CardTransaction)]
    [InlineData(EventTypes.Remittance)]
    [InlineData(EventTypes.PointsAdjusted)]
    [InlineData(EventTypes.OrderCreated)]
    [InlineData(EventTypes.CashSpent)]
    public void Events_whose_handler_runs_the_engine_are_evaluated_by_the_handler_only(string eventType) =>
        HandlerEvaluatedEvents.All.Should().Contain(eventType);

    [Theory]
    [InlineData(EventTypes.CashAdded)]
    [InlineData(EventTypes.OrderRefunded)]
    [InlineData(EventTypes.PointsRedeem)]
    [InlineData(EventTypes.PointsTransfer)]
    [InlineData(EventTypes.RewardPurchase)]
    [InlineData("tenant.custom_event")]
    public void Other_events_are_evaluated_by_the_worker(string eventType) =>
        HandlerEvaluatedEvents.All.Should().NotContain(eventType);
}
