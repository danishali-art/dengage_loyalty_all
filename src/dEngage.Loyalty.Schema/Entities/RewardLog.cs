using dEngage.Loyalty.Shared;

namespace dEngage.Loyalty.Schema.Entities;

public class RewardLog
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string ContactKey { get; set; } = default!;
    public Guid AccountTypeId { get; set; }
    public string RewardName { get; set; } = default!;
    public string SourceEventId { get; set; } = default!;
    public Guid LedgerResetEntryId { get; set; }
    public int CompletionCount { get; set; }
    public string Status { get; set; } = RewardLogStatus.Pending;
    public Guid? RewardDefinitionId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? DeliveredAt { get; set; }

    public AccountType AccountType { get; set; } = default!;
    public RewardDefinition? RewardDefinition { get; set; }
}
