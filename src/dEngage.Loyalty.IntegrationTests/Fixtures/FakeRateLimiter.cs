using dEngage.Loyalty.Api.Framework.RateLimiting;

namespace dEngage.Loyalty.IntegrationTests.Fixtures;

// Always grants — stands in for RedisRateLimiter so the test host needs no real Redis.
public sealed class FakeRateLimiter : IRateLimiter
{
    public Task<bool> TryAcquireAsync(string bucket, string tenantId, int limitPerMinute, CancellationToken ct) =>
        Task.FromResult(true);
}
