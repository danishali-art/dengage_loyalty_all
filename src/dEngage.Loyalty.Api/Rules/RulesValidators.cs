using FluentValidation;
using dEngage.Loyalty.RuleEngine.Metadata;
using dEngage.Loyalty.RuleEngine.Models;
using dEngage.Loyalty.Shared;
using dEngage.Loyalty.Shared.Events;

namespace dEngage.Loyalty.Api.Rules;

internal static class RetiredStackingMessages
{
    public const string ExclusivityGroup =
        "stacking_field_retired: 'exclusivityGroup' is no longer supported — exclusive rules compete per target account type.";
    public const string StackMode =
        "stacking_field_retired: 'stackMode' only accepts 'Additive' — multiplier stacking is no longer supported.";
}

internal static class RetiredRuleMessages
{
    // CR 2026-10-05 (D15, addendum A-D4): points.expired and birthdaybonus are no longer
    // triggers. They must be rejected by name — an unknown trigger is otherwise treated as a
    // tenant generic type and accepted.
    public static string RetiredTrigger(string? trigger) =>
        $"trigger_retired: '{trigger}' is no longer a rule trigger (retired by CR 2026-10-05).";

    // CR 2026-10-06 D20: test mode was removed from every rule — the field stays on the wire
    // (existing fields are frozen) but only false is accepted.
    public const string TestMode =
        "test_mode_removed: test mode was removed (CR 2026-10-06) — 'configuration.testMode' must be false or omitted. Trial a rule in a staging tenant, with the Event Simulator, or with a condition or a short active window.";
}

