using System.Text.Json;
using dEngage.Loyalty.Schema;

namespace dEngage.Loyalty.IntegrationTests.Fixtures;

// For tests that construct services by hand: answers like a freshly provisioned database — global
// settings from LoyaltyConfigDefaults.Global, tenant settings from LoyaltyConfigDefaults.NewTenant
// (what every tenant starts with). Tests of the tables themselves use LoyaltyConfigReader.
public sealed class DefaultLoyaltyConfig : ILoyaltyConfig
{
    public Task<T> GetAsync<T>(string module, string parameter, CancellationToken ct) =>
        Task.FromResult(Read<T>(LoyaltyConfigDefaults.Global, module, parameter));

    public Task<T> GetForTenantAsync<T>(string module, string parameter, Guid tenantId, CancellationToken ct) =>
        Task.FromResult(Read<T>(LoyaltyConfigDefaults.NewTenant, module, parameter));

    private static T Read<T>(IReadOnlyList<LoyaltyConfigDefaults.Setting> settings, string module, string parameter)
    {
        var setting = settings.Single(s => s.Module == module && s.Parameter == parameter);
        return typeof(T) == typeof(string) && setting.ValueJson is null
            ? (T)(object)setting.Value!
            : JsonSerializer.Deserialize<T>(setting.ValueJson ?? setting.Value!)!;
    }
}
