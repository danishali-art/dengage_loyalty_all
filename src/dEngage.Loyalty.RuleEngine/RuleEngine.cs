using dEngage.Loyalty.RuleEngine.Campaigns;
using dEngage.Loyalty.RuleEngine.Metadata;
using dEngage.Loyalty.RuleEngine.Models;
using dEngage.Loyalty.RuleEngine.Processing;
using dEngage.Loyalty.Shared;
using dEngage.Loyalty.Shared.Events;
using Microsoft.Extensions.Logging;

namespace dEngage.Loyalty.RuleEngine;

public class RuleEngine(
    IRuleMatcher ruleMatcher,
    ITierContextLoader tierContextLoader,
    IWinnerSelector winnerSelector,
    ILedgerPoster ledgerPoster,
    ILimitCounterSync limitCounterSync,
    ITierEvaluationService tierEval,
    ICampaignModuleRegistry campaignModules,
    IReversalRuleProcessor reversalRuleProcessor,
    ILogger<RuleEngine> logger) : IRuleEngine
{
    public async Task ProcessEventAsync(
        string tenantId,
        Guid programId,
        string eventId,
        EvaluationEvent evt,
        CancellationToken ct = default)
    {
        var matched = await ruleMatcher.MatchAsync(tenantId, programId, evt, ct);

        // Most event types have no rules — bail before the program/tier/event_log queries
        if (matched.IsEmpty) return;

        var context = await tierContextLoader.LoadAsync(
            tenantId, programId, eventId, evt,
            matched.EarnRules.Select(r => r.Conditions),
            matched.CampaignConfigs.Select(c => c.Conditions),
            ct);

        // CR-02: ReversalRule bypasses WinnerSelector entirely (inherited target account — see
        // WinnerSelector's class remarks). A tenant-approved generic event type could match one,
        // so this dispatch is real, not dead code. CR 2026-10-05: TransferRule and RedemptionRule
        // are no longer dispatched here at all — their event handlers apply them through
        // IBurnRuleResolver, and posting here as well would move the points twice.
        // CR 2026-10-06 D22: a Reversal rule saved before order.refunded stopped accepting one is
        // disabled by migration; this guard keeps a not-yet-disabled one (cache, rollout order)
        // from reversing an order the built-in refund already reversed.
        var trigger = EventTypes.Describe(evt.EventType);
        var reversalRules = matched.EarnRules
            .Where(r => r.Type == RuleTypes.ReversalRule && RuleTypeCatalog.IsCompatible(trigger, r.Type))
            .ToList();
        if (reversalRules.Count > 0)
            await reversalRuleProcessor.ProcessAsync(tenantId, eventId, reversalRules, evt, context.Condition, ct);

        // CR 2026-10-06 R15: a redelivery of an event whose rules already posted selects nothing —
        // re-selecting would reserve budgets and count limits again, and could pay another
        // exclusive rule. Campaigns (own idempotency) and the tier check still run: the first
        // delivery may have failed in either.
        var alreadyPosted = await ledgerPoster.HasPostedAsync(tenantId, eventId, evt,
            matched.EarnRules.Where(r => r.Type != RuleTypes.ReversalRule), ct);
        if (alreadyPosted)
            logger.LogInformation("RuleEngine: [{Tenant}] event {EventId} already posted (redelivery) — rules not re-applied", tenantId, eventId);

        IReadOnlyList<AppliedRule> appliedRules = alreadyPosted
            ? []
            : await winnerSelector.SelectAsync(tenantId, matched.EarnRules, evt, context.Condition, ct,
                context.Program?.DefaultRounding);

        // Campaigns (e.g. streak) run in their own per-config transaction, owned by the
        // module itself (idempotency anchored on the module's own state, not this event's
        // earn flow) — they never compete with earn rules for a winner slot.
        var campaignEarned = false;
        foreach (var config in matched.CampaignConfigs)
        {
            if (!ConditionEvaluator.Evaluate(config.Conditions, evt, context.Condition))
            {
                logger.LogInformation("RuleEngine: [{Tenant}] campaign SKIP  [{Name}] condition not met", tenantId, config.Name);
                continue;
            }
            var outcome = await campaignModules.Resolve(config.CampaignType)
                .EvaluateAsync(new CampaignEvaluationRequest(tenantId, eventId, config, evt), ct);
            if (outcome.Earned)
                campaignEarned = true;
        }

        if (appliedRules.Count == 0)
        {
            // A campaign completion bonus still counts toward tier qualification; on a redelivery
            // the first delivery's tier check may be the step that failed (R15).
            if ((campaignEarned || alreadyPosted) && context.QualifyingAccountTypeId is not null)
                await tierEval.EvaluateAsync(tenantId, evt.ContactKey, programId,
                    context.QualifyingAccountTypeId.Value, eventId, ct);
            return;
        }

        await ledgerPoster.PostAsync(tenantId, eventId, evt, appliedRules, ct);

        await limitCounterSync.SyncAsync(tenantId, evt.ContactKey, appliedRules);

        // Re-evaluate tier — a failure does not roll back the earning
        if (context.QualifyingAccountTypeId is not null)
            await tierEval.EvaluateAsync(tenantId, evt.ContactKey, programId,
                context.QualifyingAccountTypeId.Value, eventId, ct);
    }
}
