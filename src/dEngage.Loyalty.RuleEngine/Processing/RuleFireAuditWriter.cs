using System.Text.Json;
using dEngage.Loyalty.RuleEngine.Models;
using dEngage.Loyalty.Schema;
using dEngage.Loyalty.Schema.Entities;
using Microsoft.EntityFrameworkCore;

namespace dEngage.Loyalty.RuleEngine.Processing;

public sealed class RuleFireAuditWriter(LoyaltyDbContext db, ITenantSlugResolver tenantSlugResolver) : IRuleFireAuditWriter
{
    public async Task RecordAsync(
        string tenantId,
        Guid ruleId,
        int ruleVersion,
        string sourceEventId,
        string contactKey,
        ConditionTree? conditions,
        RuleCalculation calculation,
        decimal resultingDelta,
        Guid? ledgerEntryId,
        string? resolutionSnapshot,
        CancellationToken ct)
    {
        var tenantGuid = await tenantSlugResolver.ResolveAsync(tenantId, ct);
        var exists = await db.RuleFireAudits.AnyAsync(x =>
            x.TenantId == tenantGuid && x.SourceEventId == sourceEventId && x.RuleId == ruleId, ct);
        if (exists) return;

        db.RuleFireAudits.Add(new RuleFireAudit
        {
            Id = UUIDNext.Uuid.NewDatabaseFriendly(UUIDNext.Database.PostgreSql),
            TenantId = tenantGuid,
            RuleId = ruleId,
            RuleVersion = ruleVersion,
            SourceEventId = sourceEventId,
            ContactKey = contactKey,
            ConditionsSnapshot = conditions is null ? null : JsonSerializer.Serialize(conditions),
            CalculationSnapshot = JsonSerializer.Serialize(calculation),
            ResultingDelta = resultingDelta,
            LedgerEntryId = ledgerEntryId,
            ResolutionSnapshot = resolutionSnapshot,
            CreatedAt = DateTime.UtcNow
        });
    }
}
