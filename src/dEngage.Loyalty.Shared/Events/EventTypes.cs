namespace dEngage.Loyalty.Shared.Events;

// CR-01 (docs/scope-change-rules): every event declares category/source/cardinality, which
// drives rule-type filtering (RuleEngine.Metadata / CR-03) and posting sign. This is the single
// source of truth read by both the API (RulesValidators) and the engine (RuleMatcher) — see
// docs/scope-changes for the change record.
public enum EventCategory { Earn, Burn, Reverse, Adjust }

public enum EventSource { Behavioural, Scheduled, Operator }

public enum EventCardinality { Unlimited, OncePerCustomer, OncePerPeriod }

public enum EventPeriod { Yearly }

public enum EventFieldKind { Money, Number, String }

public sealed record EventFieldSchema(string Path, EventFieldKind Kind);

public sealed record EventDefinition(
    string EventType,
    EventCategory Category,
    EventSource Source,
    EventCardinality Cardinality,
    EventPeriod? Period,
    IReadOnlyList<EventFieldSchema> Fields);

public static class EventTypes
{
    public const string OrderCreated = "order.created";
    public const string OrderRefunded = "order.refunded";
    public const string CashAdded = "cash.added";
    public const string CashSpent = "cash.spent";
    public const string PointsRedeem = "points.redeem";
    public const string PointsTransfer = "points.transfer";
    public const string RewardPurchase = "reward.purchase";

    // CR-01 additions. Signup/KycCompleted/CardTransaction/Remittance close gaps A2 lists
    // alongside points.expired/points.adjusted that Part B's change-log text under-counted
    // (see docs/scope-changes changelog for this branch). (PointsExpired and BirthdayBonus were
    // retired by CR 2026-10-05 and its addendum A, see below.)
    public const string Signup = "signup";
    public const string KycCompleted = "kyc.completed";
    public const string CardTransaction = "card.transaction";
    public const string Remittance = "remittance";
    public const string PointsAdjusted = "points.adjusted";

    // CR 2026-10-05 (D15): retired rule trigger — no longer a built-in event, and rules/streaks
    // can't use it. Nothing ever published it (wallet expiry posts directly in
    // PointsExpirationJob and sends the outbound loyalty.points.expired, which is unrelated).
    // Kept only so the API can reject it by name: otherwise an unknown trigger is treated as a
    // tenant generic type and would be accepted.
    public const string PointsExpired = "points.expired";

    // CR 2026-10-05 addendum A (A-D4): the birthday bonus was removed end to end (job, worker,
    // birthday endpoint, customer_birthdays table). Retired the same way as PointsExpired — kept
    // only so rules and streak campaigns can reject it by name.
    public const string BirthdayBonus = "birthdaybonus";

    public static readonly string[] All =
    {
        OrderCreated, OrderRefunded, CashAdded, CashSpent,
        PointsRedeem, PointsTransfer, RewardPurchase,
        Signup, KycCompleted, CardTransaction, Remittance,
        PointsAdjusted
    };

    /// <summary>Triggers retired by CR 2026-10-05 — rules and streak campaigns refuse them.</summary>
    public static bool IsRetiredTrigger(string eventType) => eventType is PointsExpired or BirthdayBonus;

    public static bool IsBuiltIn(string eventType) => Array.IndexOf(All, eventType) >= 0;

