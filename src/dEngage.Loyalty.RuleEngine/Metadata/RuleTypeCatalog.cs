using dEngage.Loyalty.Shared;
using dEngage.Loyalty.Shared.Events;

namespace dEngage.Loyalty.RuleEngine.Metadata;

// CR-03 (docs/scope-change-rules): the single compatibility source of truth — used by both
// RulesValidators (Api, structural check at create/update time) and, going forward, the
// rules/metadata endpoint the Angular rule builder (CR-11) reads instead of hardcoding its own
// RULE_TYPES/EVENT_TRIGGERS arrays. TIER_POINTS is deferred (see plan) — no rule type here lists
// it as a valid target. CR 2026-10-05: StampRule and ExpiryRule are retired and STAMP is no
// longer a target, so neither the API nor the rule builder offers them.
public sealed record RuleTypeMetadata(
    string RuleType,
    EventCategory Category,
    IReadOnlyList<EventFieldKind> RequiredKinds,
    IReadOnlyList<string> ValidTargetAccountKinds,
    string Note);

public static class RuleTypeCatalog
{
    public static readonly IReadOnlyDictionary<string, RuleTypeMetadata> Catalog =
        new Dictionary<string, RuleTypeMetadata>
        {
            [RuleTypes.FixedBonusRule] = new(RuleTypes.FixedBonusRule, EventCategory.Earn,
                Array.Empty<EventFieldKind>(), new[] { "POINTS", "CASH" },
                "Awards a constant. Needs nothing from the payload."),

            [RuleTypes.SpendRule] = new(RuleTypes.SpendRule, EventCategory.Earn,
                new[] { EventFieldKind.Money }, new[] { "POINTS", "CASH" },
                "Rate applied to the event amount after currency normalisation."),

            [RuleTypes.RedemptionRule] = new(RuleTypes.RedemptionRule, EventCategory.Burn,
                new[] { EventFieldKind.Number }, new[] { "POINTS" },
                "Debits. Balance is checked inside the posting transaction."),

            [RuleTypes.TransferRule] = new(RuleTypes.TransferRule, EventCategory.Burn,
                new[] { EventFieldKind.Number, EventFieldKind.String }, new[] { "POINTS" },
                "Two postings under one transaction id."),

            [RuleTypes.ReversalRule] = new(RuleTypes.ReversalRule, EventCategory.Reverse,
                new[] { EventFieldKind.Money, EventFieldKind.String }, Array.Empty<string>(),
                "Reads the historical posting, not the current rule config. Target is inherited, never configured."),

            [RuleTypes.ManualAdjustmentRule] = new(RuleTypes.ManualAdjustmentRule, EventCategory.Adjust,
                new[] { EventFieldKind.String }, new[] { "POINTS", "CASH" },
                "Requires an authenticated operator and a reason code.")
        };

    // compatible(event, rule) = event.category == rule.category AND rule.requiredKinds ⊆ kindsOf(event.fields)
    // A trigger with no known EventDefinition (a tenant-approved generic event type — these
    // don't carry category/field metadata yet, see EventTypes catalog) is treated as compatible
    // with everything: there is nothing to structurally rule out, and generic events are freely
    // assignable to any rule type today, so this check must not regress that.
    // CR 2026-09-30 item 8: category + field kinds alone are too loose for the burn events —
    // RedemptionRule only needs "a Number field", which let it onto points.transfer. These events
    // each have exactly one meaningful rule type (and reward.purchase has none: it is configured
    // through the reward definition, never through rules). Checked on top of the category/kind
    // test, never instead of it; events not listed here are unaffected.
    // CR 2026-10-06 D22: order.refunded accepts no rule either — the built-in refund
    // (RefundService) always reverses the order's earn, so a ReversalRule there could only reverse
    // it a second time. ReversalRule stays available for tenant-defined events.
    private static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> AllowedRuleTypesByEvent =
        new Dictionary<string, IReadOnlyList<string>>
        {
            [EventTypes.PointsTransfer] = new[] { RuleTypes.TransferRule },
            [EventTypes.PointsRedeem] = new[] { RuleTypes.RedemptionRule },
            [EventTypes.RewardPurchase] = Array.Empty<string>(),
            [EventTypes.OrderRefunded] = Array.Empty<string>(),
        };

    public static bool IsCompatible(EventDefinition? evt, string ruleType)
    {
        if (!Catalog.TryGetValue(ruleType, out var meta)) return false;
        if (evt is null) return true;
        if (AllowedRuleTypesByEvent.TryGetValue(evt.EventType, out var allowed) && !allowed.Contains(ruleType))
            return false;
        if (evt.Category != meta.Category) return false;

        var availableKinds = evt.Fields.Select(f => f.Kind).ToHashSet();
        return meta.RequiredKinds.All(availableKinds.Contains);
    }

    public static IReadOnlyList<string> CompatibleRuleTypes(EventDefinition? evt) =>
        Catalog.Keys.Where(t => IsCompatible(evt, t)).ToList();

    public static bool IsValidTarget(string ruleType, string accountKind) =>
        Catalog.TryGetValue(ruleType, out var meta) && meta.ValidTargetAccountKinds.Contains(accountKind);
}
