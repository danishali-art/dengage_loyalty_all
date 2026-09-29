using dEngage.Loyalty.Schema.Entities;

namespace dEngage.Loyalty.Ledger;

public interface ILedgerService
{
    Task<LedgerEntry> AddEntryAsync(
        string tenantId,
        Guid customerAccountId,
        string contactKey,
        decimal delta,
        string reason,
        string sourceEventId,
        string idempotencyKey,
        Guid? ruleId = null,
        string? metadata = null,
        CancellationToken ct = default);

    Task<CustomerAccount?> LockAccountAsync(
        string tenantId,
        string contactKey,
        Guid accountTypeId,
        CancellationToken ct = default);

    Task<CustomerAccount> UpsertAccountAsync(
        string tenantId,
        string contactKey,
        Guid accountTypeId,
        CancellationToken ct = default);

    Task<decimal> GetBalanceAsync(
        string tenantId,
        string contactKey,
        Guid accountTypeId,
        CancellationToken ct = default);
}
