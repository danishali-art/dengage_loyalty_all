using dEngage.Loyalty.Schema;
using dEngage.Loyalty.Shared;
using Microsoft.EntityFrameworkCore;

namespace dEngage.Loyalty.RuleEngine.Processing;

// The one definition of a "live" program (1.3.CL item 8): status Active AND publication Published.
// Rule evaluation picks live programs the same way (CampaignEvaluationService, BirthdayBonusJob,
// RuleSyncService); CR 2026-10-05 item 5 makes the per-event handlers that post directly
// (redeem, transfer, reward purchase, cash.added / cash.spent) refuse non-live programs with it.
public static class ProgramLiveness
{
    public static Task<bool> IsProgramLiveAsync(this LoyaltyDbContext db, Guid tenantGuid, Guid programId, CancellationToken ct) =>
        db.Programs.AnyAsync(p =>
            p.TenantId == tenantGuid && p.Id == programId &&
            p.Status == ProgramStatus.Active && p.PublicationStatus == ProgramPublicationStatus.Published, ct);
}
