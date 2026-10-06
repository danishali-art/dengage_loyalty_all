namespace dEngage.Loyalty.Shared.Events;

// CR 2026-10-05: `reason` values on the outbound *_failed events introduced by that CR. The
// older reasons (insufficient_points, below_minimum, daily_limit_exceeded, ...) are still
// literals in their handlers.
public static class OutcomeReasons
{
    // redeem / transfer: no Active rule of the right type targets the wallet, or none of them
    // has its conditions met.
    public const string NoRule = "no_rule";
    // redeem / transfer: at least one rule's conditions were met, but every such rule's limits or
    // budget are used up.
    public const string RuleLimitReached = "rule_limit_reached";
    // Item 5: the program owning the wallet or reward is not Active + Published.
    public const string ProgramNotLive = "program_not_live";
}
