using System.Text.Json;
using dEngage.Loyalty.Ledger;
using dEngage.Loyalty.RuleEngine.Calculation;
using dEngage.Loyalty.RuleEngine.Models;
using dEngage.Loyalty.Schema;
using dEngage.Loyalty.Shared;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace dEngage.Loyalty.RuleEngine.Processing;

public sealed class LedgerPoster(
    LoyaltyDbContext db,
    ILedgerService ledger,
    IOutboxService outbox,
    IStampCompletionHandler stampCompletion,
    IRuleFireAuditWriter auditWriter,
    IBudgetReservationService budgetReservation,
    ITenantSlugResolver tenantSlugResolver,
    ILogger<LedgerPoster> logger) : ILedgerPoster
{
    public async Task PostAsync(
        string tenantId,
        string eventId,
        EvaluationEvent evt,
        IReadOnlyList<AppliedRule> appliedRules,
        CancellationToken ct)
    {
        // Single DB transaction — if commit fails, the await using dispose rolls back.
        // Redis increments and tier evaluation are OUTSIDE the transaction block: an
        // exception thrown after commit must not lead to a rollback attempt on a committed tx.
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var tenantGuid = await tenantSlugResolver.ResolveAsync(tenantId, ct);

        // Account upsert deferred to this point — no empty account row is created
        // for an account type that had no rules applied.
        var accountIds = new Dictionary<Guid, Guid>();
        foreach (var accountTypeId in appliedRules.Select(a => a.AccountTypeId).Distinct())
        {
            var account = await ledger.UpsertAccountAsync(tenantId, evt.ContactKey, accountTypeId, ct);
            accountIds[accountTypeId] = account.Id;
        }

        foreach (var (rule, originalDelta, accountTypeId, resolutionSnapshot) in appliedRules)
        {
            var accountId = accountIds[accountTypeId];
            var isStamp = rule.Type == RuleTypes.StampRule;
            var reason = ReasonFor(rule.Type);
            var idempotencyKey = $"{eventId}:{rule.Id}";
            // CR-10 (A11): "profile.* condition values arrive in the event payload and are
            // persisted with the posting as the value at award time" — snapshot the whole
            // payload `profile` object (if the caller sent one), not just whichever fields this
            // rule's conditions happened to reference, so the posting stays self-explanatory
            // even if the rule is edited later.
            var profileSnapshot = evt.Data.ValueKind == JsonValueKind.Object &&
                evt.Data.TryGetProperty("profile", out var profileEl) && profileEl.ValueKind == JsonValueKind.Object
                ? profileEl
                : (JsonElement?)null;
            var metadata = JsonSerializer.Serialize(new
            {
                order_amount = evt.Amount.ToString("F2", System.Globalization.CultureInfo.InvariantCulture),
                channel = evt.Channel,
                profile = profileSnapshot
            });

            // CR-08: test mode still evaluates and audits, but posts nothing — no ledger entry,
            // no balance change, no stamp completion. Delayed posting evaluates and holds — the
            // calculation is fixed now, but the actual ledger entry is deferred to
            // DelayedPostingPromotionJob once Configuration.holdDays elapses.
            var testMode = rule.Configuration?.TestMode == true;
            var delayed = !testMode && rule.Configuration?.Posting == "Delayed";

            // CR-09 (A10 guarantee #4): reserve, inside this transaction, before posting or
            // holding — closes the TOCTOU race the CR-07 pre-check (WinnerSelector, before this
            // transaction opened) still has for concurrent events on the same rule. Test mode
            // reserves nothing (nothing is posted). Skipped when the rule ends up with no
            // effective delta after reservation — see below.
            var delta = originalDelta;
            var budgetSkipped = false;
            if (!testMode && rule.Limits is { } limits && (limits.RuleBudgetTotal.HasValue || limits.RuleBudgetPerPeriod.HasValue))
                (delta, budgetSkipped) = await ReserveBudgetAsync(tenantId, rule, limits, delta, ct);

            if (budgetSkipped)
            {
                logger.LogInformation("RuleEngine: [{Tenant}] rule SKIP  [{Rule}] rule budget exhausted at reservation time", tenantId, rule.Name);
                continue;
            }

            logger.LogInformation("RuleEngine: [{Tenant}] {Verb} [{Rule}] contact={Contact} delta={Delta} reason={Reason}",
                tenantId, testMode ? "TESTMODE" : delayed ? "HOLD" : "APPLY", rule.Name, evt.ContactKey, delta, reason);

            Guid? ledgerEntryId = null;
            if (delayed)
            {
                var alreadyHeld = await db.HeldPostings.AnyAsync(x => x.TenantId == tenantGuid && x.IdempotencyKey == idempotencyKey, ct);
                if (!alreadyHeld)
                {
                    db.HeldPostings.Add(new Schema.Entities.HeldPosting
                    {
                        Id = UUIDNext.Uuid.NewDatabaseFriendly(UUIDNext.Database.PostgreSql),
                        TenantId = tenantGuid,
                        RuleId = rule.Id,
                        CustomerAccountId = accountId,
                        ContactKey = evt.ContactKey,
                        Delta = delta,
                        Reason = reason,
                        SourceEventId = eventId,
                        IdempotencyKey = idempotencyKey,
                        Metadata = metadata,
                        HoldUntil = DateTime.UtcNow.AddDays(rule.Configuration!.HoldDays!.Value),
                        CreatedAt = DateTime.UtcNow
                    });
                }
            }
            else if (!testMode)
            {
                var entry = await ledger.AddEntryAsync(
                    tenantId, accountId, evt.ContactKey,
                    delta, reason, eventId, idempotencyKey,
                    rule.Id, metadata, ct);
                ledgerEntryId = entry.Id;

                if (isStamp)
                    await stampCompletion.ProcessAsync(tenantId, accountId, evt.ContactKey, rule, eventId, ct);
            }

            await auditWriter.RecordAsync(
                tenantId, rule.Id, rule.Version, eventId, evt.ContactKey,
                rule.Conditions, rule.Calculation, delta, ledgerEntryId, resolutionSnapshot, ct);

            // CR-08: notifyOnAward — a distinct per-rule event alongside (not instead of) the
            // per-event PointsEarned summary below. Suppressed in test mode and while held
            // (nothing was posted yet — the promotion job's own concerns, not fired here).
            if (!testMode && !delayed && rule.Configuration?.NotifyOnAward == true)
            {
                await outbox.Enqueue(
                    tenantId,
                    dEngage.Loyalty.Shared.Events.OutboundEventTypes.RuleAwarded,
                    evt.ContactKey,
                    new
                    {
                        contact_key = evt.ContactKey,
                        rule_id = rule.Id.ToString(),
                        rule_name = rule.Name,
                        delta = delta.ToString("F2", System.Globalization.CultureInfo.InvariantCulture),
                        source_event_id = eventId
                    },
                    dedupKey: $"rule_awarded:{eventId}:{rule.Id}");
            }
        }

        // Unconditional flush of the audit rows added above — the points.earned block below
        // only saves when it actually enqueues (skipped on redelivery), so audit persistence
        // can't be left depending on that branch.
        await db.SaveChangesAsync(ct);

        // points.earned — commits in the same tx as the ledger writes.
        // On redelivery, entries come back as existing via idempotency; re-inserting the
        // outbox row would crash the whole tx with a ux_outbox_dedup violation — check existence first.
        var pointsEarnedDedup = $"points_earned:{eventId}";
        var alreadyEnqueued = await db.OutboxEvents.AnyAsync(
            x => x.TenantId == tenantGuid && x.DedupKey == pointsEarnedDedup, ct);

        // CR-08: test-mode and delayed rules posted nothing (yet) — excluded from this summary
        // so its "delta" never overstates what the account's fresh-read balance actually shows.
        var postedRules = appliedRules
            .Where(a => a.Rule.Configuration?.TestMode != true && a.Rule.Configuration?.Posting != "Delayed")
            .ToList();

        if (!alreadyEnqueued && postedRules.Count > 0)
        {
            var deltaByAccountType = postedRules
                .GroupBy(a => a.AccountTypeId)
                .ToDictionary(g => g.Key, g => g.Sum(x => x.Delta));

            // Read balances fresh — AddEntryAsync updates the balance via ExecuteUpdate,
            // so tracked instances carry the stale value.
            var ids = deltaByAccountType.Keys.ToList();
            var accountRows = await db.CustomerAccounts
                .AsNoTracking()
                .Include(x => x.AccountType)
                .Where(x => ids.Contains(x.Id))
                .ToListAsync(ct);

            await outbox.Enqueue(
                tenantId,
                dEngage.Loyalty.Shared.Events.OutboundEventTypes.PointsEarned,
                evt.ContactKey,
                new
                {
                    contact_key = evt.ContactKey,
                    source_event_id = eventId,
                    accounts = accountRows
                        .OrderBy(a => a.AccountType.Name)
                        .Select(a => new
                        {
                            account_type = a.AccountType.Type,
                            code = a.AccountType.Name,
                            delta = deltaByAccountType[a.AccountTypeId]
                                .ToString("F2", System.Globalization.CultureInfo.InvariantCulture),
                            balance = a.Balance
                                .ToString("F2", System.Globalization.CultureInfo.InvariantCulture)
                        })
                        .ToArray(),
                    applied_rules = postedRules
                        .Select(a => new
                        {
                            rule_id = a.Rule.Id.ToString(),
                            name = a.Rule.Name,
                            delta = a.Delta
                                .ToString("F2", System.Globalization.CultureInfo.InvariantCulture)
                        })
                        .ToArray()
                },
                dedupKey: pointsEarnedDedup);

            await db.SaveChangesAsync(ct);
        }

        await tx.CommitAsync(ct);
    }

    // CR-09 (A10 guarantee #4): the authoritative, race-safe budget check — runs inside this
    // transaction, after WinnerSelector's own (optimistic, pre-transaction) check already
    // filtered out the obviously-exhausted cases. Rolling-reset RuleBudgetPerPeriod is not
    // lockable (no discrete bucket to hold a persistent counter against — see
    // PeriodWindow.CalendarKey remarks) and keeps relying on WinnerSelector's ledger-query
    // clamp alone for that specific configuration.
    private async Task<(decimal Delta, bool Skipped)> ReserveBudgetAsync(
        string tenantId, CachedRule rule, RuleLimits limits, decimal delta, CancellationToken ct)
    {
        var skip = limits.OnBreach == "Skip";

        if (limits.RuleBudgetTotal is { } total)
        {
            var used = await budgetReservation.LockAndGetUsageAsync(tenantId, rule.Id, BudgetReservationService.BudgetTotal, "", null, ct);
            var remaining = total - used;
            if (remaining <= 0) return (0, true);
            if (delta > remaining)
            {
                if (skip) return (0, true);
                delta = remaining;
            }
            await budgetReservation.RecordUsageAsync(tenantId, rule.Id, BudgetReservationService.BudgetTotal, "", delta, ct);
        }

        if (limits.RuleBudgetPerPeriod is { } perPeriod && limits.ResetWindow != "Rolling")
        {
            var periodKey = PeriodWindow.CalendarKey(limits.Period, DateTime.UtcNow);
            var used = await budgetReservation.LockAndGetUsageAsync(tenantId, rule.Id, BudgetReservationService.BudgetPeriod, periodKey, limits.Period, ct);
            var remaining = perPeriod - used;
            if (remaining <= 0) return (0, true);
            if (delta > remaining)
            {
                if (skip) return (0, true);
                delta = remaining;
            }
            await budgetReservation.RecordUsageAsync(tenantId, rule.Id, BudgetReservationService.BudgetPeriod, periodKey, delta, ct);
        }

        return (delta, false);
    }

    // CR-02: beyond Earn/StampEarn, the three new pipeline-compatible rule types each get
    // their own reason so the ledger/audit trail can tell an operator correction apart from a
    // customer redemption or a scheduled expiry. RedemptionRule reuses the legacy
    // PointsRedeemed reason — same semantic (points debited via redemption), one reason code
    // regardless of which path produced it.
    private static string ReasonFor(string ruleType) => ruleType switch
    {
        RuleTypes.StampRule => LedgerReason.StampEarn,
        RuleTypes.RedemptionRule => LedgerReason.PointsRedeemed,
        RuleTypes.ExpiryRule => LedgerReason.PointsExpired,
        RuleTypes.ManualAdjustmentRule => LedgerReason.PointsAdjusted,
        _ => LedgerReason.Earn
    };
}
