using dEngage.Loyalty.Schema;
using dEngage.Loyalty.Shared;
using dEngage.Loyalty.Shared.Events;
using Microsoft.EntityFrameworkCore;

namespace dEngage.Loyalty.RuleEngine.Processing;

// CR 2026-10-06 D4/D4a/D12 (docs/scope-changes/2026-10-05-rule-config-limits-by-trigger.md §3.6):
// events the catalog marks "once per customer" (signup, kyc.completed) pay each rule at most once
// per customer, on every rule, existing or new. "Once" spans all versions of the rule (same id)
// and counts only a real award — an earn posting, or a hold not cancelled by a refund — including
// payments made before this change (no clawback, and they count as "already received"). A Per
// customer total on such a rule can never allow a second award (D4b).
internal static class OnceOnlyAward
{
    public const string SkipReason = "already_awarded_once";

    public static bool Applies(string eventType) =>
        EventTypes.Describe(eventType)?.Cardinality == EventCardinality.OncePerCustomer;

    // The posting key for these events: the unique (tenant_id, idempotency_key) index then makes
    // a second payment impossible even if two events for one customer race past the check.
    public static string IdempotencyKey(Guid ruleId, string contactKey) => $"once:{ruleId}:{contactKey}";

    public static async Task<bool> ExistsAsync(LoyaltyDbContext db, string tenantSlug, Guid ruleId, string contactKey, CancellationToken ct)
    {
        if (await db.LedgerEntries.AnyAsync(x =>
                x.TenantId == tenantSlug && x.RuleId == ruleId && x.ContactKey == contactKey &&
                (x.Reason == LedgerReason.Earn || x.Reason == LedgerReason.StampEarn), ct))
            return true;

        var tenantGuid = await db.Tenants.Where(t => t.Slug == tenantSlug).Select(t => t.Id).FirstOrDefaultAsync(ct);
        return await db.HeldPostings.AnyAsync(h =>
            h.TenantId == tenantGuid && h.RuleId == ruleId && h.ContactKey == contactKey && h.CancelledAt == null, ct);
    }
}
