using dEngage.Loyalty.Schema.Entities;
using dEngage.Loyalty.Shared;
using Microsoft.EntityFrameworkCore;

namespace dEngage.Loyalty.Schema;

// The starting value of every setting (platform alignment D4). The tables are the only source at
// runtime: there are no defaults in code, so every setting must exist as a row.
//   - Global: one row each in loyalty_config. Real databases get them from the migrations (their
//     values are frozen copies of this list); databases built without migrations (Sqlite tests,
//     dev tools) call EnsureSeededAsync. LoyaltyConfigE2ETests checks migrated rows == Global.
//   - NewTenant: the rows written into loyalty_tenant_config for each tenant when it is created
//     (PlatformAppService) — a provisioning template, never read as a fallback. Existing tenants
//     were backfilled by migration LoyaltyTenantConfigAlignD4.
// A new global setting needs an entry here and a row in a new migration; a new tenant setting
// needs an entry here, the key in LoyaltyConfigKeys.TenantSettings and a migration that backfills
// existing tenants.
public static class LoyaltyConfigDefaults
{
    public sealed record Setting(string Module, string Parameter, string? Value, string? ValueJson, string Description);

    public static readonly IReadOnlyList<Setting> NewTenant =
    [
        // 120 = the previous production value; raise it per tenant as needed.
        new(LoyaltyConfigKeys.Api.Module, LoyaltyConfigKeys.Api.RateLimitEventIngestionPerMinute, "120", null,
            "Events accepted per minute for this tenant."),
        new(LoyaltyConfigKeys.Api.Module, LoyaltyConfigKeys.Api.PiiExtraMaskedFields, null, "[]",
            "Extra field names masked in Customer 360 event data for this tenant, on top of the built-in list (additive)."),
    ];

    public static readonly IReadOnlyList<Setting> Global =
    [
        new(LoyaltyConfigKeys.Api.Module, LoyaltyConfigKeys.Api.Customer360MessageWindowBeforeMinutes, "1", null,
            "Minutes before an event in which its sent messages are looked up (Customer 360)."),
        new(LoyaltyConfigKeys.Api.Module, LoyaltyConfigKeys.Api.Customer360MessageWindowAfterMinutes, "60", null,
            "Minutes after an event in which its sent messages are looked up (Customer 360)."),
        new(LoyaltyConfigKeys.Engine.Module, LoyaltyConfigKeys.Engine.RuleCacheTtlMinutes, "60", null,
            "Redis safety-net TTL of the rule cache, in minutes. Freshness comes from RuleSync."),
        new(LoyaltyConfigKeys.Engine.Module, LoyaltyConfigKeys.Engine.CampaignConfigCacheTtlMinutes, "60", null,
            "Redis safety-net TTL of the campaign config cache, in minutes."),
        new(LoyaltyConfigKeys.Jobs.Module, LoyaltyConfigKeys.Jobs.OutboxBatchSize, "100", null,
            "Outbox rows published per batch."),
        new(LoyaltyConfigKeys.Jobs.Module, LoyaltyConfigKeys.Jobs.OutboxMaxAttempts, "8", null,
            "Publish attempts before an outbox row is marked failed."),
        new(LoyaltyConfigKeys.Jobs.Module, LoyaltyConfigKeys.Jobs.OutboxBackoffMinutes, null, "[1, 5, 30, 120, 720]",
            "Minutes to wait before each retry of an outbox row; the last value repeats."),
        new(LoyaltyConfigKeys.Jobs.Module, LoyaltyConfigKeys.Jobs.DelayedPostingBatchSize, "500", null,
            "Held postings promoted per hourly run; the next run picks up the rest."),
        new(LoyaltyConfigKeys.Jobs.Module, LoyaltyConfigKeys.Jobs.EventLogRetentionBatchSize, "10000", null,
            "event_log rows deleted per batch by the nightly retention job."),
    ];

    // Adds the global settings that have no row yet; existing rows are left as they are. For
    // databases created without migrations only (tests, dev tools) — real databases are seeded by
    // migrations.
    public static async Task EnsureSeededAsync(LoyaltyDbContext db, CancellationToken ct = default)
    {
        var existing = (await db.LoyaltyConfig.AsNoTracking()
                .Select(x => new { x.ModuleName, x.ParameterName })
                .ToListAsync(ct))
            .Select(x => (x.ModuleName, x.ParameterName))
            .ToHashSet();

        foreach (var s in Global.Where(s => !existing.Contains((s.Module, s.Parameter))))
        {
            db.LoyaltyConfig.Add(new LoyaltyConfigEntry
            {
                ModuleName = s.Module, ParameterName = s.Parameter, Value = s.Value, ValueJson = s.ValueJson,
                Description = s.Description, UpdatedAt = DateTime.UtcNow, UpdatedBy = "seed:LoyaltyConfigDefaults"
            });
        }
        await db.SaveChangesAsync(ct);
    }

    // The rows a new tenant starts with (see NewTenant). Added to the caller's DbContext; the caller saves.
    public static IEnumerable<LoyaltyTenantConfigEntry> NewTenantRows(Guid tenantId) =>
        NewTenant.Select(s => new LoyaltyTenantConfigEntry
        {
            Id = Guid.NewGuid(), TenantId = tenantId, ModuleName = s.Module, ParameterName = s.Parameter,
            Value = s.Value, ValueJson = s.ValueJson, Description = s.Description,
            UpdatedAt = DateTime.UtcNow, UpdatedBy = "tenant-provisioning"
        });
}
