using dEngage.Loyalty.Schema;
using Microsoft.EntityFrameworkCore;
// ITenantScopedEntity lives in dEngage.Loyalty.Schema (see that file for why).

namespace dEngage.Loyalty.Api.Framework.Data;

// One generic implementation shared by every tenant-scoped admin-CRUD aggregate (Repository
// pattern per plan §2 — deliberately not one hand-written class per entity, since the access
// shape is identical: tenant-filtered query, find-by-id, add, remove).
//
// tenantId parameters here are the external slug (unchanged) — TEntity.TenantId is the internal
// Guid every tenant-scoped table's FK targets. Query/FindAsync resolve slug -> Guid via
// ITenantSlugResolver internally, so every app service upstream keeps passing the plain string
// it already had from the route, with no signature changes of its own.
public interface IRepository<TEntity> where TEntity : class, ITenantScopedEntity
{
    Task<IQueryable<TEntity>> Query(string tenantId, CancellationToken ct);
    Task<TEntity?> FindAsync(string tenantId, Guid id, CancellationToken ct);
    Task AddAsync(TEntity entity, CancellationToken ct);
    void Remove(TEntity entity);
    Task<int> SaveChangesAsync(CancellationToken ct);
}

public sealed class EfRepository<TEntity>(LoyaltyDbContext db, ITenantSlugResolver tenantSlugResolver) : IRepository<TEntity>
    where TEntity : class, ITenantScopedEntity
{
    public async Task<IQueryable<TEntity>> Query(string tenantId, CancellationToken ct)
    {
        var tenantGuid = await tenantSlugResolver.ResolveAsync(tenantId, ct);
        return db.Set<TEntity>().Where(e => e.TenantId == tenantGuid);
    }

    public async Task<TEntity?> FindAsync(string tenantId, Guid id, CancellationToken ct) =>
        await (await Query(tenantId, ct)).FirstOrDefaultAsync(e => e.Id == id, ct);

    public async Task AddAsync(TEntity entity, CancellationToken ct) =>
        await db.Set<TEntity>().AddAsync(entity, ct);

    public void Remove(TEntity entity) => db.Set<TEntity>().Remove(entity);

    public Task<int> SaveChangesAsync(CancellationToken ct) => db.SaveChangesAsync(ct);
}
