using dEngage.Loyalty.Schema;
using dEngage.Loyalty.Schema.Entities;
using dEngage.Loyalty.Shared;
using Microsoft.EntityFrameworkCore;

namespace dEngage.Loyalty.Ledger;

public class LedgerService(LoyaltyDbContext db, ITenantSlugResolver tenantSlugResolver) : ILedgerService
{
    public async Task<LedgerEntry> AddEntryAsync(
        string tenantId,
        Guid customerAccountId,
        string contactKey,
        decimal delta,
        string reason,
        string sourceEventId,
        string idempotencyKey,
        Guid? ruleId = null,
        string? metadata = null,
        CancellationToken ct = default)
    {
        var entry = new LedgerEntry
        {
            Id = UUIDNext.Uuid.NewDatabaseFriendly(UUIDNext.Database.PostgreSql),
            TenantId = tenantId,
            CustomerAccountId = customerAccountId,
            ContactKey = contactKey,
            Delta = delta,
            Reason = reason,
            SourceEventId = sourceEventId,
            RuleId = ruleId,
            IdempotencyKey = idempotencyKey,
            CreatedAt = DateTime.UtcNow
        };

        if (metadata is not null)
            entry.Metadata = metadata;

        // Idempotency: if the same key was written before, do not touch the balance.
        // The TenantId filter is required — the unique index is (tenant_id, idempotency_key) and
        // on the partitioned table a query without tenant scans all partitions.
        var existing = await db.LedgerEntries
            .FirstOrDefaultAsync(x => x.TenantId == tenantId && x.IdempotencyKey == idempotencyKey, ct);
        if (existing is not null)
            return existing;

        // INSERT + balance UPDATE must happen in one transaction. With separate autocommits,
        // a crash in between leaves the entry written but the balance not updated;
        // since the idempotency check cuts off the retry, the balance would stay broken permanently.
        if (db.Database.CurrentTransaction is not null)
        {
            await InsertAndApplyAsync(entry, customerAccountId, delta, ct);
        }
        else
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            await InsertAndApplyAsync(entry, customerAccountId, delta, ct);
            await tx.CommitAsync(ct);
        }

        return entry;
    }

    private async Task InsertAndApplyAsync(LedgerEntry entry, Guid customerAccountId, decimal delta, CancellationToken ct)
    {
        db.LedgerEntries.Add(entry);
        await db.SaveChangesAsync(ct);

        await db.CustomerAccounts
            .Where(x => x.Id == customerAccountId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.Balance, x => x.Balance + delta)
                .SetProperty(x => x.UpdatedAt, DateTime.UtcNow), ct);
    }

    // Row lock (SELECT ... FOR UPDATE) — so a concurrent write cannot change the balance
    // between the balance check and the deduction. Must be called inside an open transaction;
    // tracking is disabled so a fresh DB value is returned.
    public async Task<CustomerAccount?> LockAccountAsync(
        string tenantId,
        string contactKey,
        Guid accountTypeId,
        CancellationToken ct = default)
    {
        var tenantGuid = await tenantSlugResolver.ResolveAsync(tenantId, ct);
        var rows = await db.CustomerAccounts
            .FromSql($"""
                SELECT * FROM customer_accounts
                WHERE tenant_id = {tenantGuid}
                  AND contact_key = {contactKey}
                  AND account_type_id = {accountTypeId}
                FOR UPDATE
                """)
            .AsNoTracking()
            .ToListAsync(ct);

        return rows.FirstOrDefault();
    }

    public async Task<CustomerAccount> UpsertAccountAsync(
        string tenantId,
        string contactKey,
        Guid accountTypeId,
        CancellationToken ct = default)
    {
        var tenantGuid = await tenantSlugResolver.ResolveAsync(tenantId, ct);
        var account = await db.CustomerAccounts
            .FirstOrDefaultAsync(x =>
                x.TenantId == tenantGuid &&
                x.ContactKey == contactKey &&
                x.AccountTypeId == accountTypeId, ct);

        if (account is not null)
            return account;

        account = new CustomerAccount
        {
            Id = UUIDNext.Uuid.NewDatabaseFriendly(UUIDNext.Database.PostgreSql),
            TenantId = tenantGuid,
            ContactKey = contactKey,
            AccountTypeId = accountTypeId,
            Balance = 0,
            UpdatedAt = DateTime.UtcNow
        };

        db.CustomerAccounts.Add(account);
        await db.SaveChangesAsync(ct);

        return account;
    }

    public async Task<decimal> GetBalanceAsync(
        string tenantId,
        string contactKey,
        Guid accountTypeId,
        CancellationToken ct = default)
    {
        var tenantGuid = await tenantSlugResolver.ResolveAsync(tenantId, ct);
        var account = await db.CustomerAccounts
            .FirstOrDefaultAsync(x =>
                x.TenantId == tenantGuid &&
                x.ContactKey == contactKey &&
                x.AccountTypeId == accountTypeId, ct);

        return account?.Balance ?? 0;
    }
}