    // A2 event taxonomy table. Field schemas are suggestions surfaced by the rule builder
    // (CR-11) for autocomplete; undeclared payload paths remain permitted at runtime (A5).
    //
    // Field paths are flat, bare, snake_case names (e.g. "amount", "tx_status"), matching this
    // system's ACTUAL established payload convention — confirmed against every existing
    // Consumer handler (CashAddedHandler, PointsTransferHandler, OrderRefundedHandler, etc.)
    // and CardBucketConditionTests' real card.transaction fixture. A2/A5's illustrative
    // "tx.*"/"profile.*" namespaced paths do NOT reflect this codebase's payload shape, which
    // is flat — only "event.*" (envelope-derived, e.g. event.hour_of_day) and "agg.*"
    // (context-computed, e.g. agg.tier) are real namespaces here, both handled specially by
    // GroupedConditionEvaluator rather than resolved from the payload.
    public static readonly IReadOnlyDictionary<string, EventDefinition> Catalog =
        new Dictionary<string, EventDefinition>
        {
            [Signup] = new(Signup, EventCategory.Earn, EventSource.Behavioural, EventCardinality.OncePerCustomer, null,
                new[] { new EventFieldSchema("segment", EventFieldKind.String) }),

            [KycCompleted] = new(KycCompleted, EventCategory.Earn, EventSource.Behavioural, EventCardinality.OncePerCustomer, null,
                new[] { new EventFieldSchema("segment", EventFieldKind.String) }),

            // Confirmed contract: CardBucketConditionMapper/CardBucketConditionTests.
            [CardTransaction] = new(CardTransaction, EventCategory.Earn, EventSource.Behavioural, EventCardinality.Unlimited, null,
                new[]
                {
                    new EventFieldSchema("amount", EventFieldKind.Money),
                    new EventFieldSchema("mcc", EventFieldKind.String),
                    new EventFieldSchema("country", EventFieldKind.String),
                    new EventFieldSchema("tx_status", EventFieldKind.String),
                    new EventFieldSchema("channel", EventFieldKind.String),
                    new EventFieldSchema("event.hour_of_day", EventFieldKind.Number),
                    new EventFieldSchema("event.day_of_week", EventFieldKind.String)
                }),

            // Confirmed contract: EvaluationEvent.FromEnvelope resolves "amount"/"channel"
            // generically for every Behavioural event.
            [OrderCreated] = new(OrderCreated, EventCategory.Earn, EventSource.Behavioural, EventCardinality.Unlimited, null,
                new[]
                {
                    new EventFieldSchema("amount", EventFieldKind.Money),
                    new EventFieldSchema("channel", EventFieldKind.String)
                }),

            [Remittance] = new(Remittance, EventCategory.Earn, EventSource.Behavioural, EventCardinality.Unlimited, null,
                new[]
                {
                    new EventFieldSchema("amount", EventFieldKind.Money),
                    new EventFieldSchema("channel", EventFieldKind.String)
                }),

            // Confirmed contract: CashAddedHandler ("amount", "account_type_id").
            [CashAdded] = new(CashAdded, EventCategory.Earn, EventSource.Behavioural, EventCardinality.Unlimited, null,
                new[] { new EventFieldSchema("amount", EventFieldKind.Money) }),

            // Confirmed contract: CashSpentHandler ("amount", "account_type_id").
            [CashSpent] = new(CashSpent, EventCategory.Earn, EventSource.Behavioural, EventCardinality.Unlimited, null,
                new[] { new EventFieldSchema("amount", EventFieldKind.Money) }),

            // Confirmed contract: PointsRedeemHandler ("points_amount", "source_account_type_id").
            [PointsRedeem] = new(PointsRedeem, EventCategory.Burn, EventSource.Behavioural, EventCardinality.Unlimited, null,
                new[] { new EventFieldSchema("points_amount", EventFieldKind.Number) }),

            // Confirmed contract: RewardPurchaseHandler ("reward_name" or "reward_id", "channel").
            // reward_id added by CR 2026-09-30 (§3.7).
            [RewardPurchase] = new(RewardPurchase, EventCategory.Burn, EventSource.Behavioural, EventCardinality.Unlimited, null,
                new[]
                {
                    new EventFieldSchema("reward_name", EventFieldKind.String),
                    new EventFieldSchema("reward_id", EventFieldKind.String),
                    new EventFieldSchema("channel", EventFieldKind.String)
                }),

            // Confirmed contract: PointsTransferHandler ("points_amount", "target_contact_key").
            [PointsTransfer] = new(PointsTransfer, EventCategory.Burn, EventSource.Behavioural, EventCardinality.Unlimited, null,
                new[]
                {
                    new EventFieldSchema("points_amount", EventFieldKind.Number),
                    new EventFieldSchema("target_contact_key", EventFieldKind.String)
                }),

            // Confirmed contract: OrderRefundedHandler ("amount", "original_event_id",
            // "original_amount", "refund_ratio").
            [OrderRefunded] = new(OrderRefunded, EventCategory.Reverse, EventSource.Behavioural, EventCardinality.Unlimited, null,
                new[]
                {
                    new EventFieldSchema("amount", EventFieldKind.Money),
                    new EventFieldSchema("original_event_id", EventFieldKind.String)
                }),

            [PointsAdjusted] = new(PointsAdjusted, EventCategory.Adjust, EventSource.Operator, EventCardinality.Unlimited, null,
                new[] { new EventFieldSchema("reason_code", EventFieldKind.String) })
        };

    public static EventDefinition? Describe(string eventType) =>
        Catalog.TryGetValue(eventType, out var def) ? def : null;

    // Only Behavioural/Operator events are acceptable from external POST /events — Scheduled
    // events are synthesized internally and must never be
    // spoofable by a caller (A2/A10 guarantee #10: deterministic, engine-owned scheduling).
    public static bool IsExternallyPublishable(string eventType) =>
        Describe(eventType) is not { Source: EventSource.Scheduled };
}
