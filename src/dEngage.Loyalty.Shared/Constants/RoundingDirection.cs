namespace dEngage.Loyalty.Shared;

// CR 2026-10-06 Phase 4 (docs/scope-changes/2026-10-05-rule-config-limits-by-trigger.md §3.8):
// the direction an award is rounded in — a rule's Configuration.rounding, or the program's
// default_rounding when the rule inherits. The precision always comes from the target wallet's
// decimals. Directions apply to the award's size, so a negative adjustment is rounded the same
// way as a positive one (Down = toward zero).
public static class RoundingDirection
{
    public const string Down = "down";
    public const string Nearest = "nearest";
    public const string Up = "up";

    public static readonly string[] All = [Down, Nearest, Up];
}
