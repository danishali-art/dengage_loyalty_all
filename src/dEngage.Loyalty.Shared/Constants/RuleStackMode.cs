namespace dEngage.Loyalty.Shared;

// CR-06 (docs/scope-change-rules A6): only meaningful when Rule.Stackable is true. A stackable
// rule is either Additive (posts its own computed delta, unchanged — the pre-CR-06 default
// behavior) or a Multiplier (its computed value is a factor applied to the base subtotal in its
// wallet, not posted as its own ledger entry — "multipliers must be stackable; an exclusive
// multiplier is invalid").
public static class RuleStackMode
{
    public const string Additive = "Additive";
    public const string Multiplier = "Multiplier";
}
