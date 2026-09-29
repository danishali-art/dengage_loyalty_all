using System.Text.Json;
using dEngage.Loyalty.RuleEngine;
using dEngage.Loyalty.RuleEngine.Models;
using dEngage.Loyalty.Schema;
using dEngage.Loyalty.Shared;
using dEngage.Loyalty.Shared.Events;
using Microsoft.EntityFrameworkCore;

namespace dEngage.Loyalty.Consumer;

public class CampaignEvaluationService(IRuleEngine ruleEngine, LoyaltyDbContext db, ITenantSlugResolver tenantSlugResolver) : ICampaignEvaluationService
{
    public async Task EvaluateAsync(EventEnvelope envelope, CancellationToken ct)
    {
        if (envelope.Data.ValueKind != JsonValueKind.Object ||
            !envelope.Data.TryGetProperty("contact_key", out var ck) ||
            ck.ValueKind != JsonValueKind.String ||
            string.IsNullOrEmpty(ck.GetString()))
            return; // no contact to attribute earnings to (e.g. order.refunded)

        var evt = EvaluationEvent.FromEnvelope(envelope);

        var tenantGuid = await tenantSlugResolver.ResolveAsync(envelope.Tenant, ct);
        var programs = await db.Programs
            // 1.3.CL item 8: only published + active programs run — a draft is never evaluated.
            .Where(p => p.TenantId == tenantGuid && p.Status == ProgramStatus.Active
                        && p.PublicationStatus == ProgramPublicationStatus.Published)
            .ToListAsync(ct);

        foreach (var program in programs)
            await ruleEngine.ProcessEventAsync(envelope.Tenant, program.Id, envelope.EventId, evt, ct);
    }
}
