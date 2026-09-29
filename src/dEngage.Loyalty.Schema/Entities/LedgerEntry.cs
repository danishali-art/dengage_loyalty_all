namespace dEngage.Loyalty.Schema.Entities;

public class LedgerEntry
{
    public Guid Id { get; set; }
    public string TenantId { get; set; } = default!;
    public Guid CustomerAccountId { get; set; }
    public string ContactKey { get; set; } = default!;
    public decimal Delta { get; set; }
    public string Reason { get; set; } = default!;
    public string SourceEventId { get; set; } = default!;
    public Guid? RuleId { get; set; }
    public string IdempotencyKey { get; set; } = default!;
    public string? Metadata { get; set; }
    public DateTime CreatedAt { get; set; }

    public CustomerAccount CustomerAccount { get; set; } = default!;
    public Rule? Rule { get; set; }
}
