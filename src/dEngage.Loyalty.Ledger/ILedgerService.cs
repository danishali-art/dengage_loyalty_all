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
        CancellationToken ct = default,
        // CR 2026-10-06 Phase 5: the earning rule's expiry override, as a date; null = the wallet's.
        DateTime? expiresAt = null);

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
