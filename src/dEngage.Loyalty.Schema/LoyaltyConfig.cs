using System.Collections.Concurrent;
using System.Text.Json;
using dEngage.Loyalty.Shared;
using dEngage.Loyalty.Shared.Security;
using Microsoft.EntityFrameworkCore;

namespace dEngage.Loyalty.Schema;

// Reads the loyalty settings (platform alignment D4). The tables are the only source — no
// defaults in code or appsettings — and the two kinds never mix:
//   - GetAsync: a global setting, from loyalty_config;
//   - GetForTenantAsync: a tenant setting (LoyaltyConfigKeys.TenantSettings), from the tenant's own
//     row in loyalty_tenant_config. There is no global fallback.
// A missing or blank row fails with loyalty_config_missing. Same access shape as the dEngage
// platform's GlobalConfig.GetValueFromModuleCache: a whole module is loaded and cached briefly.
public interface ILoyaltyConfig
{
    // T: string, a number, bool, or anything JSON can bind (e.g. string[], int[]). The row's
    // value_json is used when set, otherwise its value. A row that can't be read as T fails with
    // loyalty_config_invalid. Asking for a tenant setting here is a programming error.
    Task<T> GetAsync<T>(string module, string parameter, CancellationToken ct);

    // Same, for a tenant setting of one tenant. Asking for a global setting here is a programming error.
    Task<T> GetForTenantAsync<T>(string module, string parameter, Guid tenantId, CancellationToken ct);
}

// Singleton, DbContext-free — holds the last load of each module for `Ttl`. Rows are changed with
// SQL (like sys.global_config), so a change is seen by every Api and Consumer instance within
// one TTL; there is no application write path that would need an invalidation broadcast.
public sealed class LoyaltyConfigCache(TimeProvider clock)
{
    public static readonly TimeSpan Ttl = TimeSpan.FromMinutes(1);

    // Key: the module for global settings, "<tenant id>|<module>" for one tenant's settings.
    private readonly ConcurrentDictionary<string, ModuleSnapshot> _modules = new();

    public LoyaltyConfigCache() : this(TimeProvider.System) { }

    internal bool TryGet(string key, out ModuleSnapshot snapshot) =>
        _modules.TryGetValue(key, out snapshot!) && clock.GetUtcNow() - snapshot.LoadedAt < Ttl;

    internal ModuleSnapshot Set(string key, IReadOnlyDictionary<string, string?> values)
    {
        var snapshot = new ModuleSnapshot(clock.GetUtcNow(), values);
        _modules[key] = snapshot;
        return snapshot;
    }

    internal sealed record ModuleSnapshot(DateTimeOffset LoadedAt, IReadOnlyDictionary<string, string?> Values);
}

// Scoped (same lifetime as LoyaltyDbContext) — the cache it fills is the singleton above.
public sealed class LoyaltyConfigReader(LoyaltyDbContext db, LoyaltyConfigCache cache, ISecretProtector secrets)
    : ILoyaltyConfig
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public async Task<T> GetAsync<T>(string module, string parameter, CancellationToken ct)
    {
        if (LoyaltyConfigKeys.IsTenantSetting(module, parameter))
            throw new InvalidOperationException($"{module}/{parameter} is a tenant setting; read it with GetForTenantAsync.");

        if (!cache.TryGet(module, out var snapshot))
            snapshot = cache.Set(module, await LoadAsync(db.LoyaltyConfig.AsNoTracking()
                .Where(x => x.ModuleName == module)
                .Select(x => new Row(x.ParameterName, x.Value, x.ValueJson, x.IsSecret)), ct));

        if (!snapshot.Values.TryGetValue(parameter, out var raw) || string.IsNullOrWhiteSpace(raw))
            throw new InvalidOperationException(
                $"loyalty_config_missing: no value for {module}/{parameter} in loyalty_config. " +
                "Every global setting must have a row (seeded by migration, see LoyaltyConfigDefaults.Global).");

        return Parse<T>(module, parameter, raw);
    }

    public async Task<T> GetForTenantAsync<T>(string module, string parameter, Guid tenantId, CancellationToken ct)
    {
        if (!LoyaltyConfigKeys.IsTenantSetting(module, parameter))
            throw new InvalidOperationException($"{module}/{parameter} is a global setting; read it with GetAsync.");

        var key = $"{tenantId}|{module}";
        if (!cache.TryGet(key, out var snapshot))
            snapshot = cache.Set(key, await LoadAsync(db.LoyaltyTenantConfig.AsNoTracking()
                .Where(x => x.TenantId == tenantId && x.ModuleName == module)
                .Select(x => new Row(x.ParameterName, x.Value, x.ValueJson, x.IsSecret)), ct));

        if (!snapshot.Values.TryGetValue(parameter, out var raw) || string.IsNullOrWhiteSpace(raw))
            throw new InvalidOperationException(
                $"loyalty_config_missing: no value for {module}/{parameter} for tenant {tenantId} in loyalty_tenant_config. " +
                "Every tenant needs a row for each tenant setting (written when the tenant is created, see LoyaltyConfigDefaults.NewTenant).");

        return Parse<T>(module, parameter, raw);
    }

    private static T Parse<T>(string module, string parameter, string raw)
    {
        try
        {
            if (typeof(T) == typeof(string) && !LooksLikeJsonString(raw))
                return (T)(object)raw;
            return JsonSerializer.Deserialize<T>(raw, JsonOptions)
                ?? throw new JsonException("The value is JSON null.");
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException(
                $"loyalty_config_invalid: {module}/{parameter} can't be read as {typeof(T).Name}.", ex);
        }
    }

    private sealed record Row(string ParameterName, string? Value, string? ValueJson, bool IsSecret);

    private async Task<IReadOnlyDictionary<string, string?>> LoadAsync(IQueryable<Row> query, CancellationToken ct)
    {
        var values = new Dictionary<string, string?>();
        foreach (var row in await query.ToListAsync(ct))
        {
            // value_json wins (structured values); secrets are scalar ENC(...) values.
            values[row.ParameterName] = row.IsSecret
                ? (row.Value is null ? null : secrets.Unprotect(row.Value))
                : row.ValueJson ?? row.Value;
        }
        return values;
    }

    // A string stored in value_json arrives JSON-encoded ("\"abc\""); one stored in value doesn't.
    private static bool LooksLikeJsonString(string raw) => raw.Length >= 2 && raw[0] == '"' && raw[^1] == '"';
}