// Structural/required-field checks only — the condition DSL itself is validated by
// RuleEngine.Models.ConditionDsl in RulesAppService, reused rather than re-implemented.
// Streak campaigns have their own dedicated validators — see Api/StreakCampaigns.
//
// CR-02 (docs/scope-change-rules): widened from 3 to 8 rule types. Trigger-event/rule-type
// compatibility (A3's "requires" column — e.g. SpendRule needing a money field on the trigger
// event) is CR-03's single compatibility predicate, not duplicated here — this validator only
// checks what's structurally required regardless of which event a rule is attached to.
public sealed class CreateRuleRequestValidator : AbstractValidator<CreateRuleRequest>
{
    public CreateRuleRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(255);
        RuleFor(x => x.Trigger).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Trigger).Must(t => t is null || !EventTypes.IsRetiredTrigger(t))
            .WithMessage(x => RetiredRuleMessages.RetiredTrigger(x.Trigger));
        // CR 2026-10-05: StampRule and ExpiryRule are retired, so they are no longer in RuleTypes.All.
        RuleFor(x => x.Type).Must(t => RuleTypes.All.Contains(t))
            .WithMessage($"Type must be one of: {string.Join(", ", RuleTypes.All)}. " +
                         "Streak campaigns are managed under /streak-campaigns, not /rules.");

        // CR-03: single compatibility predicate, shared with RuleMatcher's runtime matching via
        // RuleTypeCatalog — prevents configuring a rule type the trigger event could never
        // satisfy (e.g. SpendRule on an event with no money field). A trigger with no known EventDefinition
        // (tenant-approved generic event) is always treated as compatible — see
        // RuleTypeCatalog.IsCompatible.
        RuleFor(x => x)
            .Must(x => RuleTypeCatalog.IsCompatible(EventTypes.Describe(x.Trigger), x.Type))
            .WithName("Type")
            .WithMessage(x => $"'{x.Type}' is not valid for trigger '{x.Trigger}' " +
                               $"(category/field mismatch — see GET .../rules/metadata).");

        // Every rule type targets a real account except ReversalRule, whose target is inherited
        // from the original posting at fire time (A3) — configuring one is a validation error,
        // not just unnecessary, since it would never be honored.
        RuleFor(x => x.TargetAccountTypeId).NotNull()
            .When(x => x.Type != RuleTypes.ReversalRule)
            .WithMessage("TargetAccountTypeId is required for this rule type.");
        RuleFor(x => x.TargetAccountTypeId).Null()
            .When(x => x.Type == RuleTypes.ReversalRule)
            .WithMessage("ReversalRule inherits its target account from the original posting and cannot have TargetAccountTypeId set.");

        RuleFor(x => x.Calculation).NotNull()
            .When(x => x.Type is RuleTypes.SpendRule or RuleTypes.FixedBonusRule
                or RuleTypes.RedemptionRule or RuleTypes.TransferRule
                or RuleTypes.ReversalRule)
            .WithMessage("Calculation is required for this rule type.");

        RuleFor(x => x.Calculation!.Factor).NotNull().GreaterThan(0)
            .When(x => x.Type == RuleTypes.SpendRule && x.Calculation is not null)
            .WithMessage("Calculation.rate is required and must be positive for SpendRule.");
        RuleFor(x => x.Calculation!.FixedValue).NotNull().GreaterThan(0)
            .When(x => x.Type == RuleTypes.FixedBonusRule && x.Calculation is not null)
            .WithMessage("Calculation.amount is required and must be positive for FixedBonusRule.");

        // CR 2026-10-06 Phase 2 (D5): a NEW rule may set only the Configuration / Limits fields
        // that apply to its trigger and type (RuleFieldCatalog — the same list GET rules/metadata
        // serves the portal). Edits of existing rules are not checked, so saved values survive.
        RuleFor(x => x).Custom((x, context) =>
        {
            var applicable = RuleFieldCatalog.For(EventTypes.Describe(x.Trigger), x.Type);
            foreach (var field in RuleFieldCatalog.SetButNotApplicable(applicable, x.Configuration, x.Limits))
                context.AddFailure(field,
                    $"field_not_applicable: '{field}' doesn't apply to a {x.Type} on '{x.Trigger}' — leave it unset (see GET .../rules/metadata, applicableFields).");
        }).When(x => RuleTypeCatalog.IsCompatible(EventTypes.Describe(x.Trigger), x.Type));

        // CR 2026-10-05: redeem / transfer rules carry the wallet's fields (cash per point,
        // minimum, Redeem into / daily transfer limit). Shared with RulesAppService's edit path.
        RuleFor(x => x).Custom((x, context) =>
        {
            foreach (var error in BurnRuleCalculationRules.Errors(x.Type, x.Calculation))
                context.AddFailure("Calculation", error);
        }).When(x => x.Calculation is not null);

        RuleFor(x => x.Calculation!.Mode).Must(m => m is "proportional" or "full")
            .When(x => x.Type == RuleTypes.ReversalRule && x.Calculation is not null)
            .WithMessage("Calculation.mode must be 'proportional' or 'full' for ReversalRule.");
        RuleFor(x => x.Calculation!.AllowNegative).Must(v => v is "allow negative" or "clamp to zero")
            .When(x => x.Type == RuleTypes.ReversalRule && x.Calculation is not null)
            .WithMessage("Calculation.allowNegative must be 'allow negative' or 'clamp to zero' for ReversalRule.");

        RuleFor(x => x.Calculation!.Reason).Must(r => r is "goodwill" or "correction" or "dispute" or "migration")
            .When(x => x.Type == RuleTypes.ManualAdjustmentRule && x.Calculation is not null)
            .WithMessage("Calculation.reason is required for ManualAdjustmentRule (goodwill, correction, dispute, or migration).");

        RuleFor(x => x.Priority).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Limits!).SetValidator(new RuleLimitsValidator()).When(x => x.Limits is not null);

        // 1.3.CL item 5: named exclusivity groups and multiplier stacking are retired. Exclusive
        // rules now compete per target wallet (WinnerSelector's existing fallback) and every
        // stackable rule is Additive. The request fields stay so old clients get a clear 400.
        RuleFor(x => x.ExclusivityGroup).Null().WithMessage(RetiredStackingMessages.ExclusivityGroup);
        RuleFor(x => x.StackMode).Null()
            .When(x => !x.Stackable)
            .WithMessage("StackMode cannot be set on a non-stackable rule — an exclusive multiplier is invalid.");
        RuleFor(x => x.StackMode).Must(m => m is null or RuleStackMode.Additive)
            .WithMessage(RetiredStackingMessages.StackMode);

        // A9 blocking rule: "Delayed posting with no hold period."
        RuleFor(x => x.Configuration!.HoldDays).NotNull().GreaterThan(0)
            .When(x => x.Configuration?.Posting == "Delayed")
            .WithMessage("Configuration.holdDays is required and must be positive when Posting is 'Delayed'.");
        // "Pending" (A8: "post as pending, confirm on settlement") is not accepted yet — it
        // needs a caller-triggered settlement-confirmation event this system has no vocabulary
        // for, unlike "Delayed" which only needs elapsed time. Accepting it now and silently
        // treating it as Immediate would be the same misleading-unenforced-setting problem
        // "Delayed" would have had without the held-posting mechanism — better to reject it
        // outright until a real settlement-confirmation design exists.
        RuleFor(x => x.Configuration!.Posting).Must(p => p is "Immediate" or "Delayed")
            .When(x => x.Configuration is not null)
            .WithMessage("Configuration.posting must be 'Immediate' or 'Delayed' ('Pending' is not yet implemented — it needs a settlement-confirmation mechanism this system doesn't have).");
        // CR 2026-10-06 Phase 5 (E1): any whole number of days above 0 — longer or shorter than
        // the wallet's own expiry.
        RuleFor(x => x.Configuration!.ExpiryOverrideDays).GreaterThan(0)
            .When(x => x.Configuration?.ExpiryOverrideDays is not null)
            .WithMessage("Configuration.expiryOverrideDays must be greater than 0 (omit it to use the wallet's expiry).");
        RuleFor(x => x.Configuration!.TestMode).Equal(false)
            .When(x => x.Configuration is not null)
            .WithMessage(RetiredRuleMessages.TestMode);
        RuleFor(x => x.Configuration!.Rounding).Must(r => r is null or "down" or "nearest" or "up")
            .When(x => x.Configuration is not null)
            .WithMessage("Configuration.rounding must be 'down', 'nearest', or 'up' (or omitted to inherit).");
    }
}

