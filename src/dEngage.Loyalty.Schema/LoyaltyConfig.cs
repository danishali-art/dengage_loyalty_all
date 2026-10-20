using System.Collections.Concurrent;
using System.Text.Json;
using dEngage.Loyalty.Shared.Security;
using Microsoft.EntityFrameworkCore;

namespace dEngage.Loyalty.Schema;

// Reads loyalty_config (platform alignment D4). Resolution order: the tenant's row, then the
// global row, then the default passed by the caller (the value that used to be hard-coded), so
// behaviour only changes once a row exists. Same access shape as the dEngage platform's
// GlobalConfig.GetValueFromModuleCache: a whole module is loaded at once and cached briefly.
public interface ILoyaltyConfig
{
    // T: string, a number, bool, or anything JSON can bind (e.g. string[], int[]). The row's
    // value_json is used when set, otherwise its value. A row that can't be read as T throws
    // loyalty_config_invalid instead of silently falling back to the default.
    Task<T> GetAsync<T>(string module, string parameter, T defaultValue, Guid? tenantId, CancellationToken ct);
}

// Singleton, DbContext-free — holds the last load of each module for `Ttl`. Rows are changed with
// SQL (like sys.global_config), so a change is seen by every Api and Consumer instance within
// one TTL; there is no application write path that would need an invalidation broadcast.
public sealed class LoyaltyConfigCache(TimeProvider clock)
{
    public static readonly TimeSpan Ttl = TimeSpan.FromMinutes(1);

    private readonly ConcurrentDictionary<string, ModuleSnapshot> _modules = new();

    public LoyaltyConfigCache() : this(TimeProvider.System) { }

    internal bool TryGet(string module, out ModuleSnapshot snapshot) =>
        _modules.TryGetValue(module, out snapshot!) && clock.GetUtcNow() - snapshot.LoadedAt < Ttl;

    internal ModuleSnapshot Set(string module, IReadOnlyDictionary<(string Parameter, Guid? TenantId), string?> values)
    {
        var snapshot = new ModuleSnapshot(clock.GetUtcNow(), values);
        _modules[module] = snapshot;
        return snapshot;
    }

    internal sealed record ModuleSnapshot(
        DateTimeOffset LoadedAt,
        IReadOnlyDictionary<(string Parameter, Guid? TenantId), string?> Values);
}

// Scoped (same lifetime as LoyaltyDbContext) — the cache it fills is the singleton above.
public sealed class LoyaltyConfigReader(LoyaltyDbContext db, LoyaltyConfigCache cache, ISecretProtector secrets)
    : ILoyaltyConfig
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public async Task<T> GetAsync<T>(string module, string parameter, T defaultValue, Guid? tenantId, CancellationToken ct)
    {
        if (!cache.TryGet(module, out var snapshot))
            snapshot = cache.Set(module, await LoadModuleAsync(module, ct));

        string? raw = null;
        var found = tenantId is { } t && snapshot.Values.TryGetValue((parameter, t), out raw);
        if (!found)
            found = snapshot.Values.TryGetValue((parameter, null), out raw);
        if (!found || string.IsNullOrWhiteSpace(raw))
            return defaultValue;

        try
        {
            if (typeof(T) == typeof(string) && !LooksLikeJsonString(raw))
                return (T)(object)raw;
            return JsonSerializer.Deserialize<T>(raw, JsonOptions) ?? defaultValue;
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException(
                $"loyalty_config_invalid: {module}/{parameter} ({(tenantId is null ? "global" : $"tenant {tenantId}")}) " +
                $"can't be read as {typeof(T).Name}.", ex);
        }
    }

    private async Task<IReadOnlyDictionary<(string, Guid?), string?>> LoadModuleAsync(string module, CancellationToken ct)
    {
        var rows = await db.LoyaltyConfig.AsNoTracking()
            .Where(x => x.ModuleName == module)
            .Select(x => new { x.ParameterName, x.TenantId, x.Value, x.ValueJson, x.IsSecret })
            .ToListAsync(ct);

        var values = new Dictionary<(string, Guid?), string?>();
        foreach (var row in rows)
        {
            // value_json wins (structured values); secrets are scalar ENC(...) values.
            var raw = row.IsSecret
                ? (row.Value is null ? null : secrets.Unprotect(row.Value))
                : row.ValueJson ?? row.Value;
            values[(row.ParameterName, row.TenantId)] = raw;
        }
        return values;
    }

    // A string stored in value_json arrives JSON-encoded ("\"abc\""); one stored in value doesn't.
    private static bool LooksLikeJsonString(string raw) => raw.Length >= 2 && raw[0] == '"' && raw[^1] == '"';
}
