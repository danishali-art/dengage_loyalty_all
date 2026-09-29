namespace dEngage.Loyalty.Schema.Entities;

// CR-10 (docs/scope-change-rules A11): "a minimal registration endpoint accepting customerRef +
// MM-DD only." Tenant-wide, not program-scoped — a birthday is a fact about the customer, not
// about any one program's account. Deliberately the only customer-profile-shaped write this
// system makes — see Api/Customers remarks on why the Customers module is otherwise read-only.
public class CustomerBirthday
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string ContactKey { get; set; } = default!;

    // "MM-DD", validated at the API boundary — never a full date (A11: the engine must not
    // learn the customer's birth year, only enough to fire an annual bonus).
    public string MonthDay { get; set; } = default!;

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