// Rule limits — previously unvalidated, so a negative cap, a 0, a typo'd period or a per-period
// cap with no period (silently treated as "Day" by PeriodWindow) all saved. Mirrors
// web/src/app/features/programs/rules/rule-limits.ts exactly; keep the two in step.
public sealed class RuleLimitsValidator : AbstractValidator<RuleLimits>
{
    private const string Positive = "must be greater than 0 (omit it for no limit).";

    public RuleLimitsValidator()
    {
        // Points / amounts: > 0, up to 4 decimal places (ledger numeric(20,4)).
        Amount(x => x.PerCustomerTotal, "per_customer_total");
        Amount(x => x.PerCustomerPerDay, "per_customer_per_day");
        Amount(x => x.PerCustomerPerPeriod, "per_customer_per_period");
        Amount(x => x.MaxPerEvent, "max_per_event");
        Amount(x => x.MinEventAmount, "min_event_amount");
        Amount(x => x.RuleBudgetTotal, "rule_budget_total");
        Amount(x => x.RuleBudgetPerPeriod, "rule_budget_per_period");

        RuleFor(x => x.CooldownHours).GreaterThan(0).When(x => x.CooldownHours is not null)
            .WithMessage($"Limits.cooldown_hours {Positive}");
        RuleFor(x => x.CooldownHours).Must(v => v!.Value.Scale <= 2).When(x => x.CooldownHours is not null)
            .WithMessage("Limits.cooldown_hours allows at most 2 decimal places.");
        RuleFor(x => x.MaxCustomers).GreaterThanOrEqualTo(1).When(x => x.MaxCustomers is not null)
            .WithMessage("Limits.max_customers must be at least 1 (omit it for no limit).");

        RuleFor(x => x.Period).Must(p => p is "Day" or "Week" or "Month" or "Year").When(x => x.Period is not null)
            .WithMessage("Limits.period must be 'Day', 'Week', 'Month' or 'Year'.");
        RuleFor(x => x.ResetWindow).Must(w => w is "Calendar" or "Rolling").When(x => x.ResetWindow is not null)
            .WithMessage("Limits.reset_window must be 'Calendar' or 'Rolling'.");
        RuleFor(x => x.OnBreach).Must(b => b is "Clamp" or "Skip")
            .WithMessage("Limits.on_breach must be 'Clamp' or 'Skip'.");

        // A per-period cap needs its period stated, not the engine's silent "Day" default.
        RuleFor(x => x.Period).NotNull()
            .When(x => x.PerCustomerPerPeriod is not null || x.RuleBudgetPerPeriod is not null)
            .WithMessage("Limits.period is required when per_customer_per_period or rule_budget_per_period is set.");

        // A smaller window can never allow more than the lifetime cap above it.
        RuleFor(x => x).Must(l => l.PerCustomerPerDay <= l.PerCustomerTotal)
            .When(l => l.PerCustomerPerDay is not null && l.PerCustomerTotal is not null)
            .WithMessage("Limits.per_customer_per_day can't be more than per_customer_total.");
        RuleFor(x => x).Must(l => l.PerCustomerPerPeriod <= l.PerCustomerTotal)
            .When(l => l.PerCustomerPerPeriod is not null && l.PerCustomerTotal is not null)
            .WithMessage("Limits.per_customer_per_period can't be more than per_customer_total.");
        RuleFor(x => x).Must(l => l.RuleBudgetPerPeriod <= l.RuleBudgetTotal)
            .When(l => l.RuleBudgetPerPeriod is not null && l.RuleBudgetTotal is not null)
            .WithMessage("Limits.rule_budget_per_period can't be more than rule_budget_total.");
    }

