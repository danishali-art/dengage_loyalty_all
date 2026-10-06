using dEngage.Loyalty.RuleEngine.Models;
using dEngage.Loyalty.Shared;

namespace dEngage.Loyalty.Api.Rules;

// CR 2026-10-05 item 1: what a redeem / transfer rule's calculation must hold now that the
// handlers apply it (the same fields as the POINTS wallet's own settings). One list, used by
// CreateRuleRequestValidator, by RulesAppService on edit (the update validator can't see the
// rule's type) and before re-enabling a rule.
internal static class BurnRuleCalculationRules
{
    public static bool RequiresCashApproval(string ruleType, RuleCalculation? calculation) =>
        ruleType == RuleTypes.RedemptionRule && calculation?.CashAccountTypeId is not null;

    public static IReadOnlyList<string> Errors(string ruleType, RuleCalculation? calculation)
    {
        var errors = new List<string>();
        if (ruleType == RuleTypes.RedemptionRule)
        {
            if (calculation?.Factor is not > 0)
                errors.Add("Calculation.rate (cash per point) is required and must be positive for RedemptionRule.");
            // R-O1: `ratio` meant "points per currency unit", the inverse of `rate`. Accepting both
            // would let a payout be off by a factor of rate².
            if (calculation?.Ratio is not null)
                errors.Add("Calculation.ratio is no longer used for RedemptionRule — use rate (cash per point).");
            if (calculation?.CashAccountTypeId is null)
                errors.Add("Calculation.cashAccountTypeId (Redeem into) is required for RedemptionRule.");
            if (calculation?.MinRedeem is < 0)
                errors.Add("Calculation.minRedeem can't be negative.");
        }
        else if (ruleType == RuleTypes.TransferRule)
        {
            if (calculation?.MaxPerDay is not > 0)
                errors.Add("Calculation.maxPerDay (daily transfer limit) is required and must be positive for TransferRule.");
        }
        return errors;
    }
}
