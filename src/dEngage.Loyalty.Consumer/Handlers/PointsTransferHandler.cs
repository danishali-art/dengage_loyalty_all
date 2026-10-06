using System.Globalization;
using System.Text.Json;
using dEngage.Loyalty.Engine.Framework.Consumers;
using dEngage.Loyalty.Ledger;
using dEngage.Loyalty.RuleEngine.Models;
using dEngage.Loyalty.RuleEngine.Processing;
using dEngage.Loyalty.Schema;
using dEngage.Loyalty.Shared;
using dEngage.Loyalty.Shared.Events;
using Microsoft.EntityFrameworkCore;

namespace dEngage.Loyalty.Consumer.Handlers;

// CR 2026-10-05 item 1: a transfer always runs on a TransferRule (daily transfer limit,
// `maxPerDay`). The POINTS wallet's own `transfer.daily_limit` only pre-fills new rules in the
// portal and is never read here. Item 5: refused while the program isn't live.
public class PointsTransferHandler(
    ILedgerService ledgerService,
    IOutboxService outbox,
    LoyaltyDbContext db,
    ITenantSlugResolver tenantSlugResolver,
    IBurnRuleResolver burnRules) : IEventHandler
{
    public string? EventType => EventTypes.PointsTransfer;

    public async Task HandleAsync(EventEnvelope envelope, CancellationToken ct)
    {
        var data = envelope.Data;
        var contactKey = data.GetProperty("contact_key").GetString()!;
        var targetContactKey = data.GetProperty("target_contact_key").GetString()!;
        var pointsAmount = decimal.Parse(data.GetProperty("points_amount").GetString()!, CultureInfo.InvariantCulture);
        var sourceAccountTypeId = Guid.Parse(data.GetProperty("source_account_type_id").GetString()!);

        if (pointsAmount <= 0)
            throw new InvalidOperationException($"invalid_amount: {pointsAmount}");

        if (contactKey == targetContactKey)
            throw new InvalidOperationException("self_transfer_not_allowed");

        var tenantGuid = await tenantSlugResolver.ResolveAsync(envelope.Tenant, ct);
        var accountType = await db.AccountTypes
            .AsNoTracking()
            .FirstOrDefaultAsync(x =>
                x.TenantId == tenantGuid &&
                x.Id == sourceAccountTypeId &&
                x.Type == "POINTS", ct)
            ?? throw new InvalidOperationException("points_account_type_not_found");

        await using var tx = await db.Database.BeginTransactionAsync(ct);

        // Redelivery: if the transfer was already written (crash after commit before the
        // inbox update), do not move points or emit events again. Must come BEFORE every other
        // check — a program paused since, or a balance already debited on the first try, would
        // otherwise yield a wrong transfer_failed on the second.
        var outIdempotencyKey = $"{envelope.EventId}:transfer_out";
        var transferExists = await db.LedgerEntries.AnyAsync(x =>
            x.TenantId == envelope.Tenant &&
            x.IdempotencyKey == outIdempotencyKey, ct);
        if (transferExists) return;

        // Program and rule checks come before the account locks, so a refused transfer never
        // creates account rows for a customer who has none.
        if (!await db.IsProgramLiveAsync(tenantGuid, accountType.ProgramId, ct))
        {
            await FailAsync(envelope, contactKey, targetContactKey, pointsAmount, OutcomeReasons.ProgramNotLive,
                await ReadBalanceAsync(tenantGuid, contactKey, sourceAccountTypeId, ct), rule: null, ct);
            await tx.CommitAsync(ct);
            return;
        }

        var evt = EvaluationEvent.FromEnvelope(envelope);
        var resolution = await burnRules.ResolveAsync(
            envelope.Tenant, accountType.ProgramId, RuleTypes.TransferRule, sourceAccountTypeId, envelope.EventId, evt, ct);
        if (resolution.Rule is not { } rule)
        {
            await FailAsync(envelope, contactKey, targetContactKey, pointsAmount, resolution.FailureReason!,
                await ReadBalanceAsync(tenantGuid, contactKey, sourceAccountTypeId, ct), rule: null, ct);
            await tx.CommitAsync(ct);
            return;
        }

        // RulesValidators require it on every saved version, and the deploy migration disabled
        // the rules saved before that — so a gap here is a configuration error.
        if (rule.Calculation.MaxPerDay is not > 0)
            throw new InvalidOperationException($"transfer_rule_incomplete: {rule.Id}");
        var dailyLimit = rule.Calculation.MaxPerDay.Value;

        // Lock both accounts in deterministic order so concurrent A→B and B→A
        // transfers cannot deadlock if consumers ever run in parallel.
        var lockFirst = string.CompareOrdinal(contactKey, targetContactKey) <= 0 ? contactKey : targetContactKey;
        var lockSecond = lockFirst == contactKey ? targetContactKey : contactKey;

        var firstAccount = await ledgerService.LockAccountAsync(envelope.Tenant, lockFirst, sourceAccountTypeId, ct)
            ?? await ledgerService.UpsertAccountAsync(envelope.Tenant, lockFirst, sourceAccountTypeId, ct);
        var secondAccount = await ledgerService.LockAccountAsync(envelope.Tenant, lockSecond, sourceAccountTypeId, ct)
            ?? await ledgerService.UpsertAccountAsync(envelope.Tenant, lockSecond, sourceAccountTypeId, ct);

        var senderAccount = lockFirst == contactKey ? firstAccount : secondAccount;
        var receiverAccount = lockFirst == contactKey ? secondAccount : firstAccount;

        // Sender is row-locked, so today's sum cannot change concurrently. R-O12: the winning
        // rule's limit decides; a lower-priority rule is never tried.
        var todayUtc = DateTime.UtcNow.Date;
        var transferredToday = await db.LedgerEntries
            .Where(x =>
                x.TenantId == envelope.Tenant &&
                x.CustomerAccountId == senderAccount.Id &&
                x.Reason == LedgerReason.TransferOut &&
                x.CreatedAt >= todayUtc)
            .SumAsync(x => -x.Delta, ct);

        if (transferredToday + pointsAmount > dailyLimit)
        {
            await FailAsync(envelope, contactKey, targetContactKey, pointsAmount,
                "daily_limit_exceeded", senderAccount.Balance, rule, ct);
            await tx.CommitAsync(ct);
            return;
        }

        if (senderAccount.Balance < pointsAmount)
        {
            await FailAsync(envelope, contactKey, targetContactKey, pointsAmount,
                "insufficient_balance", senderAccount.Balance, rule, ct);
            await tx.CommitAsync(ct);
            return;
        }

        if (!await burnRules.TryReserveBudgetAsync(envelope.Tenant, rule, pointsAmount, ct))
        {
            await FailAsync(envelope, contactKey, targetContactKey, pointsAmount,
                OutcomeReasons.RuleLimitReached, senderAccount.Balance, rule, ct);
            await tx.CommitAsync(ct);
            return;
        }

        // Only the sender's debit carries rule_id: rule budgets, cooldown and max_customers
        // count ledger rows by rule, and the receiver hasn't used the rule.
        var debit = await ledgerService.AddEntryAsync(
            tenantId: envelope.Tenant,
            customerAccountId: senderAccount.Id,
            contactKey: contactKey,
            delta: -pointsAmount,
            reason: LedgerReason.TransferOut,
            sourceEventId: envelope.EventId,
            idempotencyKey: outIdempotencyKey,
            ruleId: rule.Id,
            metadata: JsonSerializer.Serialize(new { transfer_to = targetContactKey, rule_id = rule.Id, rule_version = rule.Version }),
            ct: ct);

        await ledgerService.AddEntryAsync(
            tenantId: envelope.Tenant,
            customerAccountId: receiverAccount.Id,
            contactKey: targetContactKey,
            delta: pointsAmount,
            reason: LedgerReason.TransferIn,
            sourceEventId: envelope.EventId,
            idempotencyKey: $"{envelope.EventId}:transfer_in",
            metadata: JsonSerializer.Serialize(new { transfer_from = contactKey, rule_id = rule.Id, rule_version = rule.Version }),
            ct: ct);

        await burnRules.RecordFireAsync(envelope.Tenant, rule, envelope.EventId, contactKey, -pointsAmount, debit.Id, ct);

        await outbox.Enqueue(
            envelope.Tenant,
            OutboundEventTypes.PointsTransferred,
            contactKey,
            new
            {
                contact_key = contactKey,
                target_contact_key = targetContactKey,
                points_amount = pointsAmount.ToString("F2", CultureInfo.InvariantCulture),
                sender_balance = (senderAccount.Balance - pointsAmount).ToString("F2", CultureInfo.InvariantCulture),
                receiver_balance = (receiverAccount.Balance + pointsAmount).ToString("F2", CultureInfo.InvariantCulture),
                rule_id = rule.Id.ToString(),
                source_event_id = envelope.EventId
            },
            dedupKey: $"transfer:{envelope.EventId}");
        await db.SaveChangesAsync(ct);

        await tx.CommitAsync(ct);
    }

    // For the failure payload only, without the row lock — and without creating an account
    // row for a customer who has none.
    private async Task<decimal> ReadBalanceAsync(Guid tenantGuid, string contactKey, Guid accountTypeId, CancellationToken ct) =>
        await db.CustomerAccounts
            .AsNoTracking()
            .Where(x => x.TenantId == tenantGuid && x.ContactKey == contactKey && x.AccountTypeId == accountTypeId)
            .Select(x => (decimal?)x.Balance)
            .FirstOrDefaultAsync(ct) ?? 0m;

    // A business outcome, not a processing error: a retry won't change the limit or the
    // balance. Write the transfer_failed event and commit → the inbox becomes 'processed'.
    private async Task FailAsync(
        EventEnvelope envelope,
        string contactKey,
        string targetContactKey,
        decimal pointsAmount,
        string reason,
        decimal senderBalance,
        CachedRule? rule,
        CancellationToken ct)
    {
        var failDedupKey = $"transfer_failed:{envelope.EventId}";
        var tenantGuid = await tenantSlugResolver.ResolveAsync(envelope.Tenant, ct);
        var alreadyEnqueued = await db.OutboxEvents.AnyAsync(x =>
            x.TenantId == tenantGuid && x.DedupKey == failDedupKey, ct);
        if (alreadyEnqueued) return;

        await outbox.Enqueue(
            envelope.Tenant,
            OutboundEventTypes.PointsTransferFailed,
            contactKey,
            new
            {
                contact_key = contactKey,
                target_contact_key = targetContactKey,
                points_amount = pointsAmount.ToString("F2", CultureInfo.InvariantCulture),
                reason,
                balance = senderBalance.ToString("F2", CultureInfo.InvariantCulture),
                rule_id = rule?.Id.ToString(),
                source_event_id = envelope.EventId
            },
            dedupKey: failDedupKey);
        await db.SaveChangesAsync(ct);
    }
}
