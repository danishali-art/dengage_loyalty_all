using dEngage.Loyalty.RuleEngine.Campaigns;
using dEngage.Loyalty.RuleEngine.Models;
using dEngage.Loyalty.RuleEngine.Processing;
using dEngage.Loyalty.Shared;
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
    ITransferRuleProcessor transferRuleProcessor,
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

        // CR-02: TransferRule/ReversalRule bypass WinnerSelector entirely (dual-entry posting /
        // inherited target account — see WinnerSelector's class remarks). Neither event type
        // reaches here from a live built-in handler yet (points.transfer/order.refunded keep
        // their unconditional legacy handling — see docs/scope-changes changelog), but a
        // tenant-approved generic event type could theoretically match one, so this dispatch
        // is real, not dead code.
        var transferRules = matched.EarnRules.Where(r => r.Type == RuleTypes.TransferRule).ToList();
        if (transferRules.Count > 0)
            await transferRuleProcessor.ProcessAsync(tenantId, eventId, transferRules, evt, context.Condition, ct);

        var reversalRules = matched.EarnRules.Where(r => r.Type == RuleTypes.ReversalRule).ToList();
        if (reversalRules.Count > 0)
            await reversalRuleProcessor.ProcessAsync(tenantId, eventId, reversalRules, evt, context.Condition, ct);

        var appliedRules = await winnerSelector.SelectAsync(tenantId, matched.EarnRules, evt, context.Condition, ct);

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
            // A campaign completion bonus still counts toward tier qualification
            if (campaignEarned && context.QualifyingAccountTypeId is not null)
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
