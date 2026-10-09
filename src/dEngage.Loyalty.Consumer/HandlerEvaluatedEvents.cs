using dEngage.Loyalty.Shared.Events;

namespace dEngage.Loyalty.Consumer;

// The event types whose handler calls ICampaignEvaluationService itself, so the worker must not
// call it again — otherwise the rule engine runs twice for one event: postings are deduped, but
// budget reservations and Redis limit counters are not (backend-consumer.md).
// CR 2026-10-06 D18: only order.created was listed, so signup, kyc.completed, card.transaction,
// remittance and points.adjusted ran the engine twice and used their limits up twice as fast.
// D23: cash.spent evaluates in its handler, and only for a spend that happened.
// HandlerEvaluatedEventsTests keeps this list and the handlers in step.
internal static class HandlerEvaluatedEvents
{
    public static readonly IReadOnlySet<string> All = new HashSet<string>
    {
        EventTypes.OrderCreated,
        EventTypes.Signup,
        EventTypes.KycCompleted,
        EventTypes.CardTransaction,
        EventTypes.Remittance,
        EventTypes.PointsAdjusted,
        EventTypes.CashSpent,
    };
}
