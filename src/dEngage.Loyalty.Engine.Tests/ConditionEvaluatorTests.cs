using System.Text.Json;
using FluentAssertions;
using dEngage.Loyalty.RuleEngine;
using dEngage.Loyalty.RuleEngine.Models;
using Xunit;

namespace dEngage.Loyalty.Engine.Tests;

public sealed class ConditionEvaluatorTests
{
    private static EvaluationEvent EventAt(DateTime occurredAtUtc) => new()
    {
        EventType = "card.transaction",
        ContactKey = "c1",
        OccurredAt = occurredAtUtc,
        Data = JsonDocument.Parse("{}").RootElement
    };

    private static ConditionClause Clause(string field, string op, object value) => new()
    {
        Field = field,
        Op = op,
        Value = JsonSerializer.SerializeToElement(value)
    };

    private static bool Eval(ConditionClause clause, EvaluationEvent evt) =>
        ConditionEvaluator.Evaluate(new List<ConditionClause> { clause }, evt, ConditionContext.Empty);

    [Theory]
    [InlineData(9, ConditionOps.Gte, 9, true)]
    [InlineData(8, ConditionOps.Gte, 9, false)]
    [InlineData(17, ConditionOps.Lte, 17, true)]
    [InlineData(18, ConditionOps.Lte, 17, false)]
    [InlineData(12, ConditionOps.Eq, 12, true)]
    [InlineData(12, ConditionOps.Eq, 13, false)]
    public void Hour_of_day_gates_on_evt_OccurredAt(int hour, string op, int bound, bool expected)
    {
        var evt = EventAt(new DateTime(2026, 3, 4, hour, 30, 0, DateTimeKind.Utc));
        var clause = Clause("event.hour_of_day", op, bound);

        Eval(clause, evt).Should().Be(expected);
    }

    [Fact]
    public void Day_of_week_eq_matches_the_events_actual_utc_weekday()
    {
        var occurredAt = new DateTime(2026, 3, 4, 12, 0, 0, DateTimeKind.Utc);
        var evt = EventAt(occurredAt);
        var actualDayName = occurredAt.DayOfWeek.ToString().ToLowerInvariant();
        var otherDayName = occurredAt.AddDays(1).DayOfWeek.ToString().ToLowerInvariant();

        Eval(Clause("event.day_of_week", ConditionOps.Eq, actualDayName), evt).Should().BeTrue();
        Eval(Clause("event.day_of_week", ConditionOps.Eq, otherDayName), evt).Should().BeFalse();
        Eval(Clause("event.day_of_week", ConditionOps.Ne, otherDayName), evt).Should().BeTrue();
    }

    [Fact]
    public void Day_of_week_in_matches_a_set_of_days()
    {
        var occurredAt = new DateTime(2026, 3, 4, 12, 0, 0, DateTimeKind.Utc);
        var evt = EventAt(occurredAt);
        var actualDayName = occurredAt.DayOfWeek.ToString().ToLowerInvariant();
        var unrelatedDayName = occurredAt.AddDays(3).DayOfWeek.ToString().ToLowerInvariant();

        Eval(Clause("event.day_of_week", ConditionOps.In, new[] { actualDayName, "friday" }), evt).Should().BeTrue();
        Eval(Clause("event.day_of_week", ConditionOps.In, new[] { unrelatedDayName }), evt).Should().BeFalse();
    }

    [Fact]
    public void Time_fields_combine_with_payload_fields_in_the_same_clause_list()
    {
        var evt = new EvaluationEvent
        {
            EventType = "card.transaction",
            ContactKey = "c1",
            OccurredAt = new DateTime(2026, 3, 4, 10, 0, 0, DateTimeKind.Utc),
            Data = JsonDocument.Parse("""{"mcc":"5411"}""").RootElement
        };

        var conditions = new List<ConditionClause>
        {
            Clause("mcc", ConditionOps.Eq, "5411"),
            Clause("event.hour_of_day", ConditionOps.Gte, 9),
            Clause("event.hour_of_day", ConditionOps.Lte, 17)
        };

        ConditionEvaluator.Evaluate(conditions, evt, ConditionContext.Empty).Should().BeTrue();

        var outsideWindow = evt.OccurredAt.AddHours(10);
        var eveningEvt = new EvaluationEvent { EventType = evt.EventType, ContactKey = evt.ContactKey, OccurredAt = outsideWindow, Data = evt.Data };
        ConditionEvaluator.Evaluate(conditions, eveningEvt, ConditionContext.Empty).Should().BeFalse();
    }
}
