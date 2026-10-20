using dEngage.Loyalty.Schema;

namespace dEngage.Loyalty.IntegrationTests.Fixtures;

// For tests that construct services by hand: an empty loyalty_config, i.e. every setting keeps
// its default in code (alignment D4). Tests of the table itself use LoyaltyConfigReader.
public sealed class DefaultLoyaltyConfig : ILoyaltyConfig
{
    public Task<T> GetAsync<T>(string module, string parameter, T defaultValue, Guid? tenantId, CancellationToken ct) =>
        Task.FromResult(defaultValue);
}
