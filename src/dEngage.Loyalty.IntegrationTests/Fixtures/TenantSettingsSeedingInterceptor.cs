using dEngage.Loyalty.Schema;
using dEngage.Loyalty.Schema.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace dEngage.Loyalty.IntegrationTests.Fixtures;

// Tenant settings have no global fallback (alignment D4): every tenant needs its own
// loyalty_tenant_config rows, which PlatformAppService.CreateTenantAsync writes. Many tests insert
// tenants straight into the database instead, so the test hosts give each newly added tenant the
// same starting rows here. A tenant whose rows are already being added (the API path) is left alone.
public sealed class TenantSettingsSeedingInterceptor : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        AddMissingTenantSettings(eventData.Context);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        AddMissingTenantSettings(eventData.Context);
        return ValueTask.FromResult(result);
    }

    private static void AddMissingTenantSettings(DbContext? context)
    {
        if (context is not LoyaltyDbContext db) return;

        var newTenants = db.ChangeTracker.Entries<Tenant>()
            .Where(e => e.State == EntityState.Added)
            .Select(e => e.Entity.Id)
            .ToList();
        if (newTenants.Count == 0) return;

        var alreadyAdded = db.ChangeTracker.Entries<LoyaltyTenantConfigEntry>()
            .Where(e => e.State == EntityState.Added)
            .Select(e => e.Entity.TenantId)
            .ToHashSet();

        foreach (var tenantId in newTenants.Where(id => !alreadyAdded.Contains(id)))
            db.LoyaltyTenantConfig.AddRange(LoyaltyConfigDefaults.NewTenantRows(tenantId));
    }
}
