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

// CR 2026-10-05 item 1: a redeem always runs on a RedemptionRule (cash per point, optional
// minimum, Redeem into CASH wallet). The POINTS wallet's own `redemption` settings only pre-fill
// new rules in the portal and are never read here. Item 5: refused while the program isn't live.
public class PointsRedeemHandler(
    ILedgerService ledgerService,
    IOutboxService outbox,
    LoyaltyDbContext db,
    ITenantSlugResolver tenantSlugResolver,
    IBurnRuleResolver burnRules) : IEventHandler
{
    public string? EventType => EventTypes.PointsRedeem;

    public async Task HandleAsync(EventEnvelope envelope, CancellationToken ct)
    {
        var data = envelope.Data;
        var contactKey = data.GetProperty("contact_key").GetString()!;
        var pointsAmount = decimal.Parse(data.GetProperty("points_amount").GetString()!, CultureInfo.InvariantCulture);
        var sourceAccountTypeId = Guid.Parse(data.GetProperty("source_account_type_id").GetString()!);

        // Malformed input stays a hard failure (inbox failed → DLQ), as in PointsTransferHandler:
        // the API rejects it, only a hand-built event gets here.
        if (pointsAmount <= 0)
            throw new InvalidOperationException($"invalid_amount: {pointsAmount}");

        var tenantGuid = await tenantSlugResolver.ResolveAsync(envelope.Tenant, ct);
        var accountType = await db.AccountTypes
            .AsNoTracking()
            .FirstOrDefaultAsync(x =>
                x.TenantId == tenantGuid &&
                x.Id == sourceAccountTypeId &&
                x.Type == "POINTS", ct)
            ?? throw new InvalidOperationException("points_account_type_not_found");

        // Both legs (points debit + cash credit) in a single transaction; the points
        // balance is read under a row lock so it cannot change between check and debit.
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        // Redelivery: if the redeem was already written (crash after commit before the inbox
        // update), don't post or emit again. Must come BEFORE every other check — a program
        // paused since, or the balance already debited on the first try, would otherwise yield a
        // wrong redeem_failed on the second.
        var debitIdempotencyKey = $"{envelope.EventId}:points_redeemed";
        if (await db.LedgerEntries.AnyAsync(x =>
                x.TenantId == envelope.Tenant &&
                x.IdempotencyKey == debitIdempotencyKey, ct))
            return;

        // Business outcomes below (program not live, no usable rule, below minimum, short
        // balance, budget) are not processing errors — a retry won't change them. Each is an
        // explicit check, reported with redeem_failed and committed → the inbox becomes
        // 'processed'. Never decided by catching the ledger's exception.
        if (!await db.IsProgramLiveAsync(tenantGuid, accountType.ProgramId, ct))
        {
            await FailAsync(envelope, contactKey, pointsAmount, OutcomeReasons.ProgramNotLive,
                await ReadBalanceAsync(tenantGuid, contactKey, sourceAccountTypeId, ct), rule: null, ct);
            await tx.CommitAsync(ct);
            return;
        }

        var evt = EvaluationEvent.FromEnvelope(envelope);
        var resolution = await burnRules.ResolveAsync(
            envelope.Tenant, accountType.ProgramId, RuleTypes.RedemptionRule, sourceAccountTypeId, envelope.EventId, evt, ct);
        if (resolution.Rule is not { } rule)
        {
            await FailAsync(envelope, contactKey, pointsAmount, resolution.FailureReason!,
                await ReadBalanceAsync(tenantGuid, contactKey, sourceAccountTypeId, ct), rule: null, ct);
            await tx.CommitAsync(ct);
            return;
        }

        // RulesValidators require both on every saved version, and the deploy migration disabled
        // the rules saved before they existed — so a gap here is a configuration error.
        if (rule.Calculation.Factor is not > 0 || rule.Calculation.CashAccountTypeId is not { } cashAccountTypeId)
            throw new InvalidOperationException($"redemption_rule_incomplete: {rule.Id}");
        var rate = rule.Calculation.Factor.Value;

        var cashAccountType = await db.AccountTypes
            .AsNoTracking()
            .FirstOrDefaultAsync(x =>
                x.TenantId == tenantGuid &&
                x.Id == cashAccountTypeId &&
                x.ProgramId == accountType.ProgramId &&
                x.Type == "CASH", ct)
            ?? throw new InvalidOperationException($"cash_account_type_not_found: {cashAccountTypeId}");

        var pointsAccount = await ledgerService.LockAccountAsync(envelope.Tenant, contactKey, sourceAccountTypeId, ct)
            ?? await ledgerService.UpsertAccountAsync(envelope.Tenant, contactKey, sourceAccountTypeId, ct);

        // R-O12: the winning rule's own minimum decides; a lower-priority rule is never tried.
        if (rule.Calculation.MinRedeem is { } minRedeem && pointsAmount < minRedeem)
        {
            await FailAsync(envelope, contactKey, pointsAmount, "below_minimum", pointsAccount.Balance, rule, ct);
            await tx.CommitAsync(ct);
            return;
        }

        if (pointsAccount.Balance < pointsAmount)
        {
            await FailAsync(envelope, contactKey, pointsAmount, "insufficient_points", pointsAccount.Balance, rule, ct);
            await tx.CommitAsync(ct);
            return;
        }

        if (!await burnRules.TryReserveBudgetAsync(envelope.Tenant, rule, pointsAmount, ct))
        {
            await FailAsync(envelope, contactKey, pointsAmount, OutcomeReasons.RuleLimitReached, pointsAccount.Balance, rule, ct);
            await tx.CommitAsync(ct);
            return;
        }

        // Rounded once, down, to the CASH wallet's precision (2 dp was hard-coded before, wrong
        // for 3-decimal currencies) — never in two places.
        var cashDecimals = ReadDecimals(cashAccountType.Config);
        var cashAmount = Math.Round(pointsAmount * rate, cashDecimals, MidpointRounding.ToZero);
        var cashText = cashAmount.ToString($"F{cashDecimals}", CultureInfo.InvariantCulture);

        var cashAccount = await ledgerService.UpsertAccountAsync(envelope.Tenant, contactKey, cashAccountTypeId, ct);

        var metadata = JsonSerializer.Serialize(new
        {
            redeemed_points = pointsAmount,
            cash_amount = cashText,
            rate,
            rule_id = rule.Id,
            rule_version = rule.Version
        });

        // Only the points debit carries rule_id: rule budgets, cooldown and max_customers count
        // ledger rows by rule, so tagging the cash credit too would count one redeem twice.
        var debit = await ledgerService.AddEntryAsync(
            tenantId: envelope.Tenant,
            customerAccountId: pointsAccount.Id,
            contactKey: contactKey,
            delta: -pointsAmount,
            reason: LedgerReason.PointsRedeemed,
            sourceEventId: envelope.EventId,
            idempotencyKey: debitIdempotencyKey,
            ruleId: rule.Id,
            metadata: metadata,
            ct: ct);

        await ledgerService.AddEntryAsync(
            tenantId: envelope.Tenant,
            customerAccountId: cashAccount.Id,
            contactKey: contactKey,
            delta: cashAmount,
            reason: LedgerReason.PointsRedeemedCash,
            sourceEventId: envelope.EventId,
            idempotencyKey: $"{envelope.EventId}:points_redeemed_cash",
            metadata: metadata,
            ct: ct);

        await burnRules.RecordFireAsync(envelope.Tenant, rule, envelope.EventId, contactKey, -pointsAmount, debit.Id, ct);

        await outbox.Enqueue(
            envelope.Tenant,
            OutboundEventTypes.PointsRedeemed,
            contactKey,
            new
            {
                contact_key = contactKey,
                points_amount = pointsAmount.ToString("F2", CultureInfo.InvariantCulture),
                cash_amount = cashText,
                source_account_type_id = sourceAccountTypeId.ToString(),
                cash_account_type_id = cashAccountTypeId.ToString(),
                points_balance = (pointsAccount.Balance - pointsAmount).ToString("F2", CultureInfo.InvariantCulture),
                rule_id = rule.Id.ToString(),
                source_event_id = envelope.EventId
            },
            dedupKey: $"points_redeemed:{envelope.EventId}");
        await db.SaveChangesAsync(ct);

        await tx.CommitAsync(ct);
    }

    // For the failure payload only, before (or without) taking the row lock — and without
    // creating an account row for a customer who has none.
    private async Task<decimal> ReadBalanceAsync(Guid tenantGuid, string contactKey, Guid accountTypeId, CancellationToken ct) =>
        await db.CustomerAccounts
            .AsNoTracking()
            .Where(x => x.TenantId == tenantGuid && x.ContactKey == contactKey && x.AccountTypeId == accountTypeId)
            .Select(x => (decimal?)x.Balance)
            .FirstOrDefaultAsync(ct) ?? 0m;

    // CASH wallets require `decimals` (AccountTypeConfigValidators); 2 — the old fixed
    // precision — only covers a row written before that rule.
    private static int ReadDecimals(string configJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(configJson);
            return doc.RootElement.TryGetProperty("decimals", out var v) && v.TryGetInt32(out var places)
                ? Math.Clamp(places, 0, 4) : 2;
        }
        catch (JsonException)
        {
            return 2;
        }
    }

    private async Task FailAsync(
        EventEnvelope envelope,
        string contactKey,
        decimal pointsAmount,
        string reason,
        decimal balance,
        CachedRule? rule,
        CancellationToken ct)
    {
        var failDedupKey = $"redeem_failed:{envelope.EventId}";
        var tenantGuid = await tenantSlugResolver.ResolveAsync(envelope.Tenant, ct);
        var alreadyEnqueued = await db.OutboxEvents.AnyAsync(x =>
            x.TenantId == tenantGuid && x.DedupKey == failDedupKey, ct);
        if (alreadyEnqueued) return;

        await outbox.Enqueue(
            envelope.Tenant,
            OutboundEventTypes.PointsRedeemFailed,
            contactKey,
            new
            {
                contact_key = contactKey,
                points_amount = pointsAmount.ToString("F2", CultureInfo.InvariantCulture),
                reason,
                balance = balance.ToString("F2", CultureInfo.InvariantCulture),
                // The winning rule's minimum; null when no rule applied or the rule has none.
                min_points = rule?.Calculation.MinRedeem?.ToString("F2", CultureInfo.InvariantCulture),
                rule_id = rule?.Id.ToString(),
                source_event_id = envelope.EventId
            },
            dedupKey: failDedupKey);
        await db.SaveChangesAsync(ct);
    }
}
