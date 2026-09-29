using dEngage.Loyalty.RuleEngine.Models;

namespace dEngage.Loyalty.RuleEngine.Processing;

// Named replacement for the (CachedRule, decimal, Guid) tuple RuleEngine used to carry
// between winner selection and ledger posting.
// ResolutionSnapshot: CR-06/A10 guarantee #9 — serialised WinnerSelector.ResolutionSnapshot
// for this posting (null for Delta paths that don't go through stacking resolution).
public sealed record AppliedRule(CachedRule Rule, decimal Delta, Guid AccountTypeId, string? ResolutionSnapshot = null);
