using dEngage.Loyalty.Shared;

namespace dEngage.Loyalty.Schema.Entities;

public class Rule : ITenantScopedEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid ProgramId { get; set; }
    public string Name { get; set; } = default!;
    public string Type { get; set; } = default!;
    public string Trigger { get; set; } = default!;
    public string? Conditions { get; set; }
    public string Calculation { get; set; } = default!;
    // CR-08 (A8): rounding/posting/hold/expiry-override/reversible/testMode/notifyOnAward.
    // Nullable — a null Configuration means every setting is at its A8 default.
    public string? Configuration { get; set; }

    // CR-09 (A10 guarantee #7): bumped on every RulesAppService/CardBucketsAppService
    // UpdateAsync call — see RuleVersioningService. Starts at 1 on creation.
    public int CurrentVersion { get; set; } = 1;
    // Nullable only for ReversalRule (CR-02): its target account is inherited from the
    // original posting at fire time, not configured on the rule — see
    // RuleEngine.Processing.ReversalRuleProcessor. Every other rule type requires a value;
    // enforced by RulesValidators, not the database.
    public Guid? TargetAccountTypeId { get; set; }
    public string? Limits { get; set; }
    public int Priority { get; set; }
    public bool Stackable { get; set; }

    // CR-06: required when Stackable is false (at most one winner per group per event);
    // always null when Stackable is true (A6: "Stackable rules serialise exclusivityGroup:
    // null" — a stackable rule never competes for a winner slot).
    public string? ExclusivityGroup { get; set; }

    // CR-06: RuleStackMode.Additive | Multiplier — meaningless unless Stackable is true.
    public string StackMode { get; set; } = RuleStackMode.Additive;

    public DateTime? ActiveFrom { get; set; }
    public DateTime? ActiveTo { get; set; }
    public string Status { get; set; } = RuleStatus.Active;
    // Set by a purpose-built creation UI (e.g. "card_bucket") to tag which rows it owns for its
    // own list/edit views, distinct from rules created through the generic Rules screen. Null for
    // everything else — never read by the engine itself.
    public string? Template { get; set; }

    // CR-04: the creating/approving admin's AuthenticatedPrincipal.SubjectId. Only meaningful
    // for CASH-targeted rules (RuleStatus.PendingApproval lifecycle) — null for everything else.
    public string? CreatedBy { get; set; }
    public string? ApprovedBy { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public Program Program { get; set; } = default!;
    public AccountType? TargetAccountType { get; set; }
}
