using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;

namespace dEngage.Loyalty.Schema;

// Translates the external tenant slug (API URLs, JWT claims, RabbitMQ envelopes, X-Api-Key
// prefixes — all unchanged by the Guid-surrogate-key migration) into the internal tenants.id
// Guid that every other tenant-scoped table now stores as its FK, and back again where an
// internal Guid-typed call site needs to reconstruct an outbound slug (e.g. publishing to
// RabbitMQ). Tenants are few and effectively static once created, so an in-memory cache is
// safe and keeps this off any real hot path despite CustomerAccount/Program lookups happening
// per-event.
public interface ITenantSlugResolver
{
    Task<Guid> ResolveAsync(string slug, CancellationToken ct);
    Task<string> ResolveSlugAsync(Guid id, CancellationToken ct);

    // Called right after a tenant is created so it's resolvable with no DB round-trip.
    void Seed(string slug, Guid id);
}

// Singleton, DbContext-free — safe to inject into any service regardless of lifetime.
public sealed class TenantSlugCache
{
    private readonly ConcurrentDictionary<string, Guid> _bySlug = new();
    private readonly ConcurrentDictionary<Guid, string> _byId = new();

    public bool TryGet(string slug, out Guid id) => _bySlug.TryGetValue(slug, out id);
    public bool TryGet(Guid id, out string? slug) => _byId.TryGetValue(id, out slug);

    public void Set(string slug, Guid id)
    {
        _bySlug[slug] = id;
        _byId[id] = slug;
    }
}

// Scoped (same lifetime as LoyaltyDbContext) — the cache it reads/writes is the singleton above.
public sealed class TenantSlugResolver(LoyaltyDbContext db, TenantSlugCache cache) : ITenantSlugResolver
{
    public async Task<Guid> ResolveAsync(string slug, CancellationToken ct)
    {
        if (cache.TryGet(slug, out var cached))
            return cached;

        var id = await db.Tenants.Where(t => t.Slug == slug).Select(t => t.Id).FirstOrDefaultAsync(ct);
        if (id == Guid.Empty)
            throw new InvalidOperationException($"tenant_not_found: '{slug}'");

        cache.Set(slug, id);
        return id;
    }

    public async Task<string> ResolveSlugAsync(Guid id, CancellationToken ct)
    {
        if (cache.TryGet(id, out var cachedSlug) && cachedSlug is not null)
            return cachedSlug;

        var slug = await db.Tenants.Where(t => t.Id == id).Select(t => t.Slug).FirstOrDefaultAsync(ct);
        if (slug is null)
            throw new InvalidOperationException($"tenant_not_found: '{id}'");

        cache.Set(slug, id);
        return slug;
    }

    public void Seed(string slug, Guid id) => cache.Set(slug, id);
}