    private void Amount(System.Linq.Expressions.Expression<Func<RuleLimits, decimal?>> field, string name)
    {
        RuleFor(field).GreaterThan(0).WithMessage($"Limits.{name} {Positive}");
        RuleFor(field).Must(v => v is null || v.Value.Scale <= 4)
            .WithMessage($"Limits.{name} allows at most 4 decimal places.");
    }
}

public sealed class UpdateRuleRequestValidator : AbstractValidator<UpdateRuleRequest>
{
    public UpdateRuleRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(255).When(x => x.Name is not null);
        RuleFor(x => x.Trigger).NotEmpty().MaximumLength(100).When(x => x.Trigger is not null);
        RuleFor(x => x.Trigger).Must(t => t is null || !EventTypes.IsRetiredTrigger(t))
            .WithMessage(x => RetiredRuleMessages.RetiredTrigger(x.Trigger));
        RuleFor(x => x.Priority).GreaterThanOrEqualTo(0).When(x => x.Priority is not null);
        RuleFor(x => x.Limits!).SetValidator(new RuleLimitsValidator()).When(x => x.Limits is not null);
        // 1.3.CL item 5 — see CreateRuleRequestValidator.
        RuleFor(x => x.ExclusivityGroup).Null().WithMessage(RetiredStackingMessages.ExclusivityGroup);
        RuleFor(x => x.StackMode).Must(m => m is null or RuleStackMode.Additive)
            .WithMessage(RetiredStackingMessages.StackMode);
        RuleFor(x => x.Configuration!.HoldDays).NotNull().GreaterThan(0)
            .When(x => x.Configuration?.Posting == "Delayed")
            .WithMessage("Configuration.holdDays is required and must be positive when Posting is 'Delayed'.");
        RuleFor(x => x.Configuration!.Posting).Must(p => p is "Immediate" or "Delayed")
            .When(x => x.Configuration is not null)
            .WithMessage("Configuration.posting must be 'Immediate' or 'Delayed' ('Pending' is not yet implemented).");
        // CR 2026-10-06 Phase 5 (E1): any whole number of days above 0 — longer or shorter than
        // the wallet's own expiry.
        RuleFor(x => x.Configuration!.ExpiryOverrideDays).GreaterThan(0)
            .When(x => x.Configuration?.ExpiryOverrideDays is not null)
            .WithMessage("Configuration.expiryOverrideDays must be greater than 0 (omit it to use the wallet's expiry).");
        RuleFor(x => x.Configuration!.TestMode).Equal(false)
            .When(x => x.Configuration is not null)
            .WithMessage(RetiredRuleMessages.TestMode);
    }
}

public sealed class SetRuleStatusRequestValidator : AbstractValidator<SetRuleStatusRequest>
{
    public SetRuleStatusRequestValidator()
    {
        RuleFor(x => x.Status).Must(s => s is RuleStatus.Active or RuleStatus.Disabled)
            .WithMessage("Status must be 'active' or 'disabled'.");
    }
}
